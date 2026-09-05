using Newtonsoft.Json;

namespace VintageSymphony.Music;

/// <summary>
/// The simple track list: what someone writes when they just want to add a few files.
/// The game's own musicconfig.json is richer and still supported for a full pack, but it
/// wants a $type line naming this assembly on every entry, which is a lot to ask of
/// someone with five .ogg files.
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class TrackManifest
{
	public const string FileName = "tracks.json";

	[JsonProperty("tracks")] public List<TrackEntry> Tracks { get; set; } = new();
}

[JsonObject(MemberSerialization.OptIn)]
public class TrackEntry
{
	/// <summary>File name as it sits in the music folder, extension and all.</summary>
	[JsonProperty("file")] public string File { get; set; } = "";

	/// <summary>Situation names - see the mod page for the list. Unknown ones are reported and ignored.</summary>
	[JsonProperty("situations")] public string[] Situations { get; set; } = Array.Empty<string>();

	/// <summary>Shown by /music info. Defaults to the file name.</summary>
	[JsonProperty("title")] public string? Title { get; set; }

	/// <summary>Who made it. Shown alongside the title when the track is announced.</summary>
	[JsonProperty("artist")] public string? Artist { get; set; }

	/// <summary>Higher wins when several tracks fit. Defaults to 1.</summary>
	[JsonProperty("priority")] public float? Priority { get; set; }

	/// <summary>Per-track volume, 0 to 1. Defaults to 1.</summary>
	[JsonProperty("volume")] public float? Volume { get; set; }
}
