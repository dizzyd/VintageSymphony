using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VintageSymphony.Situations;
using VintageSymphony.Situations.Scoring;

namespace VintageSymphony.Engine;

public class MusicCurator
{
	private readonly ICoreClientAPI clientApi;
	private readonly Func<IList<SituationAssessment>> getAssessments;
	private readonly Playback playback;

	private List<MusicTrack> tracks = new();
	private readonly Dictionary<Situation, Playlist> playlists = new();
	private readonly PlaylistSwitchGate switchGate;

	/// <summary>The situations as the assessor ranks them, best first.</summary>
	public IList<SituationAssessment> Assessments => getAssessments();
	private ILogger Logger => clientApi.Logger;


	public List<MusicTrack> Tracks
	{
		get => tracks;
		set
		{
			tracks = value;
			InitializePlaylists();
		}
	}

	/// <param name="getAssessments">The assessor's ranking, read afresh each update.</param>
	/// <param name="getCurrentTimeMs">The clock the switch gate measures its holds by.</param>
	public MusicCurator(
		ICoreClientAPI clientApi,
		Func<IList<SituationAssessment>> getAssessments,
		Playback playback,
		Func<long> getCurrentTimeMs)
	{
		this.clientApi = clientApi;
		this.getAssessments = getAssessments;
		this.playback = playback;
		switchGate = new PlaylistSwitchGate(getCurrentTimeMs);
	}

	private void InitializePlaylists()
	{
		playlists.Clear();

		// Group tracks by situation
		var tracksBySituation = new Dictionary<Situation, List<MusicTrack>>();
		foreach (var situation in Enum.GetValues<Situation>())
		{
			tracksBySituation[situation] = new List<MusicTrack>();
		}

		// Assign tracks to their situations
		foreach (var track in tracks)
		{
			foreach (var situation in track.TrackSituations)
			{
				tracksBySituation[situation].Add(track);
			}
		}

		// Create playlists
		foreach (var situation in Enum.GetValues<Situation>())
		{
			var situationTracks = tracksBySituation[situation];
			var playlist = new Playlist(situation,
				(situationTracks.Count > 0) ? situationTracks : Enumerable.Empty<MusicTrack>());
			playlists[situation] = playlist;
		}
	}

	public void Update(float dt)
	{
		AutoSelectPlaylist();
	}

	private void AutoSelectPlaylist()
	{
		var playlist = GetBestPlaylistForCurrentSituation();
		if (playlist == null)
		{
			return;
		}

		// The gate decides whether the better playlist has been better for long enough -
		// see PlaylistSwitchGate for why following the scores directly was a bug. A
		// playlist from before the pool was rebuilt is nobody's current playlist: its
		// tracks are gone, so there is nothing to hold on to.
		var currentPlaylist = playback.CurrentPlaylist;
		var current = currentPlaylist != null && playlists.ContainsValue(currentPlaylist)
			? currentPlaylist.Situation
			: (Situation?)null;
		var lead = current is { } c ? WeightedScore(playlist.Situation) - WeightedScore(c) : 0f;
		if (switchGate.Allows(current, playlist.Situation, lead, playback.PlayingSinceMs))
		{
			Logger.Debug($"Switching to playlist: {playlist.Situation}");
			playback.Play(playlist);
		}
	}

	private float WeightedScore(Situation situation)
	{
		return Assessments.FirstOrDefault(a => a.Situation == situation)?.WeightedScore ?? 0f;
	}

	/// <summary>
	/// The best-ranked situation with music to offer right now.
	///
	/// Two things this used to get wrong. It only looked within 0.2 of the top score, and
	/// when nothing in that band had music it returned nothing - which left the current
	/// playlist in place, whatever its own situation had fallen to, so a pack with fight
	/// music and no danger music kept the combat going after the fight was over. And it
	/// took a playlist with tracks for a playlist with music: the game's one Danger track
	/// plays only inside the Resonance Archive, so anyone on the game's music alone got
	/// silence whenever Danger led. Now the ranking is walked all the way down and
	/// playback is asked whether a playlist has anything it may play. Silence is the one
	/// situation whose empty playlist is the point.
	/// </summary>
	private Playlist? GetBestPlaylistForCurrentSituation()
	{
		foreach (var assessment in Assessments)
		{
			if (playlists.TryGetValue(assessment.Situation, out var playlist)
			    && (assessment.Situation == Situation.Silence || playback.HasPlayableTrack(playlist)))
			{
				return playlist;
			}
		}

		return null;
	}
}
