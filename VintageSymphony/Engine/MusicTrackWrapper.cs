using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VintageSymphony.Engine;

public sealed class MusicTrackWrapper : MusicTrack
{
	private static readonly GameTrackSituationLibrary SituationLibrary = new ();
	private IMusicTrack wrappedTrack;
	private AssetLocation DefaultAssetLocation => AssetLocation.Create("undefined");

	public MusicTrackWrapper(IMusicTrack wrappedTrack)
	{
		this.wrappedTrack = wrappedTrack;

		if (wrappedTrack is SurfaceMusicTrack wrappedSurfaceTrack)
		{
			Location = wrappedSurfaceTrack.Location ?? DefaultAssetLocation;
			Situation = SituationLibrary.GetSituationString(wrappedTrack);
			Priority = wrappedSurfaceTrack.Priority;
			StartPriority = wrappedSurfaceTrack.StartPriority;
			Title = TitleFrom(Location);
		}

		if (wrappedTrack is CaveMusicTrack)
		{
			Location = DefaultAssetLocation;
			Situation = SituationLibrary.GetSituationString(wrappedTrack);
			isCaveMusic = true;
			DisableCooldown = true;
			MinSunlight = 0;
		}

		InternalInitialize();
	}

	public override bool IsPlaying => wrappedTrack.IsActive;

	/// <summary>
	/// The game's musicconfig names its tracks, but the parser drops the name on the
	/// floor - so the file name is all there is, and "nadiya-spring" reads better as
	/// "Nadiya Spring" when it is announced.
	/// </summary>
	private static string TitleFrom(AssetLocation location)
	{
		var name = location.Path;
		if (name.StartsWith("music/"))
		{
			name = name.Substring("music/".Length);
		}

		if (name.EndsWith(".ogg"))
		{
			name = name.Substring(0, name.Length - ".ogg".Length);
		}

		var words = name.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1));
		return string.Join(' ', words);
	}

	/// <summary>
	/// BeginSort rolls the wrapped track's StartPriority, and selection reads it off this
	/// wrapper - so the roll has to be carried back over, or every wrapped track sorts on
	/// whatever the value happened to be when it was wrapped.
	/// </summary>
	public override void BeginSort()
	{
		wrappedTrack.BeginSort();
		StartPriority = wrappedTrack.StartPriority;
	}

	public override void Initialize(IAssetManager assetManager, ICoreClientAPI capi, IMusicEngine musicEngine)
	{
		wrappedTrack.Initialize(assetManager, capi, musicEngine);
	}

	/// <summary>
	/// The game's verdict on its own track, with one rule optionally waived: the
	/// survival/creative playlist split, see <see cref="Config.Configuration.HonourGamePlaylists"/>.
	/// The game returns the first rule that fails, so the playlist cannot be skipped by
	/// looking at the verdict; the track is asked as though it were on every playlist,
	/// and put back the way it was. Selection runs on the main thread, nothing else reads
	/// the field while it is changed, and the game's own loop is disabled by the patch.
	/// </summary>
	public override bool ShouldPlay(TrackedPlayerProperties props, ClimateCondition conds, BlockPos pos)
	{
		if (wrappedTrack is not SurfaceMusicTrack surface || VintageSymphony.Configuration.HonourGamePlaylists)
		{
			return wrappedTrack.ShouldPlay(props, conds, pos);
		}

		var onPlayList = surface.OnPlayList;
		surface.OnPlayList = "*";
		try
		{
			return surface.ShouldPlay(props, conds, pos);
		}
		finally
		{
			surface.OnPlayList = onPlayList;
		}
	}

	public override void BeginPlay(TrackedPlayerProperties props)
	{
		wrappedTrack.BeginPlay(props);
	}

	public override bool ContinuePlay(float dt, TrackedPlayerProperties props)
	{
		return wrappedTrack.ContinuePlay(dt, props);
	}

	public override void UpdateVolume()
	{
		wrappedTrack.UpdateVolume();
	}

	public override void FadeOut(float seconds, Action? onFadedOut = null)
	{
		wrappedTrack.FadeOut(seconds, onFadedOut);
	}

	public override void FastForward(float seconds)
	{
		wrappedTrack.FastForward(seconds);
	}
}