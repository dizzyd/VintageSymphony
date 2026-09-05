using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using VintageSymphony.Situations;
using MusicTrack = VintageSymphony.Engine.MusicTrack;

namespace VintageSymphony.Music;

/// <summary>
/// Builds tracks for sources that use the simple tracks.json manifest - a folder of ours
/// on disk, or a mod that put one under its music assets.
///
/// A source whose music folder holds the game's own musicconfig.json is left well alone -
/// the game parses that itself and the tracks arrive through the patched music engine
/// like any other. This only covers the folders the game knows nothing about.
/// </summary>
public class LocalMusicLoader
{
	private const string GameMusicConfig = "musicconfig.json";

	private readonly MusicSources sources;
	private readonly ICoreClientAPI clientApi;
	private readonly ILogger logger;

	public LocalMusicLoader(MusicSources sources, ICoreClientAPI clientApi)
	{
		this.sources = sources;
		this.clientApi = clientApi;
		logger = clientApi.Logger;
	}

	/// <summary>
	/// Tracks from every installed source that carries a tracks.json, ready to play.
	/// </summary>
	public List<MusicTrack> LoadTracks(IMusicEngine musicEngine)
	{
		var tracks = new List<MusicTrack>();

		foreach (var source in sources.Playable)
		{
			if (MusicSources.IsFromMod(source))
			{
				tracks.AddRange(LoadFromMod(source, musicEngine));
				continue;
			}

			var musicPath = sources.MusicPathOf(source);

			// The game's format wins if both are present - it is the more expressive one,
			// and it is what a published pack ships. At startup the game has already
			// parsed it; for a source that arrived since, this is where it gets read.
			if (File.Exists(Path.Combine(musicPath, GameMusicConfig)))
			{
				tracks.AddRange(LoadGameFormat(source, musicEngine));
				continue;
			}

			var manifest = ReadOrCreateManifest(source, musicPath);
			if (manifest == null)
			{
				continue;
			}

			foreach (var entry in manifest.Tracks)
			{
				var track = BuildTrack(source, entry, musicEngine,
					file => File.Exists(Path.Combine(musicPath, file)), musicPath);
				if (track != null)
				{
					tracks.Add(track);
				}
			}
		}

		return tracks;
	}

	/// <summary>
	/// The asset domains of every mod shipping a tracks.json under its music assets.
	/// Folders of ours are origins too, so they show up here as well - which is fine,
	/// since they are already sources and the sync leaves them be.
	/// </summary>
	public static IEnumerable<string> DomainsShippingManifests(ICoreClientAPI capi)
	{
		var path = "music/" + TrackManifest.FileName;
		return capi.Assets.GetLocations(path)
			.Where(location => location.Path == path)
			.Select(location => location.Domain)
			.Distinct();
	}

	/// <summary>
	/// A source that arrived inside a mod. Its files are assets rather than a folder we
	/// can walk, so the manifest is read through the asset manager and a track's file is
	/// looked for there too; nothing gets written, since there is nowhere of ours to
	/// write it. The game's format takes precedence here as it does on disk.
	/// </summary>
	private List<MusicTrack> LoadFromMod(MusicSource source, IMusicEngine musicEngine)
	{
		var tracks = new List<MusicTrack>();

		if (clientApi.Assets.Exists(new AssetLocation(source.Id, "music/" + GameMusicConfig)))
		{
			return LoadGameFormat(source, musicEngine);
		}

		var manifestLocation = new AssetLocation(source.Id, "music/" + TrackManifest.FileName);
		var asset = clientApi.Assets.TryGet(manifestLocation);
		if (asset == null)
		{
			return tracks;
		}

		TrackManifest? manifest;
		try
		{
			manifest = JsonConvert.DeserializeObject<TrackManifest>(asset.ToText());
		}
		catch (Exception e)
		{
			logger.Error("Could not read {0}: {1}. Skipping the '{2}' music source.",
				manifestLocation, e.Message, source.Id);
			return tracks;
		}

		foreach (var entry in manifest?.Tracks ?? new List<TrackEntry>())
		{
			var track = BuildTrack(source, entry, musicEngine,
				file => clientApi.Assets.Exists(new AssetLocation(source.Id, "music/" + file)),
				"the assets of mod '" + source.Mod + "'");
			if (track != null)
			{
				tracks.Add(track);
			}
		}

		return tracks;
	}

