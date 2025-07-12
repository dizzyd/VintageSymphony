using VintageSymphony.Situations.Evaluator;

namespace VintageSymphony.Situations;

public static class SituationExtensions
{
	/// <summary>
	/// Extension method to get the SituationDataAttribute for a Situation enum value
	/// </summary>
	/// <param name="situation">The situation enum value</param>
	/// <returns>The SituationDataAttribute attached to the enum value</returns>
	public static SituationDataAttribute Attributes(this Situation situation)
	{
		return SituationDataReader.GetAttributes(situation);
	}
}