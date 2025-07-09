using System.Runtime.InteropServices;
using VintageSymphony.Situations.Facts;
using VintageSymphony.Util;

namespace VintageSymphony.Situations.Evaluator;

public class DeathEvaluator : IEvaluator
{
	public bool IsEvaluatingSituation(Situation situation)
	{
		return situation == Situation.Dead;
	}

	public float Evaluate(Situation situation, SituationalFacts facts)
	{
		bool health = facts.Alive;
		
		
		return health ? 0f : 1f;
	}
}