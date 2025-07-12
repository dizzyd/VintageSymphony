using VintageSymphony.Situations;

namespace VintageSymphony.Engine;

public class Playlist
{
	public Situation Situation { get; }
	public List<MusicTrack> Tracks { get; }

	public Playlist(Situation situation, IEnumerable<MusicTrack> tracks)
	{
		Situation = situation;
		Tracks = new List<MusicTrack>(tracks);
	}

	public IEnumerable<MusicTrack> GetTracks(Func<MusicTrack, bool> predicate)
	{
		return Tracks.Where(predicate);
	}

	public bool ContainsTrack(MusicTrack track)
	{
		return Tracks.Contains(track);
	}

	public override string ToString()
	{
		return $"Playlist for {Situation} with {Tracks.Count} tracks";
	}
}