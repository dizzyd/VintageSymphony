using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using VintageSymphony.Situations;
using VintageSymphony.Util;

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

	private const int MinPlaybackTimeForSongReplacementMs = 8 * 1000;
	private const int DisableTrackCooldownThresholdMs = 30 * 1000;

	private static readonly float[][] PauseDurations =
	{
		new[] { 960f, 480f },
		new[] { 420f, 240f },
		new[] { 180f, 120f },
		new float[2]
	};

	private int musicFrequency = 2; // Default middle value

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
		MonitorCurrentTrack();
		AutoEnqueueNextTrack();
	}

	public void SetMusicFrequency(int frequency)
	{
		musicFrequency = frequency;
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

		if (oldAttributes is {DynamicSituation: true})
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
		
		var result = filteredTracks
			.Where(track => !trackCooldownManager.IsOnCooldown(track))
			.ForeachContinuous(track => track.BeginSort())
			.OrderBy(_ => Random.Shared.Next())
			.FirstOrDefault();
		
		// If no track found (all are on cooldown), try again ignoring cooldown
		if (result == null)
		{
			result = filteredTracks
				.ForeachContinuous(track => track.BeginSort())
				.OrderBy(_ => Random.Shared.Next())
				.FirstOrDefault();
		}
		
		return result;
	}

	private bool TrackFilterPredicate(MusicTrack track)
	{
		var playerPosition = VintageSymphony.ClientApi.World.Player.Entity.Pos.AsBlockPos;
		var climateCondition = VintageSymphony.ClientApi.World.BlockAccessor.GetClimateAt(playerPosition);

		return trackRestrictionMatcher.IsWithinConfiguredRestrictions(track, getPlayerProperties(),
			       climateCondition, playerPosition);
	}

	public void NextTrack()
	{
		StopTrack();
		Pause.Stop();

		var track = FindNextTrack();
		if (track != null)
		{
			PlayTrack(track);
		}
	}

	private void PlayTrack(MusicTrack track)
	{
		CurrentTrack = track;
		CurrentTrack.BeginPlay(getPlayerProperties());
		if (!track.DisableCooldown)
		{
			trackCooldownManager.PutOnCooldown(CurrentTrack);
		}

		logger.Notification($"Playing track: {track.Name}");
	}

	public void StopTrack(float fadeOutTimeS = 2f)
	{
		if (CurrentTrack == null)
		{
			return;
		}

		logger.Debug($"Stopping track: {CurrentTrack.Name}");
		if (CurrentTrack.IsPlaying)
		{
			CurrentTrack.FadeOut(fadeOutTimeS);
		}

		CurrentTrack = null;
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
		if (CurrentTrack == null && !IsPaused)
		{
			NextTrack();
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