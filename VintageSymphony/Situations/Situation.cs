namespace VintageSymphony.Situations;

public enum Situation
{
	[SituationData(10f, urgent: true)]
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

	/// <summary>
	/// A room the player built below the ground: sealed, and under the natural
	/// terrain. Weighted to beat Calm's easy 1.0 when it is fully certain, so that a
	/// pack with keep music gets to play it; a pack without falls through to Calm and
	/// Idle, which is what the Cave damping already leaves.
	/// </summary>
	[SituationData(weight: 1.2f,
		aversions: new[] { Danger, Dead })]
	Keep,

	[SituationData(weight: 1.2f,
		aversions: new[] { Cave, Danger, Dead })]
	Adventure,

	[SituationData(
		aversions: new[] { Cave, Danger, Dead })]
	Idle,

	[SituationData(aversions: new[] { Cave, Danger, Dead })]
	Calm,

	[SituationData(20f,
		dynamicSituation: true,
		playContinuous: true,
		pauseAfterPlayback: true,
		smoothDecreasingScore: false,
		smoothIncreasingScore: false,
		urgent: true)]
	Dead,
	
	[SituationData(2.0f,
		dynamicSituation: true,
		pauseAfterPlayback: true,
		smoothIncreasingScore: false,
		aversions: new[] { Fight })]
	Silence,
}