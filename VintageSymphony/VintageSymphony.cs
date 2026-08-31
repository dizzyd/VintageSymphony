using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using VintageSymphony.Config;
using VintageSymphony.Debug;
using VintageSymphony.Engine;
using VintageSymphony.Music;
using VintageSymphony.Storage;
using VintageSymphony.Update;

// ReSharper disable NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace VintageSymphony;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
public class VintageSymphony : BaseModSystem
{
	private const string PersistentStoragePrefix = "mm_";

	public static VintageSymphony Instance { get; private set; }
	public static ICoreClientAPI ClientApi { get; private set; }
	public static ClientMain ClientMain { get; private set; }
	public static MusicEngine MusicEngine { get; private set; }
	public static DebugOverlay DebugOverlay { get; private set; }
	public static Configuration Configuration { get; private set; }
	public static ConfigurationDialog ConfigurationDialog { get; private set; }

	private string ModId => Mod.Info.ModID;
	public AttributeStorage AttributeStorage { get; private set; }
	private string modDataPath;
	private ConfigurationLoader configurationLoader;


	public override double ExecuteOrder() => 1.5;
	public override bool ShouldLoad(EnumAppSide side) => true;

	public static MusicSources MusicSources { get; private set; }

	/// <summary>
	/// Let music be folders of files rather than mods. Each source is registered as an
	/// asset origin under its own domain, which puts it in front of the asset manager
	/// exactly like a mod's assets would - so the game resolves the .ogg files normally,
	/// two sources cannot collide, and nothing has to be installed into the player's Mods
	/// directory or declare a dependency on anything.
	///
	/// StartPre runs before any mod receives Start(), which is early enough for the assets
	/// to be read.
	/// </summary>
	public override void StartPre(ICoreAPI api)
	{
		base.StartPre(api);

		if (api.Side != EnumAppSide.Client)
		{
			return;
		}

		MusicSources = new MusicSources(Path.Combine(api.DataBasePath, "ModData", Mod.Info.ModID), api.Logger);
		MusicSources.Load();

		foreach (var source in MusicSources.Installed)
		{
			MusicSources.RegisterOrigin(api, source);
			api.Logger.Notification("Music source '{0}' registered from {1}",
				source.Id, MusicSources.DirectoryOf(source));
		}
	}

	public override void StartClientSide(ICoreClientAPI api)
	{
		base.StartClientSide(api);

		Instance = this;
		ClientApi = api;
		ClientMain = (ClientMain)api.World;
		MusicEngine = ClientApi.ModLoader.GetModSystem<MusicEngine>();

		configurationLoader = new ConfigurationLoader(ClientApi, ModId);
		try
		{
			Configuration = configurationLoader.LoadConfiguration();
		}
		catch (ConfigurationException)
		{
			ClientApi.Logger.Fatal($"Failed to load {ModId} configuration. Check config file for errors.");
			throw;
		}

		modDataPath = Path.Combine(api.DataBasePath, "ModData", ModId, api.World.SavegameIdentifier);
		if (!Directory.Exists(modDataPath))
		{
			Directory.CreateDirectory(modDataPath);
		}

		var attributeStorageFile = Path.Combine(modDataPath, "attributes.json");
		AttributeStorage = new AttributeStorage(PersistentStoragePrefix,
			new LocalAttributePersistenceLayer(attributeStorageFile));


		if (!Harmony.HasAnyPatches(ModId))
		{
			var harmony = new Harmony(ModId);
			harmony.PatchAll();
		}
	}

	protected override void OnGameStarted()
	{
		ConfigurationDialog = new ConfigurationDialog(ClientApi, Configuration, configurationLoader, MusicSources);
		if (!Configuration.InitialConfigurationShown)
		{
			Configuration.InitialConfigurationShown = true;
			configurationLoader.SaveConfiguration(Configuration);
			ConfigurationDialog.TryOpen(true);
		}
		
		DebugOverlay = new DebugOverlay(ClientApi, MusicEngine);
#if DEBUG
		DebugOverlay.TryOpen();
#endif
	}

	public override void Dispose()
	{
		AttributeStorage?.Dispose();
		DebugOverlay?.Dispose();
		base.Dispose();
	}
}