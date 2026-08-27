using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using VintageSymphony.Situations;

namespace VintageSymphony.Engine;

[JsonObject(MemberSerialization.OptIn)]
public class MusicTrack : SurfaceMusicTrack
{
	[JsonProperty]
	public string Situation = "";

	[JsonProperty]
	public string Source = "";

	[JsonProperty(propertyName: "title")]
#pragma warning disable CS0649
	private string? trackTitle;
#pragma warning restore CS0649

	/// <summary>
	/// What /music info shows. Settable so tracks built from a tracks.json manifest can
	/// carry the name their author gave them rather than a file path.
	/// </summary>
	public string Title
	{
		get => trackTitle ?? Name;
		set => trackTitle = value;
	}

	[JsonProperty]
	public float MinTemperature = -99;

	[JsonProperty]
	public float MinWorldGenRainfall = 0;

	[JsonProperty]
	public float MaxWorldGenRainfall = 1;

	[JsonProperty]
	public float MinWorldGenTemperature = -99;

	[JsonProperty]
	public float MaxWorldGenTemperature = 99;

	// Sunlight value is from in-game data.
	[JsonProperty]
	public float MaxSunlight = 32f;
	
	[JsonProperty]
	public float MinDaylight = 0f;
	
	[JsonProperty]
	public float MaxDaylight = 2f;

	[JsonProperty]
	public float Volume = 1f;

	private float GlobalVolume => VintageSymphony.Configuration.GlobalVolume;
	private bool volumeSet = false;

	public Situation[] TrackSituations = Array.Empty<Situation>();

	[JsonProperty]
	public bool DisableCooldown = false;

	public virtual bool IsPlaying => IsActive;
	
	// Used for .music info to prevent a crash.
	public bool isCaveMusic = false;


	public override void Initialize(IAssetManager assetManager, ICoreClientAPI capi, IMusicEngine musicEngine)
	{
		base.Initialize(assetManager, capi, musicEngine);
		InternalInitialize();
	}

	protected virtual void InternalInitialize()
	{
		TrackSituations = ParseTrackSituations(Situation);
		StartPriorityRnd = NatFloat.createGauss(1f, 0.3f);
	}

	protected static Situation[] ParseTrackSituations(string situationString)
	{
		return situationString
			.Split("|")
			.Select(s =>
				Enum.TryParse<Situation>(s, true, out var e)
					? new Situation?(e)
					: null
			)
			.Where(s => s.HasValue)
			.Cast<Situation>()
			.ToArray();
	}

	public override bool ContinuePlay(float dt, TrackedPlayerProperties props)
	{
		if (!volumeSet && Sound != null)
		{
			Sound.FadeTo(Volume * GlobalVolume, 2, sound => { });
			volumeSet = true;
		}

		return true;
	}

	public override void BeginPlay(TrackedPlayerProperties props)
	{
		base.BeginPlay(props);
		volumeSet = false;
	}

	public override string ToString()
	{
		return $"{nameof(MusicTrack)} ({Title})";
	}
}