using VintageSymphony.Situations;

namespace VintageSymphony.Engine;

/// <summary>
/// A track starting, and the draw that chose it: how many tracks the playlist held, how
/// many of them fit the moment, how many of those were off cooldown, and which of the
/// three tiers the pick came from. The numbers are what make a repeat explicable.
/// </summary>
/// <param name="InPlaylist">Tracks in the playlist drawn from.</param>
/// <param name="Fit">Of those, the ones whose rules allowed them right now.</param>
/// <param name="OffCooldown">Of those, the ones that had not played recently.</param>
/// <param name="Borrowed">Of the ones that fit, how many came from the playlist this one borrows from.</param>
public sealed record TrackStart(
	MusicTrack Track,
	Situation Situation,
	int InPlaylist,
	int Fit,
	int OffCooldown,
	TrackStart.Tier How,
	int Borrowed = 0)
{
	public enum Tier
	{
		/// <summary>Drawn from the tracks that fit and were off cooldown.</summary>
		Fresh,

		/// <summary>
		/// The track chosen was still on cooldown. The draw takes one only when no track it
		/// could choose is off cooldown - and the track just played is not one it could
		/// choose, so that one may be off cooldown here.
		/// </summary>
		Recycled,

		/// <summary>The only track that fit, cooldown or not.</summary>
		OnlyFit,
	}
}
