using Cairo;
using Vintagestory.API.Client;

namespace VintageSymphony.Update;

public class UpdateProgressOverlay : HudElement
{
	private const string Text = "Updating Vintage Symphony Assets…";
	private const string ProgressBarKey = "UpdateProgressBar";
	
	private const double BarHeight = 10;
	private const double BarWidth = 150;
	private const double BorderThickness = 4;

	private float progress;

	public UpdateProgressOverlay(ICoreClientAPI api)
		: base(api)
	{
		SetupDialog();
	}

	private void SetupDialog()
	{
		var composerBounds = ElementBounds.Fixed(5, 5, 600, 50);
		var progressBarBounds = ElementBounds.Fixed(0, 8, BarWidth, BarHeight);
		var textBounds = ElementBounds.Fixed(BarWidth + 20, 3, 400, 50);

		SingleComposer = capi.Gui
			.CreateCompo("updateProgressOverlay", composerBounds)
			.AddDynamicCustomDraw(progressBarBounds, OnDrawProgressBar, ProgressBarKey)
			.AddStaticText(Text, CairoFont.WhiteSmallText(), textBounds)
			.Compose();
	}

	private void OnDrawProgressBar(Context ctx, ImageSurface surface, ElementBounds bounds)
	{
		// Draw white border
		ctx.SetSourceRGBA(1, 1, 1, 1);
		DrawRectangle(ctx, 0, 0, bounds.OuterWidth, bounds.OuterHeight, false);

		// Calculate filled width based on progress
		double filledWidth = bounds.OuterWidth * progress;

		// Draw white fill
		ctx.SetSourceRGBA(1, 1, 1, 1);
		DrawRectangle(ctx, 0, 0, filledWidth, bounds.OuterHeight, true);
	}

	private void DrawRectangle(Context ctx, double x, double y, double width, double height, bool fill)
	{
		ctx.Rectangle(x, y, width, height);
		if (fill)
		{
			ctx.Fill();
		}
		else
		{
			ctx.LineWidth = BorderThickness;
			ctx.Stroke();
		}
	}

	public void UpdateProgress(float value)
	{
		progress = value;
		SingleComposer?.GetCustomDraw(ProgressBarKey)?.Redraw();
	}

}