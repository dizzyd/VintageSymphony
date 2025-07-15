using Vintagestory.API.Client;

namespace VintageSymphony.Update;

#nullable disable
public class UpdateInstalledOverlay : HudElement
{
	private GuiComposer notificationComposer;
	private GuiElementDynamicText textElement;
	private string text = "Vintage Symphony assets updated. Reload your save-game to apply changes.";

	public UpdateInstalledOverlay(ICoreClientAPI api)
		: base(api)
	{
		SetupDialog();
	}

	private void SetupDialog()
	{
		notificationComposer = capi.Gui
			.CreateCompo("updateCompletedNotification",
				ElementBounds.Percentual(EnumDialogArea.LeftTop, 1, 0.3).WithFixedAlignmentOffset(5.0, 5.0))
			.AddDynamicText(text, CairoFont.WhiteSmallText(),
				ElementBounds.Fill, "updateCompletedText").OnlyDynamic()
			.Compose();
		textElement = notificationComposer.GetDynamicText("updateCompletedText");
	}

	public override void OnFinalizeFrame(float dt)
	{
		notificationComposer.PostRender(dt);
	}

	public override void OnRenderGUI(float deltaTime)
	{
		notificationComposer.Render(deltaTime);
	}
}