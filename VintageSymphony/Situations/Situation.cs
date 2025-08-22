namespace VintageSymphony.Situations;

public enum Situation
{
	[SituationData(10f)]
	TemporalStorm,

	[SituationData(2.0f,
		dynamicSituation: true,
		playContinuous: true,
		pauseAfterPlayback: true,
		smoothIncreasingScore: false,
		aversions: [Dead])]
	Fight,

	[SituationData(
		dynamicSituation: true,
		playContinuous: true,
		pauseAfterPlayback: true,
		smoothIncreasingScore: false,
		aversions: [Dead])]
	Danger,

	[SituationData(
		pauseAfterPlayback: true,
		smoothDecreasingScore: false)]
	Cave,

	[SituationData(weight: 1.2f,
		aversions: [Cave, Danger, Dead])]
	Adventure,

	[SituationData(
		aversions: [Cave, Danger, Dead])]
	Idle,

	[SituationData(aversions: [Cave, Danger, Dead])]
	Calm,

	[SituationData(20f,
		dynamicSituation: true,
		playContinuous: true,
		pauseAfterPlayback: true,
		smoothDecreasingScore: false,
		smoothIncreasingScore: false)]
	Dead,
	
	[SituationData(2.0f,
		dynamicSituation: true,
		pauseAfterPlayback: true,
		smoothIncreasingScore: false,
		aversions: [Fight])]
	Silence,
}