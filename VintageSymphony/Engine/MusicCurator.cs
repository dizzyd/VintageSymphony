using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VintageSymphony.Situations;
using VintageSymphony.Situations.Scoring;

namespace VintageSymphony.Engine;

public class MusicCurator
{
	private readonly ICoreClientAPI clientApi;
	private readonly SituationAssessor situationAssessor;
	private readonly Playback playback;

	private List<MusicTrack> tracks = new();
	private readonly Dictionary<Situation, Playlist> playlists = new();
	private readonly PlaylistSwitchGate switchGate;
	public IList<SituationAssessment> Assessments => situationAssessor.Assessments;
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

	public MusicCurator(ICoreClientAPI clientApi, SituationAssessor situationAssessor, Playback playback)
	{
		this.situationAssessor = situationAssessor;
		this.clientApi = clientApi;
		this.playback = playback;
		switchGate = new PlaylistSwitchGate(() => clientApi.ElapsedMilliseconds);
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

	private IEnumerable<SituationAssessment> GetHighestAssessments()
	{
		const float certaintyFuzziness = 0.2f;

		if (Assessments.Count == 0)
		{
			return Array.Empty<SituationAssessment>();
		}

		float highestCertainty = Assessments[0].WeightedScore;
		float scoreThreshold = highestCertainty - certaintyFuzziness;
		return Assessments
			.TakeWhile(s => s.WeightedScore >= scoreThreshold);
	}

	/// <summary>
	/// The best-scoring situation that has something to play. Every situation owns a
	/// playlist, so testing for the playlist's existence - as this used to - made the
	/// leader win regardless and left the near-tie fallthrough above doing nothing: a
	/// pack with no fight music sat in silence through every fight. Silence is the one
	/// situation whose empty playlist is the point.
	/// </summary>
	private Playlist? GetBestPlaylistForCurrentSituation()
	{
		foreach (var assessment in GetHighestAssessments())
		{
			if (playlists.TryGetValue(assessment.Situation, out var playlist)
			    && (playlist.Tracks.Count > 0 || assessment.Situation == Situation.Silence))
			{
				return playlist;
			}
		}

		return null;
	}
}