using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using VintageSymphony.Music;

namespace VintageSymphony.Config;

#nullable disable
/// <summary>
/// Lists where music can come from, lets each source be switched on or off, and lets one
/// be downloaded - on a button, never on its own.
///
/// The switches edit a draft applied when the dialog closes, by the button, the title
/// bar, escape, or /music config toggling it shut: they all go through TryClose, so they
/// all mean the same thing. Downloading is not part of that draft; it is an action, and
/// it happens when it is pressed.
/// </summary>
public class ConfigurationDialog : GuiDialog
{
	private const int DialogWidth = 560;

	/// <summary>
	/// Even space at every edge. Children are positioned from the frame's left edge while
	/// the frame is the content box plus a padding on each side, so the room to the right
	/// and below is that padding twice over unless it is subtracted back out by hand.
	/// </summary>
	private const int Margin = 12;
	private const int RowHeight = 46;
	private const int RowsPerPage = 6;

	private const string AddSourceKey = "vscfg_addsource";
	private const string OkButtonKey = "vscfg_ok";
	private const string PrevKey = "vscfg_prev";
	private const string NextKey = "vscfg_next";
	public const string PlaylistsSwitchKey = "vscfg_playlists";

	private readonly Configuration configuration;
	private readonly ConfigurationLoader configurationLoader;
	private readonly MusicSources sources;
	private readonly MusicSourceInstaller installer;
	private readonly AddSourceDialog addSourceDialog;

	private readonly Dictionary<string, bool> draftSourceEnabled = new();

	/// <summary>Drafted like the source switches: read when the dialog closes.</summary>
	private bool draftHonourPlaylists;

	/// <summary>Status text per source, so a download can report itself where it belongs.</summary>
	private readonly Dictionary<string, string> statusText = new();

	private string busySourceId;

	/// <summary>Removing deletes a folder, so it takes two presses rather than one.</summary>
	private string pendingRemoveId;
	private int page;

	public ConfigurationDialog(ICoreClientAPI api, Configuration configuration,
		ConfigurationLoader configurationLoader, MusicSources sources)
		: base(api)
	{
		this.configuration = configuration;
		this.configurationLoader = configurationLoader;
		this.sources = sources;
		installer = new MusicSourceInstaller(sources, api.Logger);
		addSourceDialog = new AddSourceDialog(api, sources, OnSourceAdded);
		SetupDialog();
	}

	private static string SwitchKey(MusicSource source) => "vscfg_src_" + source.Id;
	private static string ButtonKey(MusicSource source) => "vscfg_get_" + source.Id;
	private static string RemoveKey(MusicSource source) => "vscfg_rm_" + source.Id;

	private int PageCount => Math.Max(1, (sources.Sources.Count + RowsPerPage - 1) / RowsPerPage);

	private void SetupDialog()
	{
		var dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

		page = Math.Clamp(page, 0, PageCount - 1);

		// Sized for a full page rather than for this page, so the dialog does not jump
		// about as pages are turned - but still shrinks to fit when there are only a
		// couple of sources.
		var rowsShown = Math.Max(1, Math.Min(sources.Sources.Count, RowsPerPage));
		var settingsY = 40;
		var listTop = settingsY + RowHeight + 6;
		var listHeight = rowsShown * RowHeight;

		var pagerY = listTop + listHeight + 6;
		var showPager = PageCount > 1;
		var addY = pagerY + (showPager ? 42 : 12);
		var padding = (int)GuiStyle.ElementToDialogPadding;
		var usableWidth = DialogWidth + 2 * padding;

		const int okWidth = 120;
		var boundsOk = ElementBounds.Fixed(usableWidth - Margin - okWidth, addY, okWidth, 30);

		// Sizing child: the background fits itself around what the dialog holds
		// The sizing child wants a height, not the coordinate its bottom sits at. It also
		// stops short of the buttons, because the frame will add a padding below them
		// whether or not anything asked for it.
		var sizing = ElementBounds.Fixed(0, settingsY, DialogWidth,
			addY + 30 + Margin - settingsY - 2 * padding);

		var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
		bgBounds.BothSizing = ElementSizing.FitToChildren;
		bgBounds.WithChildren(sizing);

		SingleComposer?.Dispose();
		SingleComposer = capi.Gui.CreateCompo("vintagesymphonyforked_configuration", dialogBounds)
			.AddShadedDialogBG(bgBounds)
			.AddDialogTitleBar("Vintage Symphony configuration", () => TryClose());

		AddSettingsRows(settingsY);
		AddSourceRows(listTop);

		// A page at a time rather than a scrolling list: GuiElementClip only scissors the
		// interactive render pass, and switches and static text bake into the composed
		// background, so they draw straight through a clip area.
		if (showPager)
		{
			SingleComposer
				.AddSmallButton("<", () => TurnPage(-1), ElementBounds.Fixed(Margin, pagerY, 40, 28),
					EnumButtonStyle.Normal, PrevKey)
				.AddStaticText($"Page {page + 1} of {PageCount}", CairoFont.WhiteDetailText(),
					EnumTextOrientation.Center, ElementBounds.Fixed(60, pagerY + 5, 120, 24))
				.AddSmallButton(">", () => TurnPage(1), ElementBounds.Fixed(190, pagerY, 40, 28),
					EnumButtonStyle.Normal, NextKey);
		}

		SingleComposer
			.AddSmallButton("Add a source...", () => { addSourceDialog.TryOpen(); return true; },
				ElementBounds.Fixed(Margin, addY, 160, 30), EnumButtonStyle.Normal, AddSourceKey)
			.AddSmallButton("Done", () => TryClose(), boundsOk, EnumButtonStyle.Normal, OkButtonKey)
			.Compose();

		RestoreSwitchStates();
	}

