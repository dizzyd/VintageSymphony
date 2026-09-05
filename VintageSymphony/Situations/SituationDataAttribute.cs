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

	/// <summary>
	/// Takes over the music the moment it leads, without the dwell time and minimum play
	/// time the curator otherwise holds a playlist for. Death and a temporal storm are
	/// events, not readings that might waver.
	/// </summary>
	public bool Urgent { get; }

	public Situation[] Aversions { get; }

	public SituationDataAttribute(
		float weight = 1f,
		bool dynamicSituation = false,
		bool playContinuous = false,
		bool pauseAfterPlayback = true,
		bool smoothIncreasingScore = true,
		bool smoothDecreasingScore = true,
		bool urgent = false,
		Situation[]? aversions = null)
	{

		Weight = weight;
		DynamicSituation = dynamicSituation;
		PlayContinuous = playContinuous;
		PauseAfterPlayback = pauseAfterPlayback;
		SmoothIncreasingScore = smoothIncreasingScore;
		SmoothDecreasingScore = smoothDecreasingScore;
		Urgent = urgent;
		Aversions = aversions ?? Array.Empty<Situation>();
	}
}