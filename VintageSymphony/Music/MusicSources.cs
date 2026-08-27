using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace VintageSymphony.Music;

/// <summary>
/// The list of music sources and where they live on disk. This is deliberately a
/// separate file from the mod configuration: it is the thing players hand to each other,
/// and it changes for different reasons.
/// </summary>
public class MusicSources
{
	public const string FileName = "sources.json";
	public const string SourcesFolder = "sources";
	public const string MusicFolder = "music";

	/// <summary>The music that ships as the default experience.</summary>
	public const string DefaultSourceId = "vintagesymphony";

	private readonly ILogger logger;

	/// <summary>Directory holding sources.json, with the sources themselves beneath it.</summary>
	public string RootPath { get; }

	public List<MusicSource> Sources { get; private set; } = new();

	public MusicSources(string rootPath, ILogger logger)
	{
		RootPath = rootPath;
		this.logger = logger;
	}

	public IEnumerable<MusicSource> Enabled => Sources.Where(s => s.Enabled && s.Id.Length > 0);

	/// <summary>Sources that are enabled and actually have music sitting on disk.</summary>
	public IEnumerable<MusicSource> Installed => Enabled.Where(s => Directory.Exists(MusicPathOf(s)));

	public string DirectoryOf(MusicSource source) => Path.Combine(RootPath, SourcesFolder, source.Id);
	public string MusicPathOf(MusicSource source) => Path.Combine(DirectoryOf(source), MusicFolder);

	public void Load()
	{
		var path = Path.Combine(RootPath, FileName);
		if (!File.Exists(path))
		{
			Sources = DefaultSources();
			Save();
			logger.Notification("Wrote a default music source list to {0}", path);
			return;
		}

		try
		{
			Sources = JsonConvert.DeserializeObject<List<MusicSource>>(File.ReadAllText(path)) ?? new();
		}
		catch (Exception e)
		{
			// A broken list must not cost the player their music: fall back to the
			// default and leave their file alone to be fixed.
			logger.Error("Could not read {0}: {1}. Using the default music sources.", path, e.Message);
			Sources = DefaultSources();
		}
	}

	public void Save()
	{
		Directory.CreateDirectory(RootPath);
		File.WriteAllText(Path.Combine(RootPath, FileName),
			JsonConvert.SerializeObject(Sources, Formatting.Indented));
	}

	private static List<MusicSource> DefaultSources() => new()
	{
		new MusicSource
		{
			Id = DefaultSourceId,
			Name = "Vintage Symphony music",
			Enabled = true,
			Url = "https://api.github.com/repos/Dantoes/VintageSymphony-Assets-Release/releases",
			Compatible = "1.1"
		}
	};
}
