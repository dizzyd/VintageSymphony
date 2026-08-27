using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;
using VintageSymphony.Music;
using VintageSymphony.Situations.Scoring;

namespace VintageSymphony.Engine;

// ReSharper disable once ClassNeverInstantiated.Global
public class MusicEngine : BaseModSystem
{
	public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;
	public override double ExecuteOrder() => 1.6;

	private long playbackUpdateEventId;
	private const int PlaybackUpdateIntervalMs = 1 * 1000;
	private const int PlaybackUpdateDelayMs = 10 * 1000 + 50;

	private const int TrackCooldownCleanupIntervalMs = 2 * 60 * 1000;
	private TrackCooldownManager trackCooldownManager = null!;
	private long trackCooldownCleanupEventId;

	private SituationAssessor situationAssessor = null!;
	private const int SituationUpdateIntervalMs = 300;
	private readonly CancellationTokenSource situationUpdateCancellationTokenSource = new ();
	private CancellationToken SituationUpdateCancellationToken => situationUpdateCancellationTokenSource.Token;

	public SituationAssessor SituationAssessor => situationAssessor;
	private TrackedPlayerProperties PlayerProperties => VintageSymphony.ClientMain.playerProperties;
	private ILogger Logger => clientApi!.Logger;
	private MusicCurator musicCurator = null!;
	private Playback playback = null!;

	public Playback Playback => playback;
	public MusicTrack? CurrentMusicTrack => playback?.CurrentTrack;

	public override void StartClientSide(ICoreClientAPI api)
	{
		base.StartClientSide(api);

		clientApi = api;
		ClientSettings.Inst.Int.AddWatcher("musicFrequency", newValue => { playback.SetMusicFrequency(newValue); });
		ClientSettings.Inst.AddWatcher<int>("musicLevel", OnMusicLevelChanged);
	}

	protected override void OnGameStarted()
	{
		situationAssessor = new SituationAssessor(VintageSymphony.Instance.AttributeStorage);
		trackCooldownManager = new TrackCooldownManager(() => clientApi!.ElapsedMilliseconds);
		
		playback = new Playback(
			Logger,
			trackCooldownManager,
			() => PlayerProperties,
			() => clientApi!.ElapsedMilliseconds);

		musicCurator = new MusicCurator(clientApi!, situationAssessor, playback);

		// Set the initial music frequency
		playback.SetMusicFrequency(ClientSettings.MusicFrequency);

		playbackUpdateEventId =
			clientApi!.World.RegisterGameTickListener(UpdatePlayback, PlaybackUpdateIntervalMs, PlaybackUpdateDelayMs);
		trackCooldownCleanupEventId = clientApi.World.RegisterGameTickListener(
			_ => trackCooldownManager.CleanupRoutine(), TrackCooldownCleanupIntervalMs, TrackCooldownCleanupIntervalMs);
		
		TyronThreadPool.CreateDedicatedThread(UpdateSituation, "VintageSymphony-SituationUpdate").Start();
	}

	private void OnMusicLevelChanged(int volume)
	{
		playback.CurrentTrack?.UpdateVolume();
	}

	public override void Dispose()
	{
		void UnregisterTickListeners(long eventId)
		{
			if (eventId != 0L)
			{
				clientApi!.World.UnregisterGameTickListener(eventId);
			}
		}

		situationUpdateCancellationTokenSource.Cancel();
		UnregisterTickListeners(playbackUpdateEventId);
		UnregisterTickListeners(trackCooldownCleanupEventId);
		situationAssessor?.Dispose();
		base.Dispose();
	}


	private void UpdatePlayback(float dt)
	{
		if (!IsGameStarted || !TracksLoaded() || ClientSettings.MusicLevel == 0)
		{
			return;
		}

		musicCurator.Update(dt);
		playback.Update(dt);
	}


