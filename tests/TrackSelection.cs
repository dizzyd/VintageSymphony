using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using VS = VintageSymphony.VintageSymphony;

namespace VintageSymphony.Tests
{
    /// <summary>
    /// End to end: the game's own track list, through the filter and the wrapper,
    /// into a playlist, out as a playing sound.
    ///
    /// This drives it off the *vanilla* music rather than the mod's own asset pack,
    /// because the pack is a 200MB separate download the updater fetches at runtime
    /// and a test box need not have it. That makes this the test that covers
    /// GameTrackSituationLibrary - the hardcoded table of vanilla track names, which
    /// silently degrades to "Calm" for anything the game renamed or added.
    /// </summary>
    public class TrackSelection
    {
        [VsTest(TimeoutMs = 180000), RequiresClient]
        public async Task VanillaTracksLoadMapAndPlay()
        {
            await OnClient();

            var engine = VS.MusicEngine;
            var curator = Curator(engine);
            var vanilla = VanillaTracks();
            Assert.Greater(vanilla.Length, 0, "tracks the game handed the patch");

            var gameSource = VS.MusicSources.Sources.First(s => s.Id == "game");
            var wasLoadingGameMusic = gameSource.Enabled;
            var wasMusicLevel = ClientSettings.MusicLevel;
            try
            {
                // The harness runs silent and the game's own music is off by default, so
                // switch that source on deliberately.
                gameSource.Enabled = true;
                curator.Tracks = new List<Engine.MusicTrack>();
                engine.LoadTracks(vanilla, Vanilla());

                Assert.Equal(vanilla.Length, curator.Tracks.Count,
                    "every vanilla track survives the filter when game music is on");

                // The vanilla names are the mod's own hardcoded table, so a gap there is
                // a bug in this repo and fails. Tracks from the asset pack carry their
                // situation in their own JSON - a gap there is the pack's data, so it is
                // reported and left to the pack. (1.1.0 ships two with an empty string:
                // deskant_-_crying_winds and jon_algar_-_natures_way, which can never
                // be selected because they join no playlist.)
                var unmapped = curator.Tracks.Where(t => t.TrackSituations.Length == 0).ToList();
                var unmappedVanilla = unmapped.Where(t => t.Location?.Domain == "game").ToList();

                Assert.Equal(0, unmappedVanilla.Count,
                    "vanilla tracks with no situation: " +
                    string.Join(", ", unmappedVanilla.Select(t => t.Name)));

                if (unmapped.Count > unmappedVanilla.Count)
                {
                    Log("asset pack tracks with no situation (they can never play): " +
                        string.Join(", ", unmapped.Except(unmappedVanilla).Select(t => t.Name)));
                }

                Log("mapped: " + string.Join("  ", curator.Tracks
                    .GroupBy(t => t.Situation)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key + "=" + g.Count())));

                ClientSettings.MusicLevel = 20;

                // The engine holds its playback loop for 10s after the player joins, and
                // Until polls per tick rather than per second - so this budget has to
                // cover that delay, or the test result depends on how long the tests
                // before it happened to take.
                await Until(() => engine.Playback.CurrentPlaylist != null, 1500,
                    "the curator picks a playlist");

                engine.NextTrack();

                // Not necessarily this instant: if something was already playing, the next
                // track waits for its fade out rather than starting over the top of it.
                await Until(() => engine.CurrentMusicTrack != null, 900, "a track was selected");

                var track = engine.CurrentMusicTrack;
                await Until(() => track.IsPlaying, 300, "the selected track starts playing");

                Log("playing " + track.Name + " [" + track.Situation + "] from the " +
                    engine.Playback.CurrentPlaylist.Situation + " playlist");

                // And the game's own engine still has not started anything of its own.
                Assert.Null(VanillaCurrentTrack(), "vanilla currentTrack while the mod plays");
            }
            finally
            {
                engine.Playback.StopTrack(0f);
                ClientSettings.MusicLevel = wasMusicLevel;
                gameSource.Enabled = wasLoadingGameMusic;
            }
        }

        // ---- helpers ----------------------------------------------------------

        static Engine.MusicCurator Curator(Engine.MusicEngine engine)
        {
            var curator = typeof(Engine.MusicEngine)
                .GetField("musicCurator", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(engine) as Engine.MusicCurator;
            Assert.NotNull(curator, "music curator");
            return curator;
        }

        static SystemMusicEngine Vanilla() =>
            VS.ClientMain.clientSystems.OfType<SystemMusicEngine>().First();

        static IMusicTrack[] VanillaTracks() =>
            typeof(SystemMusicEngine)
                .GetField("shuffledTracks", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(Vanilla()) as IMusicTrack[];

        static IMusicTrack VanillaCurrentTrack() =>
            typeof(SystemMusicEngine)
                .GetField("currentTrack", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(Vanilla()) as IMusicTrack;
    }
}
