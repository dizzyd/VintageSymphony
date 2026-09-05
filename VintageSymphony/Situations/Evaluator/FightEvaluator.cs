using VintageSymphony.Situations.Facts;
using VintageSymphony.Util;

namespace VintageSymphony.Situations.Evaluator;

/// <summary>
/// A fight is an enemy the player can see, an attack the player can see, or a hit the
/// player has taken. Proximity alone is not: an enemy on the other side of a wall or a
/// window used to score here through the distance term and, with a weapon in hand,
/// through the weapon term, so a player safe indoors heard combat music while a
/// drifter loitered outside. Unseen proximity is Danger's business. The one thing an
/// unseen enemy can still contribute is damage, since a hit is a hit wherever it came
/// from - that term keeps the any-enemy distance as its weight.
/// </summary>
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
		float anyEnemy = MoreMath.ClampMap(facts.EnemyDistance, 5, distanceThreshold, 1, 0);

		float holdingWeapon = facts.IsHoldingWeapon ? 1f : 0f;
		float damage = facts.SecondsSinceLastDamage == 0f
			? 0f
			: MoreMath.ClampMap(facts.SecondsSinceLastDamage, 0, 20, 1, 0);
		float attack = facts.SecondsSinceLastAttack == 0f
			? 0f
			: MoreMath.ClampMap(facts.SecondsSinceLastAttack, 0, 10, 1, 0);

		return MoreMath.WeightedAverage(
			new Tuple<float, float>(visibleEnemy, 2.3f),
			new Tuple<float, float>(attack, 1.3f),
			new Tuple<float, float>(damage, anyEnemy),
			new Tuple<float, float>(holdingWeapon, 0.5f * visibleEnemy)
		);
	}
}
