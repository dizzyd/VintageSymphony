using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VintageSymphony;

// ReSharper disable once UnusedType.Global
public class ConsoleCommandSystem : ModSystem
{
	public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

	public override void StartClientSide(ICoreClientAPI api)
	{
		base.StartClientSide(api);
		api.ChatCommands.Create("music")
			.WithDescription("Music related commands for Vintage Symphony.");
		
		api.ChatCommands.Get("music")
			.BeginSubCommand("next")
			.WithDescription("Play the next track")
			.HandleWith(NextTrack)
			.EndSubCommand();
		
		api.ChatCommands.Get("music").BeginSubCommand("info")
			.WithDescription("Displays the currently playing track")
			.HandleWith(OutputCurrentTrack)
			.EndSubCommand();
		
		api.ChatCommands.Get("music").BeginSubCommand("stop")
			.WithDescription("Stop the music and hold silence for a while")
			.HandleWith(StopTrack)
			.EndSubCommand();
		
		api.ChatCommands.Get("music").BeginSubCommand("debug")
			.WithDescription("Toggle debug overlay")
			.HandleWith(ToggleDebugOverlay)
			.EndSubCommand();
		
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
		var playback = VintageSymphony.MusicEngine?.Playback;
		if (playback == null)
		{
			return TextCommandResult.Success();
		}

		// Say how long the silence lasts. Music coming back on its own is the point of a
		// pause rather than a mute, but it is a surprise if nobody said so.
		var seconds = playback.Stop();
		return TextCommandResult.Success(
			$"&gt; stopped. Music returns in about {seconds / 60}m {seconds % 60}s, or type .music next");
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

		return TextCommandResult.Success($"&gt; {track.Title} [{track.PositionString}]");
	}

	private TextCommandResult NextTrack(TextCommandCallingArgs args)
	{
		VintageSymphony.MusicEngine?.NextTrack();
		return TextCommandResult.Success();
	}
}