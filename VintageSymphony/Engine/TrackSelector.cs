using System.Text.RegularExpressions;

namespace VintageSymphony.Engine;

/// <summary>
/// Which of the tracks that fit gets played.
///
/// A shuffle bag, not a cooldown. Every draw is from the half of what fits that has gone
/// longest unheard, so a track cannot come back until about half of what fits has played
/// since - however few or many that is. A fixed cooldown could not do that: the pack's
/// default rotation plays around fifty tracks in four hours from the twenty or so that
/// fit at any one moment, so every track came back the moment its thirty minutes were
/// up, and a playlist of two alternated them all afternoon.
///
/// Priority weights the draw within the bag rather than deciding it. The weight is
/// priority to the sixteenth: 1.05 about doubles a track's chance, 1.1 makes it about
/// five times as likely, and 1.5 or more wins whenever it is in the bag. The bag is what
/// stops a favoured track winning twice running - once played, it is at the back.
///
/// Drawing on Priority alone, which this did before the draw had a roll in it, was not
/// a draw at all: the highest number among the tracks that currently fit won every
/// single time, and a pack with two 1.05 daytime tracks played nothing else by day.
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
	/// <param name="previous">What just played: never again straight away, unless it is all there is.</param>
	public static MusicTrack? Draw(
		IReadOnlyList<MusicTrack> fit,
		Func<MusicTrack, long?> lastHeard,
		Func<MusicTrack, bool> isOnCooldown,
		MusicTrack? previous)
	{
		var others = fit.Where(t => t != previous).ToList();
		var candidates = others.Count > 0 ? others : fit.ToList();
		if (candidates.Count == 0)
		{
			return null;
		}

		// Longest unheard first, and never heard before everything. The order they arrive
		// in must not decide ties - a new session has nothing heard at all.
		var bag = candidates
			.OrderBy(_ => Random.Shared.Next())
			.OrderBy(t => lastHeard(t) ?? long.MinValue)
			.Take(Math.Max(1, (int)Math.Ceiling(candidates.Count * BagFraction)))
			.ToList();

		// The bag's front is what has gone longest unheard, so anything off cooldown is
		// already in it; this only narrows a bag that runs on into the recently heard.
		var rested = bag.Where(t => !isOnCooldown(t)).ToList();
		return Select(rested.Count > 0 ? rested : bag, previous);
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
