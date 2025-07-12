namespace VintageSymphony.Situations;

public enum Situation
{
	[SituationData(10f)]
	TemporalStorm,

	[SituationData(2.0f,
		dynamicSituation: true,
		playContinuous: true,
		pauseAfterPlayback: true,
		smoothIncreasingScore: false)]
	Fight,

	[SituationData(1.5f,
		dynamicSituation: true,
		playContinuous: true,
		pauseAfterPlayback: true,
		smoothIncreasingScore: false,
		aversions: new[] { Cave })]
	Danger,

	[SituationData(
		pauseAfterPlayback: true,
		smoothDecreasingScore: false)]
	Cave,

	[SituationData(weight: 1.2f,
		aversions: new[] { Cave })]
	Adventure,

	[SituationData(weight: 0.9f,
		aversions: new[] { Cave })]
	Idle,

	[SituationData(aversions: new[] { Cave })]
	Calm,

	[SituationData(20f,
		dynamicSituation: true,
		playContinuous: true,
		pauseAfterPlayback: true,
		smoothDecreasingScore: false,
		smoothIncreasingScore: false)]
	Dead,
}