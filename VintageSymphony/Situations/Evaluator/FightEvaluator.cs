using VintageSymphony.Situations.Facts;
using VintageSymphony.Util;

namespace VintageSymphony.Situations.Evaluator;

public class FightEvaluator : IEvaluator
{
	public bool IsEvaluatingSituation(Situation situation)
	{
		return situation == Situation.Fight;
	}

	public float Evaluate(Situation situation, SituationalFacts facts)
	{
		const float distanceThreshold = 10;
		float visibleEnemy = MoreMath.ClampMap(facts.VisibleEnemyDistance, 5, distanceThreshold, 1, 0);
		float enemyDistance = MoreMath.ClampMap(facts.EnemyDistance, 5, distanceThreshold, 1, 0);

		float holdingWeapon = facts.IsHoldingWeapon ? 1f : 0f;
		float damage = facts.SecondsSinceLastDamage == 0f
			? 0f
			: MoreMath.ClampMap(facts.SecondsSinceLastDamage, 0, 20, 1, 0);
		float attack = facts.SecondsSinceLastAttack == 0f
			? 0f
			: MoreMath.ClampMap(facts.SecondsSinceLastAttack, 0, 10, 1, 0);


		return MoreMath.WeightedAverage(
			new Tuple<float, float>(visibleEnemy, 1.3f),
			new Tuple<float, float>(attack, 1.3f),
			new Tuple<float, float>(enemyDistance, 1f),
			new Tuple<float, float>(damage, enemyDistance),
			new Tuple<float, float>(holdingWeapon, 0.5f * enemyDistance)
		);
	}
}