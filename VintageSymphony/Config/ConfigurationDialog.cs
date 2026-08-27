using Vintagestory.API.Client;

namespace VintageSymphony.Config;

#nullable disable
/// <summary>
/// The switches edit a draft, and the draft is applied when the dialog closes -
/// by the button, the title bar, escape, or /music config toggling it shut. They all
/// go through TryClose, so they all mean the same thing.
///
/// That matters because the track pool is built once, from whatever the configuration
/// says at that moment. A switch that wrote straight through could be read halfway
/// through startup, so the same click took effect immediately or not until the next
/// restart depending on how far the game had got. Closing applies the draft and
/// rebuilds the pool explicitly, on first run and from /music config alike.
/// </summary>
public class ConfigurationDialog : GuiDialog
{
	/// <summary>Drives the background's size, and what the OK button is centred in.</summary>
	private const int DialogWidth = 400;

	private const string OkButtonKey = "vscfg_ok";
	private const string LoadGameMusicToggleKey = "vscfg_gameMusicToggle";
	private const string LoadVintageSymphonyMusicToggleKey = "vscfg_modMusicToggle";

	private readonly Configuration configuration;
	private readonly ConfigurationLoader configurationLoader;

	private bool draftLoadGameMusic;
	private bool draftLoadVintageSymphonyMusic;

	public ConfigurationDialog(ICoreClientAPI api, Configuration configuration, ConfigurationLoader configurationLoader)
		: base(api)
	{
		this.configuration = configuration;
		this.configurationLoader = configurationLoader;
		SetupDialog();
	}

	private void SetupDialog()
	{
		// Auto-sized dialog at the center of the screen
		ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

		// Sizing child: the background fits itself around what the dialog holds
		ElementBounds textBounds = ElementBounds.Fixed(0, 40, DialogWidth, 120);

		// Background boundaries. Again, just make it fit it's child elements, then add the text as a child element
		ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
		bgBounds.BothSizing = ElementSizing.FitToChildren;
		bgBounds.WithChildren(textBounds);

		const int width = 350;
		const int height = 30;

		// Lastly, create the dialog
		SingleComposer = capi.Gui.CreateCompo("vintagesymphony_configuration", dialogBounds)
			.AddShadedDialogBG(bgBounds)
			.AddDialogTitleBar("Vintage Symphony configuration", OnTitleBarCloseClicked);

		var boundsChk1 = ElementBounds.Fixed(10, 50, 10, height);
		var boundsChk1Label = ElementBounds.Fixed(50, boundsChk1.fixedY + 5, width, boundsChk1.fixedHeight);
		SingleComposer.AddSwitch(state => draftLoadVintageSymphonyMusic = state, boundsChk1,
				LoadVintageSymphonyMusicToggleKey)
			.AddStaticText("Load Vintage Symphony music", CairoFont.WhiteSmallText(), EnumTextOrientation.Left,
				boundsChk1Label);

		var boundsChk2 = ElementBounds.Fixed(10, 90, 10, height);
		var boundsChk2Label = ElementBounds.Fixed(50, boundsChk2.fixedY + 5, width, boundsChk2.fixedHeight);
		SingleComposer.AddSwitch(state => draftLoadGameMusic = state, boundsChk2, LoadGameMusicToggleKey)
			.AddStaticText("Load Vintage Story music", CairoFont.WhiteSmallText(), EnumTextOrientation.Left,
				boundsChk2Label);

		// Element coordinates start at the frame's left edge, not inside its padding, so
		// the frame is DialogWidth plus a padding either side and its centre sits half a
		// padding to the right of the content box's.
		const int okWidth = 120;
		var okX = DialogWidth / 2 + GuiStyle.ElementToDialogPadding - okWidth / 2;
		var boundsOk = ElementBounds.Fixed(okX, 135, okWidth, height);
		SingleComposer.AddSmallButton("OK", OnOk, boundsOk, EnumButtonStyle.Normal, OkButtonKey);

		SingleComposer.Compose();
	}

	/// <summary>
	/// Start every session of the dialog from what is actually configured.
	/// </summary>
	public override void OnGuiOpened()
	{
		base.OnGuiOpened();

		draftLoadGameMusic = configuration.LoadGameMusic;
		draftLoadVintageSymphonyMusic = configuration.LoadVintageSymphonyMusic;

		SingleComposer.GetSwitch(LoadVintageSymphonyMusicToggleKey).On = draftLoadVintageSymphonyMusic;
		SingleComposer.GetSwitch(LoadGameMusicToggleKey).On = draftLoadGameMusic;
	}

	public override void OnGuiClosed()
	{
		base.OnGuiClosed();
		Apply();
	}

	private void Apply()
	{
		bool poolChanged = draftLoadGameMusic != configuration.LoadGameMusic
		                   || draftLoadVintageSymphonyMusic != configuration.LoadVintageSymphonyMusic;

		if (!poolChanged)
		{
			return;
		}

		configuration.LoadGameMusic = draftLoadGameMusic;
		configuration.LoadVintageSymphonyMusic = draftLoadVintageSymphonyMusic;
		configurationLoader.SaveConfiguration(configuration);

		VintageSymphony.MusicEngine?.ReloadTracks();
	}

	private bool OnOk()
	{
		TryClose();
		return true;
	}

	private void OnTitleBarCloseClicked()
	{
		TryClose();
	}

	public override string ToggleKeyCombinationCode => "vintage-symphony-config";
}
