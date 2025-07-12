using System.Text;
using Vintagestory.API.Client;
using VintageSymphony.Engine;

namespace VintageSymphony.Debug;

#nullable disable
public class DebugOverlay : HudElement
{
	private static readonly CairoFont Font = CairoFont.WhiteSmallishText().WithOrientation(EnumTextOrientation.Right);
	private const string DebugOverlayTextKey = "vsdbg_text";
	private readonly MusicEngine musicEngine;
	private GuiComposer debugTextComposer;
	private GuiElementRichtext textElement;
	private readonly StringBuilder sb = new(1024);

	public DebugOverlay(ICoreClientAPI api, MusicEngine musicEngine)
		: base(api)
	{
		this.musicEngine = musicEngine;
		SetupDialog();
	}

	private void SetupDialog()
	{
		debugTextComposer = capi.Gui
			.CreateCompo("debugScreenText",
				ElementBounds.Percentual(EnumDialogArea.RightTop, 0.5, 0.7).WithFixedAlignmentOffset(-5.0, 5.0))
			.AddRichtext("", Font, ElementBounds.Fill, DebugOverlayTextKey).OnlyDynamic()
			.Compose();
		textElement = debugTextComposer.GetRichtext(DebugOverlayTextKey);
	}

	public override void OnFinalizeFrame(float dt)
	{
		if (musicEngine.SituationAssessor != null)
		{
			UpdateText(dt);
		}

		debugTextComposer.PostRender(dt);
	}

	public override void OnRenderGUI(float deltaTime)
	{
		debugTextComposer.Render(deltaTime);
	}

	private void UpdateText(float deltaTime)
	{
		var playerPosition = VintageSymphony.ClientApi.World.Player.Entity.Pos.AsBlockPos;
		var climateCondition = VintageSymphony.ClientApi.World.BlockAccessor.GetClimateAt(playerPosition);

		var facts = musicEngine.SituationAssessor.SituationalFacts;

		sb.Clear();
		sb.Append(nameof(facts.DistanceTravelledTotal)).Append(": ")
			.AppendLine(facts.DistanceTravelledTotal.ToString("0.##"));
		sb.Append(nameof(facts.DistanceTravelledDiagonal)).Append(": ")
			.AppendLine(facts.DistanceTravelledDiagonal.ToString("0.##"));
		sb.Append(nameof(facts.DistanceFromHome)).Append(": ").AppendLine(facts.DistanceFromHome.ToString("0.##"));
		sb.Append(nameof(facts.MovementRadius)).Append(": ").AppendLine(facts.MovementRadius.ToString("0.##"));
		sb.Append(nameof(facts.Time)).Append(": ").AppendLine(facts.Time.ToString("0.##"));
		sb.Append(nameof(facts.RelativeHeight)).Append(": ").AppendLine(facts.RelativeHeight.ToString("0.##"));
		sb.Append(nameof(facts.DistanceToSurface)).Append(": ").AppendLine(facts.DistanceToSurface.ToString("0.##"));
		sb.Append(nameof(facts.EnemyDistance)).Append(": ").AppendLine(facts.EnemyDistance.ToString("0.##"));
		sb.Append(nameof(facts.RiftDistance)).Append(": ").AppendLine(facts.RiftDistance.ToString("0.##"));
		sb.Append(nameof(facts.SecondsSinceLastDamage)).Append(": ")
			.AppendLine(facts.SecondsSinceLastDamage.ToString("0.##"));
		sb.Append(nameof(facts.IsHoldingWeapon)).Append(": ").AppendLine(facts.IsHoldingWeapon.ToString());
		sb.Append(nameof(facts.SunLevel)).Append(": ").AppendLine(facts.SunLevel.ToString("0"));
		sb.Append("Temperature: ").AppendLine(climateCondition.Temperature.ToString("0.##"));
		sb.AppendLine("---");
		foreach (var assessment in musicEngine.SituationAssessor.Assessments.OrderByDescending(s => s.WeightedScore))
		{
			if (assessment.WeightedScore == 0)
			{
				sb.Append("<font color=\"0xAAAAAA99\">");
			}
			
			sb.Append(assessment.WeightedScore.ToString("0.00"))
				.Append(" ")
				.Append(assessment.Situation.ToString().PadLeft(15))
				.Append(" (").Append(assessment.Score.ToString("0.00")).Append(")");
			
			if (assessment.WeightedScore == 0)
			{
				sb.Append("</font>");
			}

			sb.AppendLine();
		}

		sb.AppendLine("---");
		var track = musicEngine.CurrentMusicTrack;
		if (track != null)
		{
			sb.Append(nameof(track)).Append(": ")
				.Append(track.Title)
				.Append(" (")
				.Append(track.Situation)
				.AppendLine(")");
		}
		else
		{
			int duration = 0;
			var playback = musicEngine.Playback;

			if (playback.Pause.Active)
			{
				duration = playback.Pause.GetRemainingTimeS();
			}

			sb.Append("Pause (").Append(duration).AppendLine("s)");
		}

		textElement.SetNewText(sb.ToString(), Font);
	}
}