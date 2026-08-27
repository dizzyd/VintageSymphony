using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.Client.NoObf;
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
		long lastUpdate = 0;
		while (!SituationUpdateCancellationToken.IsCancellationRequested)
		{
			long now = clientApi?.InWorldEllapsedMilliseconds ?? 0;
			situationAssessor.Update(now - lastUpdate);
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

	public void LoadTracks(IMusicTrack[] allTracks)
	{
		// The game populates shuffledTracks on a background thread, so it can still be null
		// while OnEverySecond (and therefore our patch) is already ticking.
		if (allTracks == null || TracksLoaded())
		{
			return;
		}

		var filter = new TrackFilter(VintageSymphony.Configuration, Mod.Info.ModID);
		musicCurator.Tracks = allTracks
			.Where(filter.KeepTrack)
			.Select(t => t as MusicTrack ?? new MusicTrackWrapper(t))
			.ToList();
	}

	private bool TracksLoaded()
	{
		return musicCurator is { Tracks.Count: > 0 };
	}
}