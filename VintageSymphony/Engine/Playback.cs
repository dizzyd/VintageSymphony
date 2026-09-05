using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using VintageSymphony.Situations;

namespace VintageSymphony.Engine;

public class Playback
{
	private readonly ILogger logger;
	private readonly TrackCooldownManager trackCooldownManager;
	private readonly Func<TrackedPlayerProperties> getPlayerProperties;
	private readonly Func<long> getCurrentTimeMs;
	private readonly TrackRestrictionMatcher trackRestrictionMatcher;

	public MusicTrack? CurrentTrack { get; private set; }
	public Playlist? CurrentPlaylist { get; private set; }

	public bool IsPaused => Pause.Active;

	public readonly Pause Pause;

	private static readonly float[][] PauseDurations =
	{
		new[] { 960f, 480f },
		new[] { 420f, 240f },
		new[] { 180f, 120f },
		new float[2]
	};

	private int musicFrequency = 2; // Default middle value

	/// <summary>
	/// How long a track stays out of the running after it played, however often the player
	/// asked for music. The pause table above is all zeroes at the highest frequency -
	/// deliberately, that setting means continuous music - and deriving the cooldown from
	/// it left no cooldown at all, so the few highest-priority tracks that fit the moment
	/// repeated between themselves. The game's own same-song cooldown never drops below
	/// eight minutes either.
	/// </summary>
	private const long MinimumTrackCooldownMs = 8L * 60L * 1000L;

	/// <summary>
	/// The shortest silence <see cref="Stop"/> will hold. Same reason: at the highest music
	/// frequency the ordinary between-track pause is zero, and a stop that borrowed it was
	/// followed by a new track on the very next tick.
	/// </summary>
	private const long MinimumManualPauseMs = 180L * 1000L;

	/// <summary>Slack on top of a fade's own length before it is treated as over.</summary>
	private const long FadeOutGraceMs = 500L;

	/// <summary>
	/// Tracks told to fade out and not yet silent, with the moment their fade is due to be
	/// over. A faded track is not something the engine may forget: the sound is still
	/// audible for the length of the fade, and nothing else in the mod holds a reference to
	/// it. Only <see cref="Update"/> touches this, so the game's audio threads cannot race
	/// with it.
	/// </summary>
	private readonly Dictionary<MusicTrack, long> fadingOut = new();

	/// <summary>Something is still audible from a track that has been stopped.</summary>
	public bool IsFadingOut => fadingOut.Count > 0;

	/// <summary>
	/// A skip is outstanding: someone asked for the next track and the previous one is
	/// still fading. Held so the wait for the fade cannot be swallowed by a pause that
	/// something else starts in the meantime - a playlist switch, say.
	/// </summary>
	private bool startWhenQuiet;

	/// <summary>The track that played last, kept so the next one need not be it again.</summary>
	private MusicTrack? lastPlayedTrack;

	private long currentTrackStartedMs;

	/// <summary>
	/// When the sounding track started, or null while nothing is sounding. The curator
	/// reads it to give a track its minimum play time before switching away.
	/// </summary>
	public long? PlayingSinceMs => IsPlayingTrack() ? currentTrackStartedMs : null;

	public Playback(
		ILogger logger,
		TrackCooldownManager trackCooldownManager,
		Func<TrackedPlayerProperties> getPlayerProperties,
		Func<long> getCurrentTimeMs)
	{
		this.logger = logger;
		this.trackCooldownManager = trackCooldownManager;
		this.getPlayerProperties = getPlayerProperties;
		this.getCurrentTimeMs = getCurrentTimeMs;
		this.trackRestrictionMatcher = new TrackRestrictionMatcher(VintageSymphony.ClientApi.World.Calendar);

		Pause = new Pause(getCurrentTimeMs);

		// Start with initial pauses
		Pause.Start(GetPauseDuration());
	}

	public void Update(float dt)
	{
		ForgetFinishedFadeOuts();
		MonitorCurrentTrack();
		AutoEnqueueNextTrack();
	}

	public void SetMusicFrequency(int frequency)
	{
		musicFrequency = Math.Clamp(frequency, 0, PauseDurations.Length - 1);
		var cooldown = (long)(10 * PauseDurations[musicFrequency][0] * 1000L);
		trackCooldownManager.SetCooldownDuration(Math.Max(cooldown, MinimumTrackCooldownMs));

		if (Pause.Active)
		{
			Pause.UpdateDuration(GetPauseDuration());
		}
	}

	public bool CanPlayMusic()
	{
		return !CurrentTrack?.IsPlaying ?? !IsPaused;
	}

	public bool IsPlayingTrack()
	{
		return CurrentTrack is { IsPlaying: true };
	}

	public void Play(Playlist playlist)
	{
		var oldPlaylist = CurrentPlaylist;
		var oldAttributes = oldPlaylist?.Situation.Attributes();
		var newAttributes = playlist.Situation.Attributes();
		CurrentPlaylist = playlist;

		if (newAttributes.DynamicSituation)
		{
			NextTrack();
			return;
		}

		if (IsPlayingTrack() && oldAttributes is { DynamicSituation: true })
		{
			StopTrackAndPause();
			return;
		}

		if (oldAttributes is { PauseAfterPlayback: true })
		{
			Pause.Start(GetPauseDuration());
		}
	}

	[SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
	private MusicTrack? FindNextTrack()
	{
		if (CurrentPlaylist == null)
			return null;

		var filteredTracks = CurrentPlaylist.GetTracks(TrackFilterPredicate);

		return TrackSelector.Select(filteredTracks.Where(track => !trackCooldownManager.IsOnCooldown(track)))
		       // Everything that fits has played recently. Going round again beats sitting
		       // in silence, but not straight back into the track that just finished - a
		       // skip that plays the same song again reads as a broken skip. The game's own
		       // engine refuses its LastPlayedTrack for the same reason.
		       ?? TrackSelector.Select(filteredTracks.Where(track => track != lastPlayedTrack))
		       // Unless it really is the only thing that fits.
		       ?? TrackSelector.Select(filteredTracks);
	}

	private bool TrackFilterPredicate(MusicTrack track)
	{
		// Never what is already sounding. The cooldown list cannot stand in for this: its
		// duration is a setting, and the fallback above ignores it outright once everything
		// that fits has played. Picking the playing track means BeginPlay on a track that
		// still owns a live sound, which is the surest way to end up with two of them.
		if (track == CurrentTrack || fadingOut.ContainsKey(track))
		{
			return false;
		}

		return IsEligible(track, Here());
	}

	/// <summary>
	/// Does this playlist hold anything that may play right now? The curator asks this
	/// before it selects a playlist: a playlist with tracks is not a playlist with music,
	/// and choosing one whose every track is barred - the game's one Danger track plays
	/// only inside the Resonance Archive - meant silence for as long as its situation led.
	/// The sounding and fading tracks count; they are this playlist's music too.
	/// </summary>
	public bool HasPlayableTrack(Playlist playlist)
	{
		var here = Here();
		return playlist.Tracks.Any(track => IsEligible(track, here));
	}

	private (TrackedPlayerProperties props, ClimateCondition climate, BlockPos position) Here()
	{
		var position = VintageSymphony.ClientApi.World.Player.Entity.Pos.AsBlockPos;
		var climate = VintageSymphony.ClientApi.World.BlockAccessor.GetClimateAt(position);
		return (getPlayerProperties(), climate, position);
	}

	/// <summary>
	/// Wrapped tracks keep the game's own eligibility rules - playlist, structure,
	/// temporal stability, hours - along with the game's cooldowns and hour windows.
	/// The wrapper carries none of those fields, so testing our restrictions against
	/// it passed everything: the village tracks, priority 1.5 and only meant to play
	/// inside a village, won nearly every draw, and the game's ContinuePlay faded each
	/// one out a second in. Custom tracks use our own restrictions and cooldowns.
	/// </summary>
	private bool IsEligible(MusicTrack track, (TrackedPlayerProperties props, ClimateCondition climate, BlockPos position) here)
	{
		if (track is MusicTrackWrapper)
		{
			return track.ShouldPlay(here.props, here.climate, here.position);
		}

		return trackRestrictionMatcher.IsWithinConfiguredRestrictions(track, here.props,
			here.climate, here.position);
	}

	/// <summary>
	/// Forget what is playing and what was selected. The playlists are rebuilt when the
	/// track pool changes, so both the current track and the playlist holding it can
	/// cease to exist; clearing the pause too lets the curator start again immediately
	/// instead of sitting out a pause that belonged to the old selection.
	/// </summary>
	public void Reset()
	{
		StopTrack();
		CurrentPlaylist = null;
		Pause.Stop();
	}

	public void NextTrack()
	{
		StopTrack();
		Pause.Stop();
		startWhenQuiet = true;
		TryStartNextTrack();
	}

	/// <summary>
	/// Stop the music and hold silence, which is what /music stop is asking for. The
	/// automatic pause is not enough on its own - see <see cref="MinimumManualPauseMs"/>.
	/// Returns how long the silence will last, in seconds.
	/// </summary>
	public int Stop(float fadeOutTimeS = 2f)
	{
		StopTrack(fadeOutTimeS);
		Pause.Start(Math.Max(GetPauseDuration(), MinimumManualPauseMs));
		return Pause.GetRemainingTimeS();
	}

	/// <summary>
	/// Start something, unless a stopped track is still audible. The waiting is the point:
	/// the fade out is two seconds long, and starting regardless is what laid one track
	/// over another every time the situation changed or /music next was typed.
	/// </summary>
	private void TryStartNextTrack()
	{
		if (CurrentTrack != null || IsFadingOut)
		{
			return;
		}

		// The wait is over either way: a skip that finds nothing to play is a skip that
		// happened, not one still owed.
		startWhenQuiet = false;

		var track = FindNextTrack();
		if (track != null)
		{
			PlayTrack(track);
		}
	}

	private void PlayTrack(MusicTrack track)
	{
		lastPlayedTrack = track;
		CurrentTrack = track;
		currentTrackStartedMs = getCurrentTimeMs();
		CurrentTrack.BeginPlay(getPlayerProperties());
		if (!track.DisableCooldown)
		{
			trackCooldownManager.PutOnCooldown(CurrentTrack);
		}

		logger.Notification($"Playing track: {track.Name}");
	}

	public void StopTrack(float fadeOutTimeS = 2f)
	{
		var track = CurrentTrack;
		CurrentTrack = null;
		startWhenQuiet = false;

		if (track == null)
		{
			return;
		}

		logger.Debug($"Stopping track: {track.Name}");
		if (!track.IsPlaying)
		{
			return;
		}

		track.FadeOut(fadeOutTimeS);

		// The fade belongs to this engine until it is over. Dropping the reference here -
		// which is what used to happen - left a sound playing that nothing could stop and
		// nothing knew about, and cleared the way for the next track to start on top of it.
		fadingOut[track] = getCurrentTimeMs() + (long)(fadeOutTimeS * 1000f) + FadeOutGraceMs;
	}

	/// <summary>
	/// Retire the tracks whose fade out has finished.
	///
	/// A track drops off this list as soon as it goes quiet. If one is still sounding when
	/// its fade was due to be over, the fade did not take - the game skips a fade's
	/// completion callback if anything issued another fade on that sound in the meantime,
	/// and this mod's own ContinuePlay issues one - so it is stopped outright rather than
	/// left to play on unowned.
	/// </summary>
	private void ForgetFinishedFadeOuts()
	{
		if (fadingOut.Count == 0)
		{
			return;
		}

		var now = getCurrentTimeMs();
		foreach (var (track, deadline) in fadingOut.ToList())
		{
			if (track.IsPlaying && now < deadline)
			{
				continue;
			}

			if (track.IsPlaying)
			{
				logger.Debug($"Fade out of {track.Name} did not finish in time; stopping it");
				track.FadeOut(0f);
			}

			fadingOut.Remove(track);
		}
	}

	public void StopTrackAndPause(float fadeOutTimeS = 2f)
	{
		if (CurrentTrack == null)
		{
			return;
		}

		if (CurrentPlaylist?.Situation.Attributes().PauseAfterPlayback == true)
		{
			Pause.Start(GetPauseDuration());
		}

		StopTrack(fadeOutTimeS);
	}

	private void MonitorCurrentTrack()
	{
		if (CurrentTrack == null)
		{
			return;
		}

		// required for dynamic tracks to update (CaveMusicTrack)
		CurrentTrack.ContinuePlay(0f, getPlayerProperties());

		if (!CurrentTrack.IsPlaying)
		{
			StopTrackAndPause(0);
		}
	}

	private void AutoEnqueueNextTrack()
	{
		if (CurrentTrack == null && (startWhenQuiet || !IsPaused))
		{
			TryStartNextTrack();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private long GetPauseDuration()
	{
		int frequencySetting = Math.Clamp(musicFrequency, 0, 3);
		float baseDuration = PauseDurations[frequencySetting][0];
		float variance = PauseDurations[frequencySetting][1];
		float duration = baseDuration - (Random.Shared.NextSingle() * 2 - 1) * variance;
		return (long)duration * 1000L;
	}
}
