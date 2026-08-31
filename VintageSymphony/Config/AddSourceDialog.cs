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
		var sizing = ElementBounds.Fixed(0, 40, Width, 190);

		var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
		bgBounds.BothSizing = ElementSizing.FitToChildren;
		bgBounds.WithChildren(sizing);

		SingleComposer?.Dispose();
		SingleComposer = capi.Gui.CreateCompo("vintagesymphony_addsource", dialogBounds)
			.AddShadedDialogBG(bgBounds)
			.AddDialogTitleBar("Add a music source", () => TryClose())
			.AddStaticText("Name", CairoFont.WhiteDetailText(), EnumTextOrientation.Left,
				ElementBounds.Fixed(10, 48, 100, 20))
			.AddTextInput(ElementBounds.Fixed(10, 70, 200, 28), text => draftName = text,
				CairoFont.WhiteDetailText(), NameKey)
			.AddStaticText("Address", CairoFont.WhiteDetailText(), EnumTextOrientation.Left,
				ElementBounds.Fixed(10, 106, 100, 20))
			.AddTextInput(ElementBounds.Fixed(10, 128, Width - 20, 28), text => draftUrl = text,
				CairoFont.WhiteDetailText(), UrlKey)
			.AddDynamicText("", CairoFont.WhiteDetailText(), ElementBounds.Fixed(10, 162, Width - 20, 20), StatusKey)
			.AddSmallButton("Cancel", OnCancel, ElementBounds.Fixed(10, 190, 110, 28))
			.AddSmallButton("Add", OnAdd, ElementBounds.Fixed(Width - 120, 190, 110, 28))
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
