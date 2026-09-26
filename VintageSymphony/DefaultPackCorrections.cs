using VintageSymphony.Engine;
using VintageSymphony.Music;
using VintageSymphony.Situations;

namespace VintageSymphony;

/// <summary>
/// Situations for the default pack's tracks where its own musicconfig.json leaves them
/// short - the same kind of table <see cref="GameTrackSituationLibrary"/> keeps for the
/// game's music. The pack is released from a repository this fork cannot write to, so
/// its data is corrected here, on load, and only where it is still wrong: a pack that
/// fixes a track itself is left as it is.
/// </summary>
public static class DefaultPackCorrections
{
	/// <summary>
	/// Pack 1.1.0 ships these with "situation": "", so they join no playlist and can
	/// never play.
	/// </summary>
	private static readonly Dictionary<string, string> Untagged = new()
	{
		["deskant_-_crying_winds"] = "calm|idle",
		["jon_algar_-_natures_way"] = "calm|idle|adventure",
	};

	/// <summary>Correct the default pack's tracks in place; returns how many changed.</summary>
	public static int Apply(IEnumerable<MusicTrack> tracks)
	{
		var changed = 0;
		foreach (var track in tracks.Where(t => t.Location?.Domain == MusicSources.DefaultSourceId))
		{
			var tags = Correct(NameOf(track), track.TrackSituations);
			if (tags != null)
			{
				track.SetSituation(tags);
				changed++;
			}
		}

		return changed;
	}

	/// <summary>The situation string a track should have, or null if it is fine as it is.</summary>
	public static string? Correct(string name, Situation[] situations)
	{
		if (situations.Length == 0)
		{
			return Untagged.GetValueOrDefault(name);
		}

		// Idle is standing still at home, which is most of an evening of building, and the
		// pack tags two tracks for it by day in winter against fifteen for Calm. Anything
		// peaceful enough to play at home while moving is peaceful enough while not.
		if (situations.Contains(Situation.Calm) && !situations.Contains(Situation.Idle))
		{
			return string.Join('|', situations.Append(Situation.Idle).Select(s => s.ToString().ToLowerInvariant()));
		}

		return null;
	}

	private static string NameOf(MusicTrack track)
	{
		var path = track.Location?.Path ?? "";
		var slash = path.LastIndexOf('/');
		var name = slash >= 0 ? path[(slash + 1)..] : path;
		return name.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
	}
}
