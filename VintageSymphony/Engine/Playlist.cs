using VintageSymphony.Situations;

namespace VintageSymphony.Engine;

public class Playlist
{
	public Situation Situation { get; }
	public List<MusicTrack> Tracks { get; }

	/// <summary>
	/// A playlist whose tracks may join this one's draw when too few of this one's fit.
	/// Idle and Adventure are both peaceful, and the default pack gives them two and six
	/// tracks on a winter's afternoon - a pool that small is a loop, however it is drawn.
	/// Calm has fifteen or more at any hour, and nothing in it is out of place there.
	/// </summary>
	public Playlist? Borrows { get; set; }

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