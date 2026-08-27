using Vintagestory.API.MathTools;
using VintageSymphony.Situations.Evaluator;
using VintageSymphony.Situations.Facts;
using VintageSymphony.Storage;
using VintageSymphony.Util;

namespace VintageSymphony.Situations.Scoring;

public class SituationAssessor : IDisposable
{
	private readonly List<SituationAssessment> assessments = new();
	private readonly Dictionary<Situation, List<SituationAssessment>> aversions = new();

	public IList<SituationAssessment> Assessments => assessments.AsReadOnly();
	private readonly Dictionary<Situation, IEvaluator> evaluators = new();

	private readonly Comparison<SituationAssessment> scoreComparator =
		(x, y) => y.WeightedScore.CompareTo(x.WeightedScore);

	private readonly List<IEvaluator> allEvaluators = TypeResolver.ResolveAll<IEvaluator>();

	private readonly SituationalFactsCollector situationalFactsCollector;
	private SituationalFacts facts;
	public SituationalFacts SituationalFacts => facts;

	public SituationAssessor(AttributeStorage attributeStorage)
	{
		situationalFactsCollector = new(attributeStorage);

		foreach (var situation in Enum.GetValues<Situation>())
		{
			var situationAssessment = new SituationAssessment(situation, 0f);
			assessments.Add(situationAssessment);

			foreach (var evaluator in allEvaluators)
			{
				if (evaluator.IsEvaluatingSituation(situation))
				{
					evaluators[situation] = evaluator;
					break;
				}
			}
		}

		foreach (var situation in Enum.GetValues<Situation>())
		{
			var situationAttributes = situation.Attributes();
			var situationAversions = situationAttributes.Aversions;

			aversions[situation] = assessments
				.Where(a => situationAversions.Contains(a.Situation))
				.ToList();
		}
	}

	/// <summary>
	/// The collector holds a tick listener and a block-changed subscription, so it has to
	/// be let go of when the world does.
	/// </summary>
	public void Dispose()
	{
		situationalFactsCollector.Dispose();
	}

	public void Update(float dt)
	{
		facts = situationalFactsCollector.GatherFacts(dt);

		foreach (var assessment in assessments)
		{
			if (!evaluators.TryGetValue(assessment.Situation, out var evaluator))
			{
				continue;
			}

			var newCertainty = GameMath.Clamp(evaluator.Evaluate(assessment.Situation, facts), 0f, 1f);
			foreach (var aversion in aversions[assessment.Situation])
			{
				newCertainty -= aversion.Score;
			}

			newCertainty = Math.Clamp(newCertainty, 0f, 1);
			assessment.Score = ExponentialSmoothing(assessment, dt, assessment.Score, newCertainty);
		}

		assessments.Sort(scoreComparator);
	}

	private static float ExponentialSmoothing(SituationAssessment assessment, float dt, float oldCertainty,
		float newCertainty)
	{
		if ((newCertainty > oldCertainty && assessment.SmoothIncreasingScore)
		    || (newCertainty < oldCertainty && assessment.SmoothDecreasingScore))
		{
			return MoreMath.ExponentialSmoothing(oldCertainty, newCertainty, 0.2f, dt);
		}

		return newCertainty;
	}
}