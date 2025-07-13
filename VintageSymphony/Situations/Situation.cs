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
		aversions: new[] { Dead })]
	Fight,

	[SituationData(
		dynamicSituation: true,
		playContinuous: true,
		pauseAfterPlayback: true,
		smoothIncreasingScore: false,
		aversions: new[] { Dead })]
	Danger,

	[SituationData(
		pauseAfterPlayback: true,
		smoothDecreasingScore: false)]
	Cave,

	[SituationData(weight: 1.2f,
		aversions: new[] { Cave, Danger, Dead })]
	Adventure,

	[SituationData(weight: 0.9f,
		aversions: new[] { Cave, Danger, Dead })]
	Idle,

	[SituationData(aversions: new[] { Cave, Danger, Dead })]
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
		aversions: new[] { Fight })]
	Silence,
}