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
/// Three rules, in order. A challenger must lead the current situation by
/// <see cref="LeadMargin"/>, and keep leading for <see cref="DwellMs"/> without a break -
/// the dwell starts over when the lead lapses. Then, if the current situation is a
/// dynamic one - Fight, Danger - its track plays for at least <see cref="MinimumPlayMs"/>
/// before it is dropped; that is what stops the combat music giving up the moment the
/// enemy pauses. Leaving calm music for a fight is not held that way, since interrupting
/// the calm is the whole point. An <see cref="SituationDataAttribute.Urgent"/> situation
/// skips all of it.
/// </summary>
public class PlaylistSwitchGate
{
	public const float LeadMargin = 0.15f;
	public const long DwellMs = 3_000L;
	public const long MinimumPlayMs = 30_000L;

	private readonly Func<long> getCurrentTimeMs;
	private Situation? challenger;
	private long challengerSinceMs;

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
			challenger = null;
			return true;
		}

		if (current == candidate)
		{
			challenger = null;
			return false;
		}

		if (candidate.Attributes().Urgent)
		{
			challenger = null;
			return true;
		}

		if (lead < LeadMargin)
		{
			challenger = null;
			return false;
		}

		var now = getCurrentTimeMs();
		if (challenger != candidate)
		{
			challenger = candidate;
			challengerSinceMs = now;
			return false;
		}

		if (now - challengerSinceMs < DwellMs)
		{
			return false;
		}

		if (current.Value.Attributes().DynamicSituation
		    && playingSinceMs is { } since
		    && now - since < MinimumPlayMs)
		{
			return false;
		}

		challenger = null;
		return true;
	}
}
