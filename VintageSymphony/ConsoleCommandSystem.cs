using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VintageSymphony;

// ReSharper disable once UnusedType.Global
public class ConsoleCommandSystem : ModSystem
{
	private ICoreClientAPI api;
	public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

	public override void StartClientSide(ICoreClientAPI api)
	{
		this.api = api;
		base.StartClientSide(api);
		api.ChatCommands.Create("music")
			.WithDescription("Music related commands for Vintage Symphony.");
		api.ChatCommands.Get("music")
			.BeginSubCommand("next")
			.WithDescription("Play the next track")
			.HandleWith(NextTrack)
			.EndSubCommand();
			// --
		api.ChatCommands.Get("music").BeginSubCommand("info")
			.WithDescription("Displays the currently playing track")
			.HandleWith(OutputCurrentTrack)
			.EndSubCommand();
			// --
		api.ChatCommands.Get("music").BeginSubCommand("stop")
			.HandleWith(StopTrack)
			.EndSubCommand();
			// --
		api.ChatCommands.Get("music").BeginSubCommand("debug")
			.WithDescription("Toggle debug overlay")
			.HandleWith(ToggleDebugOverlay)
			.EndSubCommand();
			// --
		api.ChatCommands.Get("music").BeginSubCommand("config")
			.WithDescription("Toggle Vintage Symphony configuration")
			.HandleWith(ToggleConfigurationDialog)
			.EndSubCommand();
	}

	private TextCommandResult ToggleConfigurationDialog(TextCommandCallingArgs args)
	{
		var configurationDialog = VintageSymphony.ConfigurationDialog;
		if (configurationDialog.IsOpened())
		{
			configurationDialog.TryClose();
		}
		else
		{
			configurationDialog.TryOpen();
		}

		return TextCommandResult.Success();
	}

	private TextCommandResult ToggleDebugOverlay(TextCommandCallingArgs args)
	{
		var debugOverlay = VintageSymphony.DebugOverlay;
		if (debugOverlay.IsOpened())
		{
			debugOverlay.TryClose();
		}
		else
		{
			debugOverlay.TryOpen();
		}

		return TextCommandResult.Success();
	}

	private TextCommandResult StopTrack(TextCommandCallingArgs args)
	{
		VintageSymphony.MusicEngine?.StopTrackAndPause();
		return TextCommandResult.Success();
	}

	private TextCommandResult OutputCurrentTrack(TextCommandCallingArgs args)
	{
		var track = VintageSymphony.MusicEngine?.CurrentMusicTrack;
		if (track == null)
		{
			return TextCommandResult.Success("&gt; no track playing");
		}
		if (track.isCaveMusic)
		{
			return TextCommandResult.Success($"&gt; {track.Title}, Cave Music");
		}
		string track_position = "";
		try 
		{
			track_position = track.PositionString;
		}
		catch (Exception e)
		{
			api.Logger.Error($"Tried to get track position for track {track.Title}, but failed! Error: {e}");
			return TextCommandResult.Success($"&gt; {track.Title} [Track Position Not Available]");
		}
		return TextCommandResult.Success($"&gt; {track.Title} [{track.PositionString}]");
	}

	private TextCommandResult NextTrack(TextCommandCallingArgs args)
	{
		VintageSymphony.MusicEngine?.NextTrack();
		return TextCommandResult.Success();
	}
}