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
	private const int DialogWidth = 500;
	private const int RowHeight = 46;
	private const int RowsPerPage = 6;

	private const string AddSourceKey = "vscfg_addsource";
	private const string OkButtonKey = "vscfg_ok";
	private const string PrevKey = "vscfg_prev";
	private const string NextKey = "vscfg_next";

	private readonly Configuration configuration;
	private readonly ConfigurationLoader configurationLoader;
	private readonly MusicSources sources;
	private readonly MusicSourceInstaller installer;
	private readonly AddSourceDialog addSourceDialog;

	private readonly Dictionary<string, bool> draftSourceEnabled = new();

	/// <summary>Status text per source, so a download can report itself where it belongs.</summary>
	private readonly Dictionary<string, string> statusText = new();

	private string busySourceId;
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

	private int PageCount => Math.Max(1, (sources.Sources.Count + RowsPerPage - 1) / RowsPerPage);

	private void SetupDialog()
	{
		var dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

		page = Math.Clamp(page, 0, PageCount - 1);
		var rowsOnPage = Math.Max(1, PageOfSources().Count());
		var listTop = 40;
		var listHeight = rowsOnPage * RowHeight;

		var pagerY = listTop + listHeight + 6;
		var showPager = PageCount > 1;
		var addY = pagerY + (showPager ? 42 : 12);
		var boundsNote = ElementBounds.Fixed(10, addY + 40, DialogWidth - 20, 30);

		const int okWidth = 120;
		var okX = DialogWidth / 2 + GuiStyle.ElementToDialogPadding - okWidth / 2;
		var boundsOk = ElementBounds.Fixed(okX, addY + 76, okWidth, 30);

		// Sizing child: the background fits itself around what the dialog holds
		var sizing = ElementBounds.Fixed(0, listTop, DialogWidth, addY + 76);

		var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
		bgBounds.BothSizing = ElementSizing.FitToChildren;
		bgBounds.WithChildren(sizing);

		SingleComposer?.Dispose();
		SingleComposer = capi.Gui.CreateCompo("vintagesymphony_configuration", dialogBounds)
			.AddShadedDialogBG(bgBounds)
			.AddDialogTitleBar("Vintage Symphony configuration", () => TryClose());

		AddSourceRows();

		// A page at a time rather than a scrolling list: GuiElementClip only scissors the
		// interactive render pass, and switches and static text bake into the composed
		// background, so they draw straight through a clip area.
		if (showPager)
		{
			SingleComposer
				.AddSmallButton("<", () => TurnPage(-1), ElementBounds.Fixed(10, pagerY, 40, 28),
					EnumButtonStyle.Normal, PrevKey)
				.AddStaticText($"Page {page + 1} of {PageCount}", CairoFont.WhiteDetailText(),
					EnumTextOrientation.Center, ElementBounds.Fixed(60, pagerY + 5, 120, 24))
				.AddSmallButton(">", () => TurnPage(1), ElementBounds.Fixed(190, pagerY, 40, 28),
					EnumButtonStyle.Normal, NextKey);
		}

		SingleComposer
			.AddSmallButton("Add a source...", () => { addSourceDialog.TryOpen(); return true; },
				ElementBounds.Fixed(10, addY, 160, 28), EnumButtonStyle.Normal, AddSourceKey)
			.AddStaticText("Changes apply when this closes.", CairoFont.WhiteDetailText(),
				EnumTextOrientation.Left, boundsNote)
			.AddSmallButton("OK", () => TryClose(), boundsOk, EnumButtonStyle.Normal, OkButtonKey)
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
	}

	private void AddSourceRows()
	{
		var row = 0;
		foreach (var source in PageOfSources())
		{
			var y = 40 + row * RowHeight;
			var isBusy = source.Id == busySourceId;

			SingleComposer
				.AddSwitch(state => draftSourceEnabled[source.Id] = state,
					ElementBounds.Fixed(6, y + 6, 10, 30), SwitchKey(source))
				.AddStaticText(source.Name.Length > 0 ? source.Name : source.Id,
					CairoFont.WhiteSmallText(), EnumTextOrientation.Left,
					ElementBounds.Fixed(46, y + 4, 280, 24))
				.AddDynamicText(StatusOf(source), CairoFont.WhiteDetailText(),
					ElementBounds.Fixed(46, y + 24, 280, 20), "vscfg_status_" + source.Id);

			// The game's own music comes with the game, and a source with nowhere to
			// download from is somebody's own folder: neither has anything to press.
			if (!MusicSources.IsBuiltIn(source) && !string.IsNullOrWhiteSpace(source.Url) && !isBusy)
			{
				// What is on disk decides, not what we remember installing: a folder
				// someone filled in by hand is just as installed as one we fetched.
				var label = sources.HasMusicOnDisk(source) ? "Update" : "Download";
				var captured = source;
				SingleComposer.AddSmallButton(label, () => OnDownload(captured),
					ElementBounds.Fixed(340, y + 8, 120, 28), EnumButtonStyle.Normal, ButtonKey(source));
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

	public override string ToggleKeyCombinationCode => "vintage-symphony-config";
}
