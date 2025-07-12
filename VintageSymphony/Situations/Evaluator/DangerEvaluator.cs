using VintageSymphony.Situations.Facts;
using VintageSymphony.Util;

namespace VintageSymphony.Situations.Evaluator;

public class DangerEvaluator : IEvaluator
{
	public bool IsEvaluatingSituation(Situation situation)
	{
		return situation == Situation.Danger;
	}

	public float Evaluate(Situation situation, SituationalFacts facts)
	{
		const float distanceThreshold = 25;
		float enemies = MoreMath.ClampMap(facts.EnemyDistance, 5, distanceThreshold, 1, 0) * 0.7f
		                + MoreMath.ClampMap(facts.VisibleEnemyDistance, 5, distanceThreshold, 1, 0) * 0.3f;
		float rifts = MoreMath.ClampMap(facts.RiftDistance, 0, 40, 1, 0);
		float damage = MoreMath.ClampMap(facts.SecondsSinceLastDamage, 0, 60, 1, 0);
		float damageWeight = damage >= 0.5f && facts.EnemyDistance < distanceThreshold ? 3f : 0.2f;

		return MoreMath.WeightedAverage(
			new Tuple<float, float>(enemies, 1.5f),
			new Tuple<float, float>(rifts, 0.3f),
			new Tuple<float, float>(damage, damageWeight)
		);
	}
}