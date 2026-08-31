using System.IO.Compression;
using Vintagestory.API.Common;
using VintageSymphony.Update;

namespace VintageSymphony.Music;

/// <summary>What a source is offering, once someone has asked.</summary>
public class AvailableRelease
{
	public Version? Version { get; init; }
	public string DownloadUrl { get; init; } = "";
	public long SizeBytes { get; init; }
}

/// <summary>
/// Fetches and installs a music source. Nothing here runs on its own: every method is
/// something the player pressed a button for.
/// </summary>
public class MusicSourceInstaller
{
	private static readonly HttpClient Shared = NewClient();
	private readonly HttpClient httpClient;
	private readonly MusicSources sources;
	private readonly ILogger logger;

	/// <param name="httpClient">
	/// Left out in normal use. A caller supplies one to reach somewhere the default
	/// client cannot - a test serving a pack over loopback, for instance.
	/// </param>
	public MusicSourceInstaller(MusicSources sources, ILogger logger, HttpClient? httpClient = null)
	{
		this.sources = sources;
		this.logger = logger;
		this.httpClient = httpClient ?? Shared;
	}

	private static HttpClient NewClient()
	{
		var client = new HttpClient();
		client.DefaultRequestHeaders.Add("User-Agent", "VintageSymphony");
		return client;
	}

	/// <summary>
	/// What this source has on offer. A GitHub releases endpoint is asked for its newest
	/// release the source says it is compatible with; anything else is taken at face
	/// value as a zip to download.
	/// </summary>
	public async Task<AvailableRelease?> CheckAsync(MusicSource source)
	{
		if (string.IsNullOrWhiteSpace(source.Url))
		{
			return null;
		}

		try
		{
			// A releases endpoint lists versions to choose between; anything else is taken
			// at face value as a zip. Matching on the path as well as the host means a
			// mirror or a GitHub Enterprise host works the same way.
			if (source.Url.Contains("api.github.com") || source.Url.TrimEnd('/').EndsWith("/releases"))
			{
				var releases = await new GitHubReleaseFetcher(httpClient).GetAllReleasesAsync(source.Url);
				var release = releases.FirstOrDefault(r => IsCompatible(source, r.Version));
				if (release == null)
				{
					logger.Warning("No release of '{0}' is marked compatible with this version.", source.Id);
					return null;
				}

				return new AvailableRelease
				{
					Version = release.Version,
					DownloadUrl = release.DownloadUrl,
					SizeBytes = await SizeOfAsync(release.DownloadUrl)
				};
			}

			return new AvailableRelease
			{
				DownloadUrl = source.Url,
				SizeBytes = await SizeOfAsync(source.Url)
			};
		}
		catch (Exception e)
		{
			logger.Error("Could not reach '{0}': {1}", source.Id, e.Message);
			return null;
		}
	}

	/// <summary>
	/// Whether what is on offer is what is already installed. Only a source that publishes
	/// versions can say - for the rest, asking again means fetching again, because there
	/// is nothing to compare.
	/// </summary>
	public bool IsUpToDate(MusicSource source, AvailableRelease release)
	{
		return release.Version != null
		       && source.Installed == release.Version.ToString()
		       && sources.HasMusicOnDisk(source);
	}

	/// <summary>
	/// Compatible when the source names a major.minor and the release matches it. A source
	/// that names nothing takes whatever it is given.
	/// </summary>
	private static bool IsCompatible(MusicSource source, Version version)
	{
		if (string.IsNullOrWhiteSpace(source.Compatible) || !Version.TryParse(NormalizeVersion(source.Compatible), out var wanted))
		{
			return true;
		}

		return version.Major == wanted.Major && version.Minor == wanted.Minor;
	}

	private static string NormalizeVersion(string version) => version.Contains('.') ? version : version + ".0";

	private async Task<long> SizeOfAsync(string url)
	{
		using var request = new HttpRequestMessage(HttpMethod.Head, url);
		var response = await httpClient.SendAsync(request);
		return response.Content.Headers.ContentLength ?? 0;
	}

