namespace VintageSymphony.Engine;

/// <summary>
/// When each piece of music was last heard, for the shuffle bag in
/// <see cref="TrackSelector"/>. Keyed by piece rather than by track object: the pool is
/// rebuilt whenever a source changes, and a rebuild must not make everything unheard.
/// </summary>
public class ListeningHistory
{
	private readonly Dictionary<string, long> lastHeard = new();

	public void Heard(MusicTrack track, long nowMs)
	{
		lastHeard[TrackSelector.PieceOf(track)] = nowMs;
	}

	public long? LastHeard(MusicTrack track)
	{
		return lastHeard.TryGetValue(TrackSelector.PieceOf(track), out var at) ? at : null;
	}
}
