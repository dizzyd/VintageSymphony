using Vintagestory.API.Common;

namespace VintageSymphony.Engine;

/// <summary>
/// A line per track start, in a file the player can send along with "it keeps playing
/// the same songs". Each line says what played, how many times this sitting, and the
/// shape of the draw it came from - how many tracks fit the moment, and how many of
/// those were off cooldown. A small fit count says the rules are narrowing the pool; a
/// small off-cooldown count says the pool is fine and the cooldown is what repeats.
/// Notes from the engine - the pool it built, the cooldown in force - go in between.
///
/// The file is kept under <see cref="DefaultMaxBytes"/>: past that, the oldest lines go
/// until it is half the size, so the trim is a rare event rather than a cost on every
/// start. At a hundred-odd bytes a line that is several thousand tracks of history,
/// which is more than any repeat complaint needs.
/// </summary>
public class TrackHistoryLog
{
	public const long DefaultMaxBytes = 1024 * 1024;

	/// <summary>Per sitting, like the announcer's memory: rejoining a world is the same listen.</summary>
	private static readonly Dictionary<string, int> Plays = new();

	private readonly string path;
	private readonly Func<bool> wanted;
	private readonly Func<DateTime> now;
	private readonly ILogger? logger;
	private readonly long maxBytes;
	private bool failed;

	/// <param name="path">The file; appended to, created when first written.</param>
	/// <param name="wanted">Whether the player asked for the log; read each time.</param>
	/// <param name="now">The clock, for tests.</param>
	/// <param name="maxBytes">The size past which the oldest lines are dropped; for tests.</param>
	public TrackHistoryLog(string path, Func<bool> wanted, ILogger? logger = null, Func<DateTime>? now = null,
		long maxBytes = DefaultMaxBytes)
	{
		this.path = path;
		this.wanted = wanted;
		this.logger = logger;
		this.now = now ?? (() => DateTime.Now);
		this.maxBytes = maxBytes;
	}

	public string Path => path;

	public void Record(TrackStart start)
	{
		if (!wanted())
		{
			return;
		}

		var key = KeyOf(start.Track);
		Plays[key] = Plays.GetValueOrDefault(key) + 1;
		Append(Describe(start, Plays[key], now()));
	}

	/// <summary>Something the engine did that bears on what plays: the pool it built, say.</summary>
	public void Note(string text)
	{
		if (wanted())
		{
			Append($"{Stamp(now())}  -- {text}");
		}
	}

	public static string Describe(TrackStart start, int plays, DateTime at)
	{
		var how = start.How switch
		{
			TrackStart.Tier.Recycled => "; everything that fit was on cooldown",
			TrackStart.Tier.OnlyFit => "; the only track that fit",
			_ => "",
		};

		return $"{Stamp(at)}  {start.Situation,-13} {start.Track.Title}  [{KeyOf(start.Track)}]"
		       + $"  play #{plays} this sitting"
		       + $"  fit {start.Fit} of {start.InPlaylist}, {start.OffCooldown} off cooldown{how}";
	}

	private static string Stamp(DateTime at) => at.ToString("yyyy-MM-dd HH:mm:ss");

	private static string KeyOf(MusicTrack track) => track.Location?.ToString() ?? track.Title;

	private void Append(string line)
	{
		if (failed)
		{
			return;
		}

		try
		{
			Trim();
			File.AppendAllText(path, line + Environment.NewLine);
		}
		catch (Exception e)
		{
			// Once. A log that cannot be written is not worth a warning per track.
			failed = true;
			logger?.Warning("Could not write the track history to {0}: {1}", path, e.Message);
		}
	}

	/// <summary>
	/// Drop the oldest lines once the file is over its limit, keeping the newest half.
	/// Whole lines only: the cut lands on the first line break past the halfway mark,
	/// so the line at the top of what remains is a complete one.
	/// </summary>
	private void Trim()
	{
		var info = new FileInfo(path);
		if (!info.Exists || info.Length <= maxBytes)
		{
			return;
		}

		var bytes = File.ReadAllBytes(path);
		var from = bytes.Length - (int)(maxBytes / 2);
		while (from < bytes.Length && bytes[from - 1] != (byte)'\n')
		{
			from++;
		}

		File.WriteAllBytes(path, bytes[from..]);
	}

	/// <summary>Start the sitting over, for tests.</summary>
	public static void Forget() => Plays.Clear();
}
