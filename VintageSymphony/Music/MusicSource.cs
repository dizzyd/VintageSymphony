using Newtonsoft.Json;

namespace VintageSymphony.Music;

/// <summary>
/// One place music comes from. The id doubles as the asset domain the tracks are
/// addressed under, so two sources can both ship a "theme.ogg" without colliding, and
/// as the directory name under ModData.
///
/// A source with no <see cref="Url"/> is purely local - files someone put there
/// themselves, which is the case that needs no network code at all.
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class MusicSource
{
	[JsonProperty] public string Id { get; set; } = "";
	[JsonProperty] public string Name { get; set; } = "";
	[JsonProperty] public bool Enabled { get; set; } = true;

	/// <summary>Where the pack can be fetched from, if it can be fetched at all.</summary>
	[JsonProperty] public string? Url { get; set; }

	/// <summary>
	/// Major.minor of the newest release of this source that works with this build, if
	/// the source publishes versions.
	/// </summary>
	[JsonProperty] public string? Compatible { get; set; }

	/// <summary>What is on disk, once something has been installed.</summary>
	[JsonProperty] public string? Installed { get; set; }

	/// <summary>
	/// The mod this source arrived inside, for music that ships as a mod's assets rather
	/// than as a folder of ours. Such an entry is made and unmade by the mod being there,
	/// not by anyone's hand - only its on/off switch is the player's.
	/// </summary>
	[JsonProperty] public string? Mod { get; set; }

	public override string ToString() => $"{Id} ({Name})";
}
