using VintageSymphony.Situations;

namespace VintageSymphony.Engine;

/// <summary>
/// Whether the curator may act on a change of leading situation yet.
///
/// The scores move every 300ms and the curator used to follow them instantly, so a drifter
/// pacing outside a house - in Fight range one moment, out of it the next - swapped the
/// music every few seconds. At the highest music frequency the pause between tracks is
/// zero, so each swap was audible: combat, peaceful, combat, peaceful. This holds the
/// line until the new situation has earned it.
///
/// A challenger earns it one of two ways: by leading the current situation by
/// <see cref="LeadMargin"/> for <see cref="DwellMs"/> without a break - the dwell starts
/// over when the lead lapses - or by leading it at all, by however little, for
/// <see cref="PersistentLeadMs"/>. The second is what lets a playlist end: without it a
/// lead that settled just under the margin held the current playlist for as long as it
/// lasted, which for a drifter loitering outside the wall after a fight meant combat
/// music until it wandered off.
///
/// Then, if the current situation is a dynamic one - Fight, Danger - its track plays for
/// at least <see cref="MinimumPlayMs"/> before it is dropped; that is what stops the
/// combat music giving up the moment the enemy pauses. Leaving calm music for a fight is
/// not held that way, since interrupting the calm is the whole point. An
/// <see cref="SituationDataAttribute.Urgent"/> situation skips all of it.
/// </summary>
public class PlaylistSwitchGate
{
	public const float LeadMargin = 0.15f;
	public const long DwellMs = 3_000L;
	public const long PersistentLeadMs = 10_000L;
	public const long MinimumPlayMs = 30_000L;

	private readonly Func<long> getCurrentTimeMs;

	/// <summary>The situation leading by the margin, and since when.</summary>
	private Situation? challenger;
	private long challengerSinceMs;

	/// <summary>Since when the current situation has trailed something, by any amount.</summary>
	private long? trailingSinceMs;

	public PlaylistSwitchGate(Func<long> getCurrentTimeMs)
	{
		this.getCurrentTimeMs = getCurrentTimeMs;
	}

	/// <summary>
	/// May the playlist change from <paramref name="current"/> to <paramref name="candidate"/>?
	/// </summary>
	/// <param name="current">The situation whose playlist is selected, or null when none is.</param>
	/// <param name="candidate">The best-scoring situation that has music to offer.</param>
	/// <param name="lead">The candidate's weighted score minus the current situation's.</param>
	/// <param name="playingSinceMs">When the sounding track started, or null when nothing is sounding.</param>
	public bool Allows(Situation? current, Situation candidate, float lead, long? playingSinceMs)
	{
		if (current == null)
		{
			Reset();
			return true;
		}

		if (current == candidate || lead <= 0f)
		{
			Reset();
			return false;
		}

		if (candidate.Attributes().Urgent)
		{
			Reset();
			return true;
		}

		var now = getCurrentTimeMs();
		trailingSinceMs ??= now;

		if (lead < LeadMargin)
		{
			challenger = null;
		}
		else if (challenger != candidate)
		{
			challenger = candidate;
			challengerSinceMs = now;
		}

		var ledClearly = challenger == candidate && now - challengerSinceMs >= DwellMs;
		var ledPersistently = now - trailingSinceMs.Value >= PersistentLeadMs;
		if (!ledClearly && !ledPersistently)
		{
			return false;
		}

		if (current.Value.Attributes().DynamicSituation
		    && playingSinceMs is { } since
		    && now - since < MinimumPlayMs)
		{
			return false;
		}

		Reset();
		return true;
	}

	private void Reset()
	{
		challenger = null;
		trailingSinceMs = null;
	}
}
