using System.Text;
using Vintagestory.API.Client;
using VintageSymphony.Engine;

namespace VintageSymphony.Debug;

#nullable disable
public class DebugOverlay : HudElement
{
	private static readonly CairoFont Font = CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Right);
	private const string DebugOverlayTextKey = "vsdbg_text";
	private readonly MusicEngine musicEngine;
	private GuiComposer debugTextComposer;
	private GuiElementRichtext textElement;
	private readonly StringBuilder sb = new(1024);
	private long updateEventId;
	private const int UpdateIntervalMs = 200;

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

	public override void OnGuiOpened()
	{
		updateEventId = capi.World.RegisterGameTickListener(TriggerUpdateTask, UpdateIntervalMs);
	}

	public override void OnGuiClosed()
	{
		if (updateEventId != 0)
		{
			capi.World.UnregisterGameTickListener(updateEventId);
			updateEventId = 0;
		}	
	}

	private void TriggerUpdateTask(float deltaTime)
	{
		if (musicEngine.SituationAssessor != null)
		{
			Task.Run(UpdateText);
		}
	}

	public override void OnFinalizeFrame(float dt)
	{
		debugTextComposer.PostRender(dt);
	}

	public override void OnRenderGUI(float deltaTime)
	{
		debugTextComposer.Render(deltaTime);
	}

	private void UpdateText()
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
		sb.Append(nameof(facts.VisibleEnemyDistance)).Append(": ").AppendLine(facts.VisibleEnemyDistance.ToString("0.##"));
		sb.Append(nameof(facts.RiftDistance)).Append(": ").AppendLine(facts.RiftDistance.ToString("0.##"));
		sb.Append(nameof(facts.PlayingResonatorDistance)).Append(": ").AppendLine(facts.PlayingResonatorDistance.ToString("0.##"));
		sb.Append(nameof(facts.SecondsSinceLastDamage)).Append(": ")
			.AppendLine(facts.SecondsSinceLastDamage.ToString("0.##"));
		sb.Append(nameof(facts.IsHoldingWeapon)).Append(": ").AppendLine(facts.IsHoldingWeapon.ToString());
		sb.Append(nameof(facts.SecondsSinceLastAttack)).Append(": ").AppendLine(facts.SecondsSinceLastAttack.ToString());
		sb.Append(nameof(facts.SunLevel)).Append(": ").AppendLine(facts.SunLevel.ToString("0"));
		sb.Append(nameof(facts.DayLight)).Append(": ").AppendLine(facts.DayLight.ToString("0.##"));
		sb.Append("Temperature: ").AppendLine(climateCondition.Temperature.ToString("0.##"));
		sb.AppendLine("---");
		foreach (var assessment in musicEngine.SituationAssessor.Assessments.OrderByDescending(s => s.WeightedScore))
		{
			bool fontTag = false;
			if (assessment.WeightedScore < 0.01f)
			{
				sb.Append("<font color=\"0xAAAAAA99\">");
				fontTag = true;
			}

			if (musicEngine.Playback.CurrentPlaylist?.Situation == assessment.Situation)
			{
				sb.Append("<font color=\"0x38e314\">‣ ");
				fontTag = true;
			}
			
			sb.Append(assessment.Situation.ToString())
				.Append(' ')
				.Append(assessment.WeightedScore.ToString("0.00"))
				.Append(" (").Append(assessment.Score.ToString("0.00")).Append(')');
			
			if (fontTag)
			{
				sb.Append("</font>");
			}

			sb.AppendLine();
		}

		sb.AppendLine("---");
		var track = musicEngine.CurrentMusicTrack;
		if (track != null)
		{
			sb.Append("<font color=\"0x38e314\">‣ ")
				.Append(track.Title)
				.AppendLine("</font>");

			sb.Append("<font size=\"14\" color=\"0xAAAAAA99\">[")
				.Append(track.Situation)
				.AppendLine("]</font>");
		}
		else
		{
			int duration = 0;
			var playback = musicEngine.Playback;

			if (playback.Pause.Active)
			{
				duration = playback.Pause.GetRemainingTimeS();
			}
			sb.Append("<font color=\"0xAAAAAA99\">")
				.Append("• Pause (").Append(duration).AppendLine("s)</font>");
		}

		var text = sb.ToString();
		capi.Event.EnqueueMainThreadTask(() => textElement.SetNewText(text, Font), "DebugOverlayTextUpdate");
	}
}