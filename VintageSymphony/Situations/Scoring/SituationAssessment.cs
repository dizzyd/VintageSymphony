namespace VintageSymphony.Situations.Scoring;

public class SituationAssessment
{
	private readonly float weight;
	public readonly bool SmoothIncreasingScore;
	public readonly bool SmoothDecreasingScore;

	public Situation Situation { get; }

	public float Score { get; set; }

	public float WeightedScore => Score * weight;

	public SituationAssessment(Situation situation, float score)
	{
		var attributes = situation.Attributes();
		Situation = situation;
		Score = score;
		weight = attributes.Weight;
		SmoothIncreasingScore = attributes.SmoothIncreasingScore;
		SmoothDecreasingScore = attributes.SmoothDecreasingScore;
	}
}