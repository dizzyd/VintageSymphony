using VintageSymphony.Situations.Facts;
using VintageSymphony.Util;

namespace VintageSymphony.Situations.Evaluator;

public class CaveEvaluator : IEvaluator
{
	public bool IsEvaluatingSituation(Situation situation)
	{
		return situation == Situation.Cave;
	}

	public float Evaluate(Situation situation, SituationalFacts facts)
	{
		float underground = MoreMath.ClampMap(facts.DistanceToSurface, 0, 10, 0, 1);
		float sunLevel = MoreMath.ClampMap(facts.SunLevel, 3, 10, 1, 0);
		float homeProximity = MoreMath.ClampMap(facts.DistanceFromHome, 20, 50, 1, 0);
		// Walls all round are the difference between a cave and a cellar, however deep
		// the cellar. Someone who built the room they stand in is not exploring it.
		float enclosure = facts.IsInEnclosedRoom ? 1f : 0f;
		return underground * sunLevel - homeProximity - enclosure;
	}
}