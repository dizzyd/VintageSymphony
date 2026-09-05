using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Common;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using VS = VintageSymphony.VintageSymphony;

namespace VintageSymphony.Tests
{
    /// <summary>
    /// Music can ship inside an ordinary mod: a tracks.json and its files under the mod's
    /// music assets. Nobody should have to edit sources.json for that - the engine finds
    /// the manifest through the asset manager, lists the mod as a source, reads the
    /// manifest as an asset rather than a file, and takes the entry back when the mod
    /// goes. All of that is asset-manager and string plumbing, so it is proved in-world.
    ///
    /// A mod's assets are, to the asset manager, an origin for a domain - which is what
    /// this stands up by hand, the way the download path does for a folder of ours.
    /// </summary>
    public class ModShippedMusic
    {
        static Music.MusicSources Sources => VS.MusicSources;

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task AModThatShipsATracksJsonBecomesASourceOnItsOwn()
        {
            await OnClient();

            const string domain = "vsmodpack";
            var gameMusic = Path.Combine(GamePaths.AssetsPath, "survival", "music");
            var borrowed = Directory.GetFiles(gameMusic, "*.ogg").OrderBy(f => f).FirstOrDefault();
            Assert.NotNull(borrowed, "an .ogg of the game's own to borrow from " + gameMusic);

            var root = Path.Combine(Path.GetTempPath(), "vs-modpack-test");
            var music = Path.Combine(root, "music");
            Delete(root);
            Directory.CreateDirectory(music);
            File.Copy(borrowed, Path.Combine(music, "hearth.ogg"), true);
            File.WriteAllText(Path.Combine(music, "tracks.json"),
                "{ \"tracks\": [ " +
                "{ \"file\": \"hearth.ogg\", \"situations\": [\"keep\", \"idle\"], \"title\": \"By the Hearth\" }, " +
                "{ \"file\": \"missing.ogg\", \"situations\": [\"calm\"] } ] }");

            Sources.Sources.RemoveAll(s => s.Id == domain);
            var origin = new PathOrigin(domain, root);

            try
            {
                Capi.Assets.Origins.Add(origin);
                Capi.Assets.Reload(AssetCategory.music);
                VS.MusicEngine.ReloadTracks();

                await Until(() => PoolHas(domain), 300, "the mod's track joins the pool");

                var source = Sources.Sources.FirstOrDefault(s => s.Id == domain);
                Assert.NotNull(source, "the mod was listed as a source");
                Assert.True(Music.MusicSources.IsFromMod(source), "and marked as coming from a mod");
                Assert.True(source.Enabled, "switched on");
                Log("listed as " + source + " from mod '" + source.Mod + "'");

                var track = Pool().First(t => t.Location?.Domain == domain);
                Assert.Equal("By the Hearth", track.Title, "the title came from the manifest");
                Assert.True(track.TrackSituations.Contains(Situations.Situation.Keep), "and so did its situations");
                Assert.Equal(1, Pool().Count(t => t.Location?.Domain == domain),
                    "the file that is not there was skipped, the one that is was kept");

                // The mod goes away: the entry made for it goes too, without anyone's hand.
                Capi.Assets.Origins.Remove(origin);
                Capi.Assets.Reload(AssetCategory.music);
                VS.MusicEngine.ReloadTracks();

                await Until(() => Sources.Sources.All(s => s.Id != domain), 300,
                    "the entry made for the mod is taken back with it");
                Assert.False(PoolHas(domain), "and its track is out of the pool");
            }
            finally
            {
                Capi.Assets.Origins.Remove(origin);
                Capi.Assets.Reload(AssetCategory.music);
                Sources.Sources.RemoveAll(s => s.Id == domain);
                Sources.Save();
                Delete(root);
                VS.MusicEngine.ReloadTracks();
            }
        }

        static System.Collections.Generic.List<Engine.MusicTrack> Pool() =>
            ((Engine.MusicCurator)typeof(Engine.MusicEngine)
                .GetField("musicCurator", System.Reflection.BindingFlags.Instance |
                                          System.Reflection.BindingFlags.NonPublic)
                .GetValue(VS.MusicEngine)).Tracks;

        static bool PoolHas(string domain) => Pool().Any(t => t.Location?.Domain == domain);

        static void Delete(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}