	private void UpdateSituation()
	{
		long lastUpdate = clientApi?.InWorldEllapsedMilliseconds ?? 0;
		while (!SituationUpdateCancellationToken.IsCancellationRequested)
		{
			long now = clientApi?.InWorldEllapsedMilliseconds ?? 0;

			// Seconds. The assessor smooths with 1 - exp(-strength * dt), so handing it
			// milliseconds made every step exp(-60) away from a straight assignment - the
			// smoothing, and every per-situation flag built on it, did nothing at all.
			situationAssessor.Update((now - lastUpdate) / 1000f);
			lastUpdate = now;
			
			try
			{
				Task.Delay(TimeSpan.FromMilliseconds(SituationUpdateIntervalMs), SituationUpdateCancellationToken)
					.Wait(SituationUpdateCancellationToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	public void NextTrack()
	{
		playback.NextTrack();
	}

	/// <summary>
	/// Rebuild the track pool under the current configuration. The game only hands us
	/// its track list through the patched engine tick, so this drops what we hold and
	/// lets the next tick refill it - within a second, rather than at the next restart.
	/// </summary>
	public void ReloadTracks()
	{
		if (!IsGameStarted)
		{
			return;
		}

		Logger.Notification("Reloading music tracks");
		playback.Reset();
		musicCurator.Tracks = new List<MusicTrack>();
	}

	public void LoadTracks(IMusicTrack[] allTracks, IMusicEngine gameMusicEngine)
	{
		// The game populates shuffledTracks on a background thread, so it can still be null
		// while OnEverySecond (and therefore our patch) is already ticking.
		if (allTracks == null || TracksLoaded())
		{
			return;
		}

		var sources = VintageSymphony.MusicSources;

		// Enabled, not installed: a source's music may arrive from a folder we registered
		// an origin for, or from a mod someone already had installed that happens to use
		// the same domain. Both are that source's music.
		var domains = sources.Enabled.Select(s => s.Id).ToHashSet();
		var filter = new TrackFilter(VintageSymphony.Configuration, domains);

		// Two kinds of track end up in the same pool: the ones the game parsed out of a
		// musicconfig.json, and the ones we built from a simple tracks.json ourselves.
		var fromGame = allTracks.Where(filter.KeepTrack).ToList();
		var tracks = fromGame
			.Select(t => t as MusicTrack ?? new MusicTrackWrapper(t))
			.ToList();

		var local = VintageSymphony.Configuration.LoadVintageSymphonyMusic
			? new LocalMusicLoader(sources, clientApi!).LoadTracks(gameMusicEngine)
			: new List<MusicTrack>();

		tracks.AddRange(local);
		musicCurator.Tracks = tracks;

		LogPoolComposition(fromGame, local, allTracks.Length);
	}

	/// <summary>
	/// What ended up in the pool, and where each part of it came from. The pool is built
	/// once and the configuration decides what is in it, so without this line an
	/// unexpected track playing later can only be guessed at.
	/// </summary>
	private void LogPoolComposition(IList<IMusicTrack> fromGame, IList<MusicTrack> local, int available)
	{
		var parts = new List<string>();

		foreach (var source in VintageSymphony.MusicSources.Enabled)
		{
			var count = fromGame.Count(t => t is SurfaceMusicTrack s && s.Location?.Domain == source.Id)
			            + local.Count(t => t.Location?.Domain == source.Id);
			parts.Add($"{count} {source.Id}");
		}

		parts.Add($"{fromGame.Count(t => t is SurfaceMusicTrack s && s.Location?.Domain == GlobalConstants.DefaultDomain)} Vintage Story");
		parts.Add($"{fromGame.Count(t => t is CaveMusicTrack)} cave");

		Logger.Notification("Loaded {0} music tracks ({1} offered by the game): {2}",
			musicCurator.Tracks.Count, available, string.Join(", ", parts));
	}

	private bool TracksLoaded()
	{
		return musicCurator is { Tracks.Count: > 0 };
	}
}