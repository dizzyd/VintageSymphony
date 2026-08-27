using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using VS = VintageSymphony.VintageSymphony;

namespace VintageSymphony.Tests
{
    /// <summary>
    /// Music can come from a plain folder rather than a second mod, and a folder can
    /// describe its tracks with the simple tracks.json instead of the game's musicconfig.
    ///
    /// These assert the wiring that makes that work: the source list on disk, the asset
    /// origin per source, and that a hand-written manifest turns into tracks the engine
    /// will actually select. Adding a source needs an asset origin registered before
    /// assets load, so a source invented mid-session cannot be played until a restart -
    /// which is why these check the loader rather than driving a live install.
    /// </summary>
    public class MusicSourcesTests
    {
        static Music.MusicSources Sources => VS.MusicSources;

        [VsTest, RequiresClient]
        public async Task ASourceListIsWrittenAndTheDefaultSourceIsInIt()
        {
            await OnClient();

            Assert.NotNull(Sources, "music sources");
            Assert.True(File.Exists(Path.Combine(Sources.RootPath, Music.MusicSources.FileName)),
                "sources.json written to " + Sources.RootPath);

            var byDefault = Sources.Sources.FirstOrDefault(s => s.Id == Music.MusicSources.DefaultSourceId);
            Assert.NotNull(byDefault, "the default music source is listed");
            Assert.True(byDefault.Enabled, "the default music source is enabled");
            Log("sources: " + string.Join(", ", Sources.Sources.Select(s => s.ToString())));
        }

        /// <summary>
        /// The heart of it: files in a folder, a manifest naming their situations, and
        /// tracks that the engine can pick. Built through the loader rather than a
        /// restart, since the origin for this source is registered at startup.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task AHandWrittenManifestBecomesPlayableTracks()
        {
            await OnClient();

            // Borrow an .ogg the installed music already provides, so this needs no
            // fixture of its own.
            var installed = Sources.Installed.FirstOrDefault();
            Assert.NotNull(installed, "a music source is installed to borrow a file from");

            var borrowed = Directory.GetFiles(Sources.MusicPathOf(installed), "*.ogg").FirstOrDefault();
            Assert.NotNull(borrowed, "an .ogg to borrow");

            var source = new Music.MusicSource { Id = "vstestsource", Name = "test source", Enabled = true };
            var musicPath = Sources.MusicPathOf(source);
            Directory.CreateDirectory(musicPath);

            try
            {
                var copied = Path.Combine(musicPath, "test-track.ogg");
                File.Copy(borrowed, copied, true);

                File.WriteAllText(Path.Combine(musicPath, Music.TrackManifest.FileName),
                    JsonConvert.SerializeObject(new Music.TrackManifest
                    {
                        Tracks =
                        {
                            new Music.TrackEntry
                            {
                                File = "test-track.ogg",
                                Situations = new[] { "fight", "danger" },
                                Title = "A Test Track"
                            }
                        }
                    }));

                Sources.Sources.Add(source);

                var tracks = new Music.LocalMusicLoader(Sources, Capi).LoadTracks(GameMusicEngine());
                var mine = tracks.Where(t => t.Location?.Domain == "vstestsource").ToList();

                Assert.Equal(1, mine.Count, "tracks built from the manifest");
                Assert.Equal("A Test Track", mine[0].Title, "title from the manifest");
                Assert.Equal(2, mine[0].TrackSituations.Length, "situations parsed");
                Assert.True(mine[0].TrackSituations.Contains(Situations.Situation.Fight), "fight parsed");
                Assert.True(mine[0].TrackSituations.Contains(Situations.Situation.Danger), "danger parsed");

                // The base class defaults to wanting daylight, which would keep a
                // hand-added track from ever playing in a cave or at night.
                Assert.Equal(0, mine[0].MinSunlight, "hand-added tracks are not daylight-only");

                Log("built " + mine[0].Location + " for " + mine[0].Situation);
            }
            finally
            {
                Sources.Sources.RemoveAll(s => s.Id == "vstestsource");
                Directory.Delete(Sources.DirectoryOf(source), true);
            }
        }

        /// <summary>
        /// Files with nothing describing them get listed rather than ignored, so dropping
        /// .ogg files in a folder is enough to hear them.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task LooseFilesGetAStarterManifest()
        {
            await OnClient();

            var installed = Sources.Installed.FirstOrDefault();
            Assert.NotNull(installed, "a music source is installed to borrow a file from");
            var borrowed = Directory.GetFiles(Sources.MusicPathOf(installed), "*.ogg").FirstOrDefault();

            var source = new Music.MusicSource { Id = "vsloosefiles", Name = "loose files", Enabled = true };
            var musicPath = Sources.MusicPathOf(source);
            Directory.CreateDirectory(musicPath);

            try
            {
                File.Copy(borrowed, Path.Combine(musicPath, "dropped-in.ogg"), true);
                Sources.Sources.Add(source);

                var tracks = new Music.LocalMusicLoader(Sources, Capi).LoadTracks(GameMusicEngine());
                var manifestPath = Path.Combine(musicPath, Music.TrackManifest.FileName);

                Assert.True(File.Exists(manifestPath), "a starter manifest was written");
                Assert.Equal(1, tracks.Count(t => t.Location?.Domain == "vsloosefiles"), "the loose file plays");
                Log("starter manifest: " + File.ReadAllText(manifestPath).Replace("\n", " ").Replace("\r", ""));
            }
            finally
            {
                Sources.Sources.RemoveAll(s => s.Id == "vsloosefiles");
                Directory.Delete(Sources.DirectoryOf(source), true);
            }
        }

        static Vintagestory.API.Client.IMusicEngine GameMusicEngine() =>
            VS.ClientMain.clientSystems.OfType<Vintagestory.Client.NoObf.SystemMusicEngine>().First();
    }
}
