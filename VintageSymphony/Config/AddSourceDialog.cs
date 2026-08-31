using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using VintageSymphony.Music;

namespace VintageSymphony.Config;

#nullable disable
/// <summary>
/// Asks for a name and an address, and nothing else. Two boxes and a button crowded the
/// configuration dialog for something done once in a while, so they live here and it
/// opens on request.
/// </summary>
public class AddSourceDialog : GuiDialog
{
	private const int Width = 420;

	/// <summary>
	/// Even space at every edge. The frame is the content box plus a padding on each side
	/// while children are positioned from its left edge, so the room to the right and
	/// below is that padding twice over unless it is subtracted back out by hand.
	/// </summary>
	private const int Margin = 12;

	private const string AddKey = "vsadd_add";
	private const string CancelKey = "vsadd_cancel";
	private const string NameKey = "vsadd_name";
	private const string UrlKey = "vsadd_url";
	private const string StatusKey = "vsadd_status";

	private readonly MusicSources sources;
	private readonly Action onAdded;

	private string draftName = "";
	private string draftUrl = "";

	public AddSourceDialog(ICoreClientAPI api, MusicSources sources, Action onAdded)
		: base(api)
	{
		this.sources = sources;
		this.onAdded = onAdded;
		SetupDialog();
	}

	private void SetupDialog()
	{
		var dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

		var padding = (int)GuiStyle.ElementToDialogPadding;
		var usableWidth = Width + 2 * padding;
		var contentWidth = usableWidth - 2 * Margin;

		const int top = 40;
		const int rowHeight = 28;
		var nameLabelY = top + 8;
		var nameY = nameLabelY + 22;
		var urlLabelY = nameY + rowHeight + 10;
		var urlY = urlLabelY + 22;
		var statusY = urlY + rowHeight + 6;
		var buttonsY = statusY + 26;

		const int buttonWidth = 110;

		// The sizing child stops short of the buttons: the frame adds a padding below them
		// whether or not anything asked for it.
		var sizing = ElementBounds.Fixed(0, top, Width,
			buttonsY + rowHeight + Margin - top - 2 * padding);

		var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
		bgBounds.BothSizing = ElementSizing.FitToChildren;
		bgBounds.WithChildren(sizing);

		SingleComposer?.Dispose();
		SingleComposer = capi.Gui.CreateCompo("vintagesymphony_addsource", dialogBounds)
			.AddShadedDialogBG(bgBounds)
			.AddDialogTitleBar("Add a music source", () => TryClose())
			.AddStaticText("Name", CairoFont.WhiteDetailText(), EnumTextOrientation.Left,
				ElementBounds.Fixed(Margin, nameLabelY, 200, 20))
			.AddTextInput(ElementBounds.Fixed(Margin, nameY, 220, rowHeight),
				text => draftName = text, CairoFont.WhiteDetailText(), NameKey)
			.AddStaticText("Address", CairoFont.WhiteDetailText(), EnumTextOrientation.Left,
				ElementBounds.Fixed(Margin, urlLabelY, 200, 20))
			.AddTextInput(ElementBounds.Fixed(Margin, urlY, contentWidth, rowHeight),
				text => draftUrl = text, CairoFont.WhiteDetailText(), UrlKey)
			.AddDynamicText("", CairoFont.WhiteDetailText(),
				ElementBounds.Fixed(Margin, statusY, contentWidth, 20), StatusKey)
			.AddSmallButton("Cancel", OnCancel,
				ElementBounds.Fixed(Margin, buttonsY, buttonWidth, rowHeight),
				EnumButtonStyle.Normal, CancelKey)
			.AddSmallButton("Add", OnAdd,
				ElementBounds.Fixed(usableWidth - Margin - buttonWidth, buttonsY, buttonWidth, rowHeight),
				EnumButtonStyle.Normal, AddKey)
			.Compose();

		SingleComposer.GetTextInput(NameKey).SetPlaceHolderText("bobs-tunes");
		SingleComposer.GetTextInput(UrlKey).SetPlaceHolderText("https://...");
	}

	public override void OnGuiOpened()
	{
		base.OnGuiOpened();
		draftName = "";
		draftUrl = "";
		SingleComposer.GetTextInput(NameKey).SetValue("");
		SingleComposer.GetTextInput(UrlKey).SetValue("");
		SingleComposer.GetDynamicText(StatusKey).SetNewText("");
	}

	private bool OnCancel()
	{
		TryClose();
		return true;
	}

	private bool OnAdd()
	{
		var id = draftName.Trim().ToLowerInvariant();
		var url = draftUrl.Trim();

		// The name becomes a directory and an asset domain, so it has to be plain - which
		// also means a name like ../oops never gets as far as the file system.
		if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,31}$"))
		{
			return Status("A name may only use lowercase letters, digits and dashes.");
		}

		if (!url.StartsWith("http://") && !url.StartsWith("https://"))
		{
			return Status("That does not look like an http address.");
		}

		if (sources.Sources.Any(s => s.Id == id))
		{
			return Status($"There is already a source called '{id}'.");
		}

		sources.Sources.Add(new MusicSource { Id = id, Name = id, Enabled = true, Url = url });
		sources.Save();

		TryClose();
		onAdded();
		return true;
	}

	private bool Status(string text)
	{
		SingleComposer.GetDynamicText(StatusKey)?.SetNewText(text);
		return true;
	}

	public override string ToggleKeyCombinationCode => null;
}
