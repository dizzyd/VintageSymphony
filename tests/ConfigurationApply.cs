using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using VS = VintageSymphony.VintageSymphony;

namespace VintageSymphony.Tests
{
    /// <summary>
    /// The configuration dialog decides what the track pool contains, and the pool is
    /// built once. This drives the real dialog and the real element, dispatching the
    /// mouse event GuiElementSwitch handles, because the point of the draft is *when*
    /// the value is read.
    ///
    /// It hands the event to the element rather than to the window: a click through the
    /// windowing layer carries the platform's own cursor position, which a headless box
    /// does not let a test move. Hit testing is the game's business anyway - what is
    /// under test here is what the switch does once it is hit.
    /// </summary>
    public class ConfigurationApply
    {
        const string GameMusicSwitch = "vscfg_src_game";

        static Engine.MusicCurator Curator =>
            (Engine.MusicCurator)typeof(Engine.MusicEngine)
                .GetField("musicCurator", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(VS.MusicEngine);

        /// <summary>Vanilla surface music, as opposed to the cave track (which has no location).</summary>
        static int GameTracks => Curator.Tracks.Count(t =>
            t.Location?.Domain == "game" && t.Location.Path.StartsWith("music/"));

        [VsTest(TimeoutMs = 180000), RequiresClient]
        public async Task ClosingTheDialogAppliesTheSwitchesAndRebuildsThePool()
        {
            await OnClient();

            var dialog = VS.ConfigurationDialog;
            Assert.NotNull(dialog, "configuration dialog");

            var gameSource = VS.MusicSources.Sources.First(s => s.Id == "game");
            var wasOn = gameSource.Enabled;
            Assert.False(wasOn, "the game's own music starts off");
            Assert.Equal(0, GameTracks, "no vanilla tracks in the pool to begin with");

            try
            {
                // --- turn it on -------------------------------------------------
                await OpenDialog(dialog);
                await ClickSwitch(dialog, GameMusicSwitch);

                // The draft is not the configuration: nothing has happened yet.
                Assert.False(gameSource.Enabled, "the source is untouched while the dialog is open");
                Assert.Equal(0, GameTracks, "pool untouched while the dialog is open");

                await CloseDialog(dialog);

                Assert.True(gameSource.Enabled, "applied on close");
                await Until(() => Curator.Tracks.Count > 0 && GameTracks > 0, 300,
                    "the pool rebuilds with vanilla tracks in it");
                Log("after enabling: " + GameTracks + " vanilla tracks, " + Curator.Tracks.Count + " total");

                // --- and off again ----------------------------------------------
                await OpenDialog(dialog);
                await ClickSwitch(dialog, GameMusicSwitch);
                await CloseDialog(dialog);

                Assert.False(gameSource.Enabled, "applied on close again");

                // This is the half that used to need a restart: turning it off left
                // everything already loaded in the pool for the rest of the session.
                //
                // Wait for a *refilled* pool, not just an empty one - clearing it is the
                // first half of a rebuild, and a test that stops there would pass just as
                // happily if the tracks never came back.
                await Until(() => Curator.Tracks.Count > 0 && GameTracks == 0, 300,
                    "the pool rebuilds without vanilla tracks in it");
                Log("after disabling: " + GameTracks + " vanilla tracks, " + Curator.Tracks.Count + " total");
            }
            finally
            {
                if (dialog.IsOpened()) await CloseDialog(dialog);
                gameSource.Enabled = wasOn;
            }
        }

        /// <summary>
        /// The one setting that is not a source goes through the same draft: nothing
        /// changes until the dialog closes, and what changes is written to disk.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task ThePlaylistSwitchIsAppliedOnCloseAndSaved()
        {
            await OnClient();

            var dialog = VS.ConfigurationDialog;
            var config = VS.Configuration;
            var was = config.HonourGamePlaylists;
            try
            {
                await OpenDialog(dialog);
                await ClickSwitch(dialog, Config.ConfigurationDialog.PlaylistsSwitchKey);
                Assert.Equal(was, config.HonourGamePlaylists, "untouched while the dialog is open");

                await CloseDialog(dialog);
                Assert.Equal(!was, config.HonourGamePlaylists, "applied on close");

                var saved = Capi.LoadModConfig<Config.Configuration>("vintagesymphonyforked.json");
                Assert.NotNull(saved, "the config file");
                Assert.Equal(!was, saved.HonourGamePlaylists, "written to the config file");

                await OpenDialog(dialog);
                await ClickSwitch(dialog, Config.ConfigurationDialog.PlaylistsSwitchKey);
                await CloseDialog(dialog);
                Assert.Equal(was, config.HonourGamePlaylists, "applied on close again");
            }
            finally
            {
                if (dialog.IsOpened()) await CloseDialog(dialog);
                config.HonourGamePlaylists = was;
            }
        }

        // ---- helpers ----------------------------------------------------------

        static async Task OpenDialog(GuiDialog dialog)
        {
            dialog.TryOpen();
            await Frames.Wait(5);
            Assert.True(dialog.IsOpened(), "dialog opened");
        }

        static async Task CloseDialog(GuiDialog dialog)
        {
            dialog.TryClose();
            await Ticks(5);
            Assert.False(dialog.IsOpened(), "dialog closed");
        }

        static async Task ClickSwitch(GuiDialog dialog, string key)
        {
            var element = dialog.SingleComposer.GetSwitch(key);
            Assert.NotNull(element, "switch " + key);

            var before = element.On;
            var at = new MouseEvent(
                (int)(element.Bounds.absX + element.Bounds.OuterWidth / 2),
                (int)(element.Bounds.absY + element.Bounds.OuterHeight / 2),
                EnumMouseButton.Left, 0);

            element.OnMouseDownOnElement(Capi, at);
            await Frames.Wait(3);

            Assert.NotEqual(before, element.On, "the click landed on the switch");
        }
    }
}
