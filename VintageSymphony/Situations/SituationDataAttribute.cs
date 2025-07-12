namespace VintageSymphony.Situations;

[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class SituationDataAttribute : Attribute
{
	public float Weight { get; }
	public bool DynamicSituation { get; }
	public bool PlayContinuous { get; }
	public bool PauseAfterPlayback { get; }
	public bool SmoothIncreasingScore { get; }
	public bool SmoothDecreasingScore { get; }

	public Situation[] Aversions { get; }

	public SituationDataAttribute(
		float weight = 1f,
		bool dynamicSituation = false,
		bool playContinuous = false,
		bool pauseAfterPlayback = true, 
		bool smoothIncreasingScore = true,
		bool smoothDecreasingScore = true,
		Situation[]? aversions = null)
	{

		Weight = weight;
		DynamicSituation = dynamicSituation;
		PlayContinuous = playContinuous;
		PauseAfterPlayback = pauseAfterPlayback;
		SmoothIncreasingScore = smoothIncreasingScore;
		SmoothDecreasingScore = smoothDecreasingScore;
		Aversions = aversions ?? Array.Empty<Situation>();
	}
}