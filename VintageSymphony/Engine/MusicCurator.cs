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
		// Check if we need to switch to a better playlist for the current situation
		var playlist = GetBestPlaylistForCurrentSituation();
		if (playlist != null && playlist != playback.CurrentPlaylist)
		{
			Logger.Debug($"Switching to playlist: {playlist.Situation}");
			playback.Play(playlist);
		}
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