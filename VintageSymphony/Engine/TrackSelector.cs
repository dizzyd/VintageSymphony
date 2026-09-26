using System.Text.RegularExpressions;

namespace VintageSymphony.Engine;

/// <summary>
/// Which of the tracks that fit gets played.
///
/// A shuffle bag: every draw is from the longest-unheard half of what may play, so a track
/// cannot come back until about half of that has played since, however big the pool.
/// Tracks inside their cooldown are drawn from only when nothing that fits is outside one.
///
/// Priority weights the draw within the bag rather than deciding it. Above 1 the weight is
/// priority to the sixteenth: 1.05 about doubles a track's chance, 1.1 makes it about five
/// times as likely, and 1.5 or more wins whenever it is in the bag. Below 1 it is the
/// priority itself, so 0.5 is half as likely rather than never. Once played, a favoured
/// track is at the back of the bag like any other.
/// </summary>
public static class TrackSelector
{
	private const float PriorityExponent = 16f;

	/// <summary>The weight left to a track by the artist who was just heard.</summary>
	private const float SameArtistWeight = 0.35f;

	private const float BagFraction = 0.5f;

	private static readonly Regex StemsSuffix = new(@"_stems.*$", RegexOptions.IgnoreCase);

	/// <summary>
	/// The draw.
	/// </summary>
	/// <param name="fit">Everything that may play now.</param>
	/// <param name="lastHeard">When a track's piece was last heard, or null if never.</param>
	/// <param name="isOnCooldown">A track still inside its cooldown yields to any that is not.</param>
	/// <param name="previous">What just played: its piece is not drawn again straight away unless it is all that fits.</param>
	public static MusicTrack? Draw(
		IReadOnlyList<MusicTrack> fit,
		Func<MusicTrack, long?> lastHeard,
		Func<MusicTrack, bool> isOnCooldown,
		MusicTrack? previous)
	{
		// Not the piece just played - whole or stems - while a different piece fits; then
		// not the same track; then, if it is all there is, anything.
		var piece = previous == null ? null : PieceOf(previous);
		var candidates = fit.Where(t => PieceOf(t) != piece).ToList();
		if (candidates.Count == 0) candidates = fit.Where(t => t != previous).ToList();
		if (candidates.Count == 0) candidates = fit.ToList();
		if (candidates.Count == 0)
		{
			return null;
		}

		// Cooldown first, then age. The two do not agree: a favoured track's cooldown runs
		// a quarter longer, so the longest-unheard track can still be cooling while one
		// heard after it is not.
		var rested = candidates.Where(t => !isOnCooldown(t)).ToList();
		var pool = rested.Count > 0 ? rested : candidates;

		// Never heard sorts before everything, and arrival order must not decide ties - a
		// new session has nothing heard at all.
		var bag = pool
			.OrderBy(_ => Random.Shared.Next())
			.OrderBy(t => lastHeard(t) ?? long.MinValue)
			.Take(Math.Max(1, (int)Math.Ceiling(pool.Count * BagFraction)))
			.ToList();

		return Select(bag, previous);
	}

	/// <summary>
	/// A weighted pick from these, knowing nothing of what has been heard: the part of
	/// the draw that priority and artist decide.
	/// </summary>
	public static MusicTrack? Select(IEnumerable<MusicTrack> tracks, MusicTrack? previous = null)
	{
		var list = tracks.ToList();
		if (list.Count == 0)
		{
			return null;
		}

		// Twenty-three of the default pack's sixty peaceful tracks are one composer's, and
		// a different song by the same hand reads as "that again". Damped rather than
		// barred, and only when someone else is on offer.
		var artist = previous?.Artist;
		var damp = !string.IsNullOrWhiteSpace(artist)
		           && list.Any(t => !SameArtist(t, artist));

		var weights = list
			.Select(t => Weight(t) * (damp && SameArtist(t, artist) ? SameArtistWeight : 1f))
			.ToList();

		var roll = Random.Shared.NextDouble() * weights.Sum();
		for (var i = 0; i < list.Count; i++)
		{
			roll -= weights[i];
			if (roll < 0)
			{
				return list[i];
			}
		}

		return list[^1];
	}

	/// <summary>
	/// Steep above 1, so a pack can insist; plain below it, so 0.5 means half as often
	/// rather than never - which is what the guide has always told pack authors.
	/// </summary>
	public static double Weight(MusicTrack track)
	{
		var priority = Math.Max(track.Priority, 0.01f);
		return priority > 1f ? Math.Pow(priority, PriorityExponent) : priority;
	}

	/// <summary>
	/// What counts as the same piece of music. The default pack ships some pieces twice,
	/// whole and as stems - Campfire Legends, Nature's Way - and hearing one right after
	/// the other is hearing the same thing twice.
	/// </summary>
	public static string PieceOf(MusicTrack track)
	{
		var key = track.Location?.ToString() ?? track.Title;
		if (key.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
		{
			key = key[..^4];
		}

		return StemsSuffix.Replace(key, "");
	}

	private static bool SameArtist(MusicTrack track, string? artist)
	{
		return string.Equals(track.Artist?.Trim(), artist?.Trim(), StringComparison.OrdinalIgnoreCase);
	}
}