	/// <summary>
	/// Download and unpack, reporting progress as a fraction. The existing install is left
	/// untouched until the new one is unpacked and looks like music - a dropped connection
	/// used to lose the player the pack they already had.
	/// </summary>
	public async Task InstallAsync(MusicSource source, AvailableRelease release,
		Action<float> onProgress, CancellationToken cancellation)
	{
		var target = sources.DirectoryOf(source);
		var staging = target + ".incoming";
		var archive = target + ".zip";

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			await DownloadAsync(release.DownloadUrl, archive, onProgress, cancellation);

			if (Directory.Exists(staging))
			{
				Directory.Delete(staging, true);
			}

			ExtractMusic(archive, staging);

			if (!Directory.Exists(Path.Combine(staging, MusicSources.MusicFolder)))
			{
				throw new ModException($"'{source.Id}' downloaded, but there is no music in it.");
			}

			// Only now is the old copy expendable.
			if (Directory.Exists(target))
			{
				Directory.Delete(target, true);
			}

			Directory.Move(staging, target);
			// Null when the source does not publish versions - whether it is installed is
			// a question for the disk, not for this.
			source.Installed = release.Version?.ToString();
			sources.Save();

			logger.Notification("Installed music source '{0}'{1}", source.Id,
				release.Version == null ? "" : " " + release.Version);
		}
		finally
		{
			SafeDelete(archive);
			if (Directory.Exists(staging))
			{
				try { Directory.Delete(staging, true); } catch { /* best effort */ }
			}
		}
	}

	private async Task DownloadAsync(string url, string destination, Action<float> onProgress,
		CancellationToken cancellation)
	{
		using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
		response.EnsureSuccessStatusCode();

		var total = response.Content.Headers.ContentLength ?? 0;
		await using var incoming = await response.Content.ReadAsStreamAsync(cancellation);
		await using var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

		var buffer = new byte[81920];
		long read = 0;
		int got;
		while ((got = await incoming.ReadAsync(buffer, cancellation)) > 0)
		{
			await file.WriteAsync(buffer.AsMemory(0, got), cancellation);
			read += got;
			if (total > 0)
			{
				onProgress((float)read / total);
			}
		}
	}

	/// <summary>
	/// Pull the music out of an archive, whatever shape it arrives in: a mod-style zip
	/// keeps it at assets/&lt;domain&gt;/music, a plain pack might have a music folder at the
	/// root, and someone might just zip up their .ogg files.
	/// </summary>
	private void ExtractMusic(string archivePath, string staging)
	{
		var musicPath = Path.Combine(staging, MusicSources.MusicFolder);
		Directory.CreateDirectory(musicPath);

		using var archive = ZipFile.OpenRead(archivePath);
		foreach (var entry in archive.Entries)
		{
			if (entry.FullName.EndsWith('/') || entry.Length == 0)
			{
				continue;
			}

			var relative = MusicRelativePath(entry.FullName);
			if (relative == null)
			{
				continue;
			}

			// Zip entries are attacker-controlled text: an entry called ../../something
			// would otherwise be written wherever it liked.
			var destination = Path.GetFullPath(Path.Combine(musicPath, relative));
			if (!destination.StartsWith(Path.GetFullPath(musicPath) + Path.DirectorySeparatorChar))
			{
				logger.Warning("Ignoring {0}: it points outside the music folder.", entry.FullName);
				continue;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			entry.ExtractToFile(destination, true);
		}
	}

	/// <summary>Where an archive entry belongs under the music folder, or null to skip it.</summary>
	private static string? MusicRelativePath(string entryPath)
	{
		var normalized = entryPath.Replace('\\', '/');
		var parts = normalized.Split('/');

		// An entry that tries to climb out is not a track that happens to be oddly named:
		// drop it rather than keeping whatever it was pointing at.
		if (parts.Any(p => p == ".."))
		{
			return null;
		}

		var musicAt = Array.FindIndex(parts, p => p.Equals(MusicSources.MusicFolder, StringComparison.OrdinalIgnoreCase));
		if (musicAt >= 0)
		{
			return string.Join('/', parts.Skip(musicAt + 1));
		}

		// No music folder in the archive: take the audio and the manifests wherever they sit.
		var name = parts[^1];
		var keep = name.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
		           || name.Equals(TrackManifest.FileName, StringComparison.OrdinalIgnoreCase)
		           || name.Equals("musicconfig.json", StringComparison.OrdinalIgnoreCase)
		           || name.Equals("attributions.txt", StringComparison.OrdinalIgnoreCase);

		return keep ? name : null;
	}

	private static void SafeDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
			// best effort
		}
	}
}
