using System.Runtime.InteropServices;
using VintageSymphony.Situations.Facts;
using VintageSymphony.Util;

namespace VintageSymphony.Situations.Evaluator;

public class SilenceEvaluator : IEvaluator
{
	public bool IsEvaluatingSituation(Situation situation)
	{
		return situation == Situation.Silence;
	}

	public float Evaluate(Situation situation, SituationalFacts facts)
	{
		const int max = SituationalFacts.PlayingResonatorDistanceMax;
		return MoreMath.ClampMap(facts.PlayingResonatorDistance, max - (int)(max * 0.25), max, 1, 0);
	}
}