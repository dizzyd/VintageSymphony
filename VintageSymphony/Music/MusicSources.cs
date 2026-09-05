using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Common;

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

	/// <summary>
	/// The music that ships as the default experience. This is the asset domain its
	/// tracks are addressed under - the domain belongs to the music, not to this mod, so
	/// it stays as it is however the mod itself is named.
	/// </summary>
	public const string DefaultSourceId = "vintagesymphony";

	/// <summary>
	/// The game's own music. It is a source like any other as far as switching it on and
	/// off goes - it just arrives with the game rather than being downloaded.
	/// </summary>
	public const string GameSourceId = "game";

	public static bool IsBuiltIn(MusicSource source) => source.Id == GameSourceId;
	public static bool IsFromMod(MusicSource source) => source.Mod != null;

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
	public IEnumerable<MusicSource> Installed => Enabled.Where(HasMusicOnDisk);

	/// <summary>
	/// Sources with music to read: a folder on disk, or a mod's assets. A source with
	/// neither is one that was never downloaded, and has nothing to say.
	/// </summary>
	public IEnumerable<MusicSource> Playable => Enabled.Where(s => HasMusicOnDisk(s) || IsFromMod(s));

	public bool HasMusicOnDisk(MusicSource source) => Directory.Exists(MusicPathOf(source));

	public string DirectoryOf(MusicSource source) => Path.Combine(RootPath, SourcesFolder, source.Id);
	public string MusicPathOf(MusicSource source) => Path.Combine(DirectoryOf(source), MusicFolder);

	/// <summary>
	/// Put a source's folder in front of the asset manager during startup, before assets
	/// are read.
	/// </summary>
	public void RegisterOrigin(ICoreAPI api, MusicSource source)
	{
		api.Assets.AddModOrigin(source.Id, DirectoryOf(source));
	}

	/// <summary>
	/// The same, for a source that arrived - or changed - after the game had started.
	///
	/// AddModOrigin only queues an origin into CustomModOrigins, which the asset manager
	/// folds into Origins once during startup, so a late one would never be looked at.
	/// The reload matters just as much on a re-install, where the origin is already there
	/// but the files behind it have been replaced: without it the asset manager keeps
	/// serving what it cached, and an update appears to do nothing.
	/// </summary>
	public void RegisterOriginNow(ICoreClientAPI capi, MusicSource source)
	{
		var path = DirectoryOf(source);
		var known = capi.Assets.Origins.Any(o =>
			o is PathOrigin p && p.Domain == source.Id
			&& p.OriginPath.TrimEnd(Path.DirectorySeparatorChar) == path);

		if (!known)
		{
			capi.Assets.Origins.Add(new PathOrigin(source.Id, path));
		}

		capi.Assets.Reload(AssetCategory.music);
		logger.Notification("Music source '{0}' is now available from {1}", source.Id, path);
	}

	/// <summary>
	/// Bring the list in line with the mods that are loaded. A mod that ships music for
	/// this engine - a tracks.json under its music assets, or a musicconfig.json of our
	/// tracks - is listed as a source the moment it is seen, so that installing such a
	/// mod is all anyone has to do. When the mod goes, the entry we made for it goes too;
	/// an entry somebody wrote themselves under the same name is left alone, whatever
	/// happens to be loaded.
	///
	/// This can only run once assets are loaded, which is after the list was read from
	/// disk - so it is the engine's pool build that calls it, not <see cref="Load"/>.
	/// </summary>
	/// <param name="domains">Asset domains found shipping music for this engine.</param>
	/// <returns>Whether the list changed.</returns>
	public bool SyncModSources(IEnumerable<string> domains, IModLoader modLoader)
	{
		var shipping = domains.ToHashSet();
		var changed = false;

		foreach (var gone in Sources.Where(s => IsFromMod(s) && !shipping.Contains(s.Id)).ToList())
		{
			Sources.Remove(gone);
			logger.Notification("Mod '{0}' is no longer shipping music; dropped its source.", gone.Mod);
			changed = true;
		}

		foreach (var domain in shipping)
		{
			if (Sources.Any(s => s.Id == domain))
			{
				continue;
			}

			// The domain is usually the mod id, but a mod may put its music under any
			// name it likes, and then the name is all there is to go on.
			var mod = modLoader.GetMod(domain);
			Sources.Add(new MusicSource
			{
				Id = domain,
				Name = mod?.Info.Name ?? domain,
				Enabled = true,
				Mod = mod?.Info.ModID ?? domain
			});
			logger.Notification("Mod '{0}' ships music for Vintage Symphony; listed it as a source.", domain);
			changed = true;
		}

		if (changed)
		{
			Save();
		}

		return changed;
	}

	/// <summary>Forget a source and take its files with it.</summary>
	public void Remove(MusicSource source)
	{
		Sources.Remove(source);
		Save();

		var directory = DirectoryOf(source);
		try
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, true);
				logger.Notification("Removed music source '{0}' and deleted {1}", source.Id, directory);
			}
		}
		catch (Exception e)
		{
			logger.Error("Removed '{0}' but could not delete {1}: {2}", source.Id, directory, e.Message);
		}
	}

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

		EnsureBuiltInFirst();
	}

	/// <summary>
	/// The game's own music is always offered, and always at the top - including in lists
	/// written before it was one of these.
	/// </summary>
	private void EnsureBuiltInFirst()
	{
		var builtIn = Sources.FirstOrDefault(IsBuiltIn);
		if (builtIn == null)
		{
			builtIn = new MusicSource { Id = GameSourceId, Name = "Vintage Story's own music", Enabled = false };
			Sources.Add(builtIn);
		}

		Sources.Remove(builtIn);
		Sources.Insert(0, builtIn);
	}

	public void Save()
	{
		Directory.CreateDirectory(RootPath);
		// A file people are meant to read and edit should not be padded with nulls.
		File.WriteAllText(Path.Combine(RootPath, FileName),
			JsonConvert.SerializeObject(Sources, Formatting.Indented,
				new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
	}

	private static List<MusicSource> DefaultSources() => new()
	{
		new MusicSource
		{
			Id = GameSourceId,
			Name = "Vintage Story's own music",
			Enabled = false
		},
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