	private IEnumerable<MusicSource> PageOfSources() =>
		sources.Sources.Skip(page * RowsPerPage).Take(RowsPerPage);

	private bool TurnPage(int direction)
	{
		page = Math.Clamp(page + direction, 0, PageCount - 1);
		SetupDialog();
		return true;
	}

	/// <summary>
	/// Rebuilding the composer makes new switches, so the draft has to be put back into
	/// them - otherwise turning a page would quietly reset what the player just set.
	/// </summary>
	private void RestoreSwitchStates()
	{
		foreach (var source in PageOfSources())
		{
			if (draftSourceEnabled.TryGetValue(source.Id, out var enabled))
			{
				SingleComposer.GetSwitch(SwitchKey(source)).On = enabled;
			}
		}

		SingleComposer.GetSwitch(PlaylistsSwitchKey).On = draftHonourPlaylists;
	}

	/// <summary>
	/// Settings that are not a source, above the sources. One so far: whether the game's
	/// own music keeps to the survival/creative split the game gives it. Same shape as a
	/// source row.
	/// </summary>
	private void AddSettingsRows(int y)
	{
		var padding = (int)GuiStyle.ElementToDialogPadding;
		var textWidth = DialogWidth + 2 * padding - Margin - 46;

		SingleComposer
			.AddSwitch(state => draftHonourPlaylists = state,
				ElementBounds.Fixed(Margin, y + 6, 10, 30), PlaylistsSwitchKey)
			.AddStaticText("Respect the survival/creative music split",
				CairoFont.WhiteSmallText(), EnumTextOrientation.Left,
				ElementBounds.Fixed(46, y + 10, textWidth, 24));
	}

	private void AddSourceRows(int listTop)
	{
		var padding = (int)GuiStyle.ElementToDialogPadding;
		var usableWidth = DialogWidth + 2 * padding;
		// Wide enough for "Remove" without the button growing to fit, and for "Sure?"
		// without the right edge shifting when it arms.
		const int removeWidth = 80;
		const int getWidth = 120;
		var removeX = usableWidth - Margin - removeWidth;
		var getX = removeX - 10 - getWidth;

		var row = 0;
		foreach (var source in PageOfSources())
		{
			var y = listTop + row * RowHeight;
			var isBusy = source.Id == busySourceId;

			SingleComposer
				.AddSwitch(state => draftSourceEnabled[source.Id] = state,
					ElementBounds.Fixed(Margin, y + 6, 10, 30), SwitchKey(source))
				.AddStaticText(source.Name.Length > 0 ? source.Name : source.Id,
					CairoFont.WhiteSmallText(), EnumTextOrientation.Left,
					ElementBounds.Fixed(46, y + 4, getX - 56, 24))
				.AddDynamicText(StatusOf(source), CairoFont.WhiteDetailText(),
					ElementBounds.Fixed(46, y + 24, getX - 56, 20), "vscfg_status_" + source.Id);

			// The game's own music comes with the game, and a source with nowhere to
			// download from is somebody's own folder: neither has anything to press.
			if (!MusicSources.IsBuiltIn(source) && !string.IsNullOrWhiteSpace(source.Url) && !isBusy)
			{
				// What is on disk decides, not what we remember installing: a folder
				// someone filled in by hand is just as installed as one we fetched.
				var label = sources.HasMusicOnDisk(source) ? "Update" : "Download";
				var captured = source;
				SingleComposer.AddSmallButton(label, () => OnDownload(captured),
					ElementBounds.Fixed(getX, y + 8, getWidth, 28), EnumButtonStyle.Normal, ButtonKey(source));
			}

			// The game's own music cannot be removed - it would only come back.
			if (!MusicSources.IsBuiltIn(source) && !isBusy)
			{
				var captured = source;
				var removing = source.Id == pendingRemoveId;
				SingleComposer.AddSmallButton(removing ? "Sure?" : "Remove", () => OnRemove(captured),
					ElementBounds.Fixed(removeX, y + 8, removeWidth, 28), EnumButtonStyle.Normal, RemoveKey(source));
			}

			row++;
		}
	}