	/// <summary>
	/// Read a pack in the game's own format, the same way the game does. Anything the game
	/// already parsed at startup is filtered out by location when the pool is built, so
	/// reading it again here costs nothing and lets a pack downloaded mid-session play
	/// without a restart.
	/// </summary>
	private List<MusicTrack> LoadGameFormat(MusicSource source, IMusicEngine musicEngine)
	{
		var tracks = new List<MusicTrack>();
		var location = new AssetLocation(source.Id, "music/" + GameMusicConfig);

		try
		{
			var asset = clientApi.Assets.TryGet(location);
			if (asset == null)
			{
				logger.Warning("'{0}' has a {1} that the asset manager cannot see yet.",
					source.Id, GameMusicConfig);
				return tracks;
			}

			var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };
			settings.Converters.Add(new AssetLocationJsonParser(source.Id));

			var config = asset.ToObject<MusicConfig>(settings);
			foreach (var track in config?.Tracks ?? Array.Empty<IMusicTrack>())
			{
				if (track is not MusicTrack ours)
				{
					continue;
				}

				ours.Initialize(clientApi.Assets, clientApi, musicEngine);
				tracks.Add(ours);
			}
		}
		catch (Exception e)
		{
			logger.Error("Could not read the {0} of '{1}': {2}", GameMusicConfig, source.Id, e.Message);
		}

		return tracks;
	}

	private TrackManifest? ReadOrCreateManifest(MusicSource source, string musicPath)
	{
		var manifestPath = Path.Combine(musicPath, TrackManifest.FileName);

		if (!File.Exists(manifestPath))
		{
			return CreateStarterManifest(source, musicPath, manifestPath);
		}

		try
		{
			return JsonConvert.DeserializeObject<TrackManifest>(File.ReadAllText(manifestPath));
		}
		catch (Exception e)
		{
			logger.Error("Could not read {0}: {1}. Skipping the '{2}' music source.",
				manifestPath, e.Message, source.Id);
			return null;
		}
	}

	/// <summary>
	/// Someone dropped files in and wrote nothing. Rather than ignore them, list what is
	/// there as Calm and tell them where to change it - a playable starting point beats an
	/// empty folder and a silent mod.
	/// </summary>
	private TrackManifest? CreateStarterManifest(MusicSource source, string musicPath, string manifestPath)
	{
		var audioFiles = Directory.GetFiles(musicPath, "*.ogg").OrderBy(f => f).ToList();
		if (audioFiles.Count == 0)
		{
			return null;
		}

		var manifest = new TrackManifest
		{
			Tracks = audioFiles
				.Select(f => new TrackEntry
				{
					File = Path.GetFileName(f),
					Situations = new[] { Situation.Calm.ToString().ToLowerInvariant() }
				})
				.ToList()
		};

		try
		{
			File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented,
					new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
			logger.Notification(
				"Found {0} untracked music file(s) in '{1}'. Listed them as Calm in {2} - edit it to say when they should play.",
				manifest.Tracks.Count, source.Id, manifestPath);
		}
		catch (Exception e)
		{
			// Still play them this session even if the folder is read-only.
			logger.Warning("Could not write {0}: {1}", manifestPath, e.Message);
		}

		return manifest;
	}

	/// <param name="hasFile">Whether a file the manifest names is actually there.</param>
	/// <param name="where">Where that would be, for the warning when it is not.</param>
	private MusicTrack? BuildTrack(MusicSource source, TrackEntry entry, IMusicEngine musicEngine,
		System.Func<string, bool> hasFile, string where)
	{
		if (string.IsNullOrWhiteSpace(entry.File))
		{
			logger.Warning("A track in '{0}' has no file name; skipping it.", source.Id);
			return null;
		}

		if (!hasFile(entry.File))
		{
			logger.Warning("'{0}' lists {1}, which is not in {2}; skipping it.",
				source.Id, entry.File, where);
			return null;
		}

		var situations = ParseSituations(source, entry);
		if (situations.Length == 0)
		{
			logger.Warning("'{0}' lists {1} with no situation it can play in; skipping it.",
				source.Id, entry.File);
			return null;
		}

		var track = new MusicTrack
		{
			// SurfaceMusicTrack.Initialize turns this into "music/<name>.ogg" itself, so
			// it wants the bare name.
			Location = new AssetLocation(source.Id, Path.GetFileNameWithoutExtension(entry.File)),
			Situation = string.Join("|", situations),
			Volume = entry.Volume ?? 1f,

			// The base class defaults to needing daylight, which would silently keep a
			// hand-added track from ever playing underground or at night.
			MinSunlight = 0
		};

		track.Title = entry.Title ?? Path.GetFileNameWithoutExtension(entry.File);

		if (entry.Priority.HasValue)
		{
			track.Priority = entry.Priority.Value;
		}

		track.Initialize(clientApi.Assets, clientApi, musicEngine);
		return track;
	}

	private string[] ParseSituations(MusicSource source, TrackEntry entry)
	{
		var known = new List<string>();

		foreach (var name in entry.Situations)
		{
			if (Enum.TryParse<Situation>(name, true, out _))
			{
				known.Add(name.ToLowerInvariant());
			}
			else
			{
				logger.Warning("'{0}' lists {1} for {2}, which is not a situation; ignoring it.",
					source.Id, name, entry.File);
			}
		}

		return known.ToArray();
	}
}
