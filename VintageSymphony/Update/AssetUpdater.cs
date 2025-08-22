using Vintagestory.API.Common;

namespace VintageSymphony.Update;

// ReSharper disable once UnusedType.Global
public class AssetUpdater : BaseModSystem
{
	private const string ApiUrl = "https://api.github.com/repos/Dantoes/VintageSymphony-Assets-Release/releases";
	private const string AssetModId = "vintagesymphonyassets";

	private readonly GitHubReleaseFetcher releaseFetcher = new();
	private Task<Release?>? releaseFetcherTask;
	private Task? upgradeTask;
	private UpdateInstalledOverlay? updateOverlay;
	private UpdateProgressOverlay? progressOverlay;
	private float downloadProgress;
	private bool isDownloading;
	private CancellationTokenSource? downloadCancellationSource;

	public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;
	private long releaseFetcherListener;
	private long upgradeListener;
	private long progressUpdateListener;
	private long showOverlayListener;

	protected override void OnGameStarted()
	{
		clientApi!.Logger.Notification($"Checking {AssetModId} for available updates…");

		releaseFetcherListener = clientApi!.World.RegisterGameTickListener(FetchLatestRelease, 1000, 2000);
		releaseFetcherTask = releaseFetcher.GetLatestReleaseAsync(ApiUrl);
		updateOverlay = new UpdateInstalledOverlay(clientApi);
		progressOverlay = new UpdateProgressOverlay(clientApi);
	}

	private void FetchLatestRelease(float dt)
	{
		if (!releaseFetcherTask?.IsCompleted ?? true)
		{
			return;
		}

		clientApi!.World.UnregisterGameTickListener(releaseFetcherListener);
		var release = releaseFetcherTask?.Result;
		if (release == null)
		{
			clientApi.Logger.Error($"Failed to get {AssetModId} release information from GitHub");
			return;
		}

		InterpretRelease(release);
	}

	private void InterpretRelease(Release release)
	{
		var installedVersion = GetInstalledVersion();
		if (installedVersion >= release.Version)
		{
			clientApi!.Logger.Notification($"{AssetModId} is up to date");
			return;
		}

		clientApi!.Logger.Notification($"Updating {AssetModId} to version {release.Version}…");

		// Show progress overlay and start update
		isDownloading = true;
		downloadProgress = 0f;
		progressOverlay!.TryOpen();

		// Register listener to update progress UI
		progressUpdateListener = clientApi.World.RegisterGameTickListener(UpdateProgressUI, 50, 0);

		// Start upgrade task
		upgradeTask = UpgradeToRelease(release, installedVersion != null);
		upgradeListener = clientApi.World.RegisterGameTickListener(CheckUpgradeProgress, 1000, 1000);
	}

	private void UpdateProgressUI(float dt)
	{
		if (!isDownloading)
		{
			clientApi!.World.UnregisterGameTickListener(progressUpdateListener);
			progressOverlay!.TryClose();
			return;
		}

		progressOverlay!.UpdateProgress(downloadProgress);
	}

	private void CheckUpgradeProgress(float obj)
	{
		if (!upgradeTask?.IsCompleted ?? true)
		{
			return;
		}

		clientApi!.World.UnregisterGameTickListener(upgradeListener);

		// Download complete, clean up
		isDownloading = false;
		progressOverlay!.TryClose();

		// Show completion notification
		updateOverlay!.TryOpen();
		showOverlayListener = clientApi!.World.RegisterGameTickListener(CloseUpdateOverlay, 1000, 60000);
	}

	private async Task UpgradeToRelease(Release release, bool deleteObsoleteFiles)
	{
		string modsPath = Path.Combine(clientApi!.DataBasePath, "Mods");
		string[] obsoleteModFiles = Directory.GetFiles(modsPath, $"{AssetModId}*");

		await DownloadReleaseWithProgressAsync(release);

		if (!deleteObsoleteFiles)
		{
			return;
		}

		foreach (var obsoleteModFile in obsoleteModFiles)
		{
			try
			{
				File.Delete(obsoleteModFile);
			}
			catch (Exception e)
			{
				clientApi.Logger.Error($"Failed to delete obsolete mod file: {e.Message}, Path: {obsoleteModFile}");
			}
		}
	}

	private async Task DownloadReleaseWithProgressAsync(Release release)
	{
		try
		{
			using HttpClient client = new HttpClient();
			string filePath = Path.Combine(clientApi!.DataBasePath, "Mods", release.FileName);
			downloadCancellationSource = new CancellationTokenSource();

			// Get file size first to track progress
			var response = await client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead,
				downloadCancellationSource.Token);
			var totalBytes = response.Content.Headers.ContentLength ?? 0;

			if (totalBytes == 0)
			{
				// Fall back to simple download if we can't get the file size
				byte[] fileData = await client.GetByteArrayAsync(release.DownloadUrl);
				await File.WriteAllBytesAsync(filePath, fileData);
				return;
			}

			await using var stream = await response.Content.ReadAsStreamAsync(downloadCancellationSource.Token);
			await using var fileStream =
				new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

			var buffer = new byte[8192];
			var bytesRead = 0;
			var totalBytesRead = 0L;

			while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, downloadCancellationSource.Token)) > 0)
			{
				await fileStream.WriteAsync(buffer, 0, bytesRead, downloadCancellationSource.Token);
				totalBytesRead += bytesRead;
				downloadProgress = (float)totalBytesRead / totalBytes;
			}
		}
		catch (TaskCanceledException)
		{
			clientApi!.Logger.Notification($"Download of {AssetModId} was cancelled.");
		}
		catch (Exception ex)
		{
			clientApi!.Logger.Error($"Failed to download {AssetModId} release: {ex.Message}");
		}
		finally
		{
			downloadCancellationSource?.Dispose();
			downloadCancellationSource = null;
		}
	}

	private Version? GetInstalledVersion()
	{
		var versionString = GetModInfo()?.Version;
		return (versionString == null) ? null : new Version(versionString);
	}

	private ModInfo? GetModInfo()
	{
		return clientApi!.ModLoader.GetMod(AssetModId)?.Info;
	}

	private void CloseUpdateOverlay(float obj)
	{
		clientApi!.World.UnregisterGameTickListener(showOverlayListener);
		updateOverlay!.TryClose();
	}
}