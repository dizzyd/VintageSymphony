using VintageSymphony.Util;

namespace VintageSymphony.Engine;

/// <summary>
/// Which of the tracks that fit gets played.
///
/// This is a draw, not a ranking. BeginSort is what rolls each track's StartPriority - a
/// gauss around 1, see <see cref="MusicTrack.InternalInitialize"/> - so ordering on that
/// picks a different track each time while still letting a pack's own Priority tip the
/// odds. Ordering on Priority alone, which is what this used to do while calling BeginSort
/// and throwing the roll away, is not a draw at all: the highest number among the tracks
/// that currently fit wins every single time. A pack with two 1.05 daytime tracks and
/// twenty at the default 1.0 played those two and nothing else.
/// </summary>
public static class TrackSelector
{
	public static MusicTrack? Select(IEnumerable<MusicTrack> tracks)
	{
		return tracks
			.ForeachContinuous(track => track.BeginSort())
			.OrderByDescending(SelectionPriority)
			// Draws are clamped at 1 by the game, so exact ties are common and the order
			// they arrive in must not decide them.
			.ThenBy(_ => Random.Shared.Next())
			.FirstOrDefault();
	}

	/// <summary>
	/// The game's own rule, from SurfaceMusicTrack: "when reading a songs start priority
	/// the maximum of start priority and priority is used".
	/// </summary>
	public static float SelectionPriority(MusicTrack track)
	{
		return Math.Max(track.Priority, track.StartPriority);
	}
}
