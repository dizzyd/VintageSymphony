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

	/// <summary>
	/// The assessments, best first, as of the last update. A fresh list each time rather
	/// than the working list sorted in place: this is read on the main thread while
	/// <see cref="Update"/> runs on its own, and enumerating a list mid-sort throws.
	/// </summary>
	private volatile IList<SituationAssessment> ranked;

	public IList<SituationAssessment> Assessments => ranked;
	private readonly Dictionary<Situation, IEvaluator> evaluators = new();

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

		ranked = assessments.AsReadOnly();

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

	public void Update(float deltaTimeS)
	{
		facts = situationalFactsCollector.GatherFacts(deltaTimeS);

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
			assessment.Score = ExponentialSmoothing(assessment, deltaTimeS, assessment.Score, newCertainty);
		}

		ranked = assessments.OrderByDescending(a => a.WeightedScore).ToList().AsReadOnly();
	}

	private static float ExponentialSmoothing(SituationAssessment assessment, float deltaTimeS, float oldCertainty,
		float newCertainty)
	{
		if ((newCertainty > oldCertainty && assessment.SmoothIncreasingScore)
		    || (newCertainty < oldCertainty && assessment.SmoothDecreasingScore))
		{
			return MoreMath.ExponentialSmoothing(oldCertainty, newCertainty, 0.2f, deltaTimeS);
		}

		return newCertainty;
	}
}