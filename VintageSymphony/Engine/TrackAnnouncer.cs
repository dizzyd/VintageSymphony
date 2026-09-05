namespace VintageSymphony.Engine;

/// <summary>
/// Says what has started playing, in chat, with the artist when the pack names one -
/// the credit a musician is owed, put where the listener will see it. Once per track
/// per game session: the first time a piece plays is when the question is asked, and
/// the fortieth is not.
/// </summary>
public class TrackAnnouncer
{
	/// <summary>
	/// Static so that it outlives a world: leaving and rejoining is the same sitting,
	/// and the same music does not want introducing twice in it.
	/// </summary>
	private static readonly HashSet<string> Announced = new();

	private readonly Action<string> say;
	private readonly Func<bool> wanted;

	/// <param name="say">Where the line goes - chat, in the game.</param>
	/// <param name="wanted">Whether the player wants to be told at all; read each time.</param>
	public TrackAnnouncer(Action<string> say, Func<bool> wanted)
	{
		this.say = say;
		this.wanted = wanted;
	}

	public void TrackStarted(MusicTrack track)
	{
		// The game's cave music is a bed of ambience rather than a piece with a name.
		if (!wanted() || track.isCaveMusic)
		{
			return;
		}

		var key = track.Location?.ToString() ?? track.Title;
		if (!Announced.Add(key))
		{
			return;
		}

		say(Describe(track));
	}

	public static string Describe(MusicTrack track)
	{
		return string.IsNullOrWhiteSpace(track.Artist)
			? $"Now playing {track.Title}"
			: $"Now playing {track.Title} ({track.Artist})";
	}

	/// <summary>Start the session over, for tests.</summary>
	public static void Forget() => Announced.Clear();
}