	/// <summary>A source was added in the other dialog; show it where it landed.</summary>
	private void OnSourceAdded()
	{
		var added = sources.Sources.LastOrDefault();
		if (added != null)
		{
			draftSourceEnabled[added.Id] = added.Enabled;
		}

		page = (sources.Sources.Count - 1) / RowsPerPage;
		SetupDialog();
	}

	private string StatusOf(MusicSource source)
	{
		if (statusText.TryGetValue(source.Id, out var status))
		{
			return status;
		}

		if (MusicSources.IsBuiltIn(source))
		{
			return "comes with the game";
		}

		if (!sources.HasMusicOnDisk(source))
		{
			return source.Url == null ? "no music in its folder" : "not installed";
		}

		return source.Installed == null ? "installed" : "installed " + source.Installed;
	}

	/// <summary>
	/// First press arms it, second one does it. Removing takes the source's music with it,
	/// which is not something to do on a stray click.
	/// </summary>
	private bool OnRemove(MusicSource source)
	{
		if (pendingRemoveId != source.Id)
		{
			pendingRemoveId = source.Id;
			SetupDialog();
			return true;
		}

		pendingRemoveId = null;
		draftSourceEnabled.Remove(source.Id);
		statusText.Remove(source.Id);
		sources.Remove(source);

		VintageSymphony.MusicEngine?.ReloadTracks();
		SetupDialog();
		return true;
	}

	private bool OnDownload(MusicSource source)
	{
		if (busySourceId != null)
		{
			return true;
		}

		busySourceId = source.Id;
		SetStatus(source, "checking…");

		// Fire and forget on purpose: the dialog stays usable, and every step reports
		// itself into the row it belongs to.
		_ = DownloadAsync(source);
		return true;
	}

	private async Task DownloadAsync(MusicSource source)
	{
		try
		{
			var release = await installer.CheckAsync(source);
			if (release == null)
			{
				SetStatus(source, "nothing available - see the log");
				return;
			}

			if (installer.IsUpToDate(source, release))
			{
				SetStatus(source, $"already up to date ({source.Installed})");
				return;
			}

			var size = release.SizeBytes > 0 ? $" ({release.SizeBytes / 1024 / 1024} MB)" : "";
			SetStatus(source, $"downloading{size}…");

			await installer.InstallAsync(source, release,
				fraction => SetStatus(source, $"downloading{size} {fraction * 100:0}%"),
				CancellationToken.None);

			// Put the new folder in front of the asset manager and rebuild the pool, so it
			// plays now rather than after a restart.
			await OnMainThread(() =>
			{
				sources.RegisterOriginNow(capi, source);
				VintageSymphony.MusicEngine?.ReloadTracks();
			});

			SetStatus(source, "installed");
		}
		catch (Exception e)
		{
			capi.Logger.Error("Installing '{0}' failed: {1}", source.Id, e.Message);
			SetStatus(source, "failed - see the log");
		}
		finally
		{
			busySourceId = null;
		}
	}

	/// <summary>Hop back to the thread the game expects its own state to be touched on.</summary>
	private Task OnMainThread(Action action)
	{
		var done = new TaskCompletionSource();
		capi.Event.EnqueueMainThreadTask(() =>
		{
			try { action(); done.SetResult(); }
			catch (Exception e) { done.SetException(e); }
		}, "vscfg-apply");
		return done.Task;
	}

	private void SetStatus(MusicSource source, string text)
	{
		statusText[source.Id] = text;

		// The download runs off the main thread; the GUI does not.
		capi.Event.EnqueueMainThreadTask(() =>
		{
			if (IsOpened())
			{
				SingleComposer?.GetDynamicText("vscfg_status_" + source.Id)?.SetNewText(text);
			}
		}, "vscfg-status");
	}

	public override void OnGuiOpened()
	{
		base.OnGuiOpened();

		pendingRemoveId = null;
		draftHonourPlaylists = configuration.HonourGamePlaylists;
		draftSourceEnabled.Clear();
		foreach (var source in sources.Sources)
		{
			draftSourceEnabled[source.Id] = source.Enabled;
		}

		// Sources can appear between openings - someone may have edited sources.json.
		page = 0;
		SetupDialog();
	}

	public override void OnGuiClosed()
	{
		base.OnGuiClosed();
		Apply();
	}

	private void Apply()
	{
		// Read at the next selection, so the pool need not be rebuilt for it.
		if (draftHonourPlaylists != configuration.HonourGamePlaylists)
		{
			configuration.HonourGamePlaylists = draftHonourPlaylists;
			configurationLoader.SaveConfiguration(configuration);
		}

		var changed = false;

		foreach (var source in sources.Sources)
		{
			if (draftSourceEnabled.TryGetValue(source.Id, out var enabled) && enabled != source.Enabled)
			{
				source.Enabled = enabled;
				changed = true;
			}
		}

		if (!changed)
		{
			return;
		}

		sources.Save();

		VintageSymphony.MusicEngine?.ReloadTracks();
	}

	public override string ToggleKeyCombinationCode => "vintage-symphony-forked-config";
}
