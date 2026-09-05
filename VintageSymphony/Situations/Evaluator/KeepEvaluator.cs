using VintageSymphony.Situations.Facts;
using VintageSymphony.Util;

namespace VintageSymphony.Situations.Evaluator;

/// <summary>
/// Underground and enclosed. The depth ramp is short on purpose: a keep's first hall
/// may be only a few blocks under the turf, and what makes it a keep rather than a
/// house is being under the natural ground at all. The terrain height the surface is
/// measured from is the worldgen one, which building never moves - so a room sunk into
/// a hill counts and a tower built on it does not.
/// </summary>
public class KeepEvaluator : IEvaluator
{
	public bool IsEvaluatingSituation(Situation situation)
	{
		return situation == Situation.Keep;
	}

	public float Evaluate(Situation situation, SituationalFacts facts)
	{
		if (!facts.IsInEnclosedRoom)
		{
			return 0f;
		}

		return MoreMath.ClampMap(facts.DistanceToSurface, 1, 4, 0, 1);
	}
}
