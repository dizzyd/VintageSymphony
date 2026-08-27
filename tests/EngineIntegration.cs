using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using VS = VintageSymphony.VintageSymphony;

namespace VintageSymphony.Tests
{
    /// <summary>
    /// The port-critical surface: this mod reaches into the game by string.
    /// It prefixes SystemMusicEngine.OnEverySecond and returns false, and it reads
    /// the private shuffledTracks field through Harmony's ___ convention. Both
    /// compile against anything and fail silently in-world if the game moved them,
    /// which is exactly what a version bump does.
    ///
    /// Everything here is [RequiresClient] - the whole mod is client-only.
    /// </summary>
    public class EngineIntegration
    {
        const string ModId = "vintagesymphony";

        static MethodInfo VanillaTick =>
            typeof(SystemMusicEngine).GetMethod("OnEverySecond",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        [VsTest, RequiresClient]
        public async Task ModSystemsCameUp()
        {
            await OnClient();

            Assert.NotNull(VS.Instance, "VintageSymphony mod system");
            Assert.NotNull(VS.MusicEngine, "MusicEngine mod system");
            Assert.NotNull(VS.Configuration, "configuration loaded");
            Assert.NotNull(VS.ClientMain, "ClientMain cast");
            Assert.NotNull(VS.Instance.AttributeStorage, "attribute storage");
        }

        /// <summary>
        /// The patch target still exists under that name, and is patched once and
        /// only once. Start() runs per side, so a PatchAll() there can register
        /// twice in singleplayer - here it is guarded by HasAnyPatches, and this
        /// is what proves the guard still holds.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task OnEverySecondIsPrefixedExactlyOnce()
        {
            await OnClient();

            Assert.NotNull(VanillaTick, "SystemMusicEngine.OnEverySecond still exists");

            var info = Harmony.GetPatchInfo(VanillaTick);
            Assert.NotNull(info, "OnEverySecond carries patches");

            var mine = info.Prefixes.Count(p => p.owner == ModId);
            Assert.Equal(1, mine, "prefixes owned by " + ModId);
        }

        /// <summary>
        /// The prefix reads ___shuffledTracks. If the field were renamed Harmony
        /// would throw at patch time, but if it merely changed *type* the patch
        /// would attach and hand the engine nothing - so assert the mod actually
        /// received tracks rather than that the patch exists.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task TracksReachedTheCurator()
        {
            await OnClient();

            var tracks = LoadedTracks();
            Assert.NotNull(tracks, "curator track list");
            Assert.Greater(tracks.Count, 0, "tracks handed to the curator");
            Log("curator holds " + tracks.Count + " track(s): " +
                string.Join(", ", tracks.Select(Describe)));
        }

        /// <summary>
        /// The prefix returns false, so the vanilla engine must never pick a track
        /// of its own. Its currentTrack staying null is the whole point of the mod:
        /// if this fails, both engines are playing at once.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task VanillaEngineNeverStartsATrack()
        {
            await OnClient();

            // Vanilla ticks OnEverySecond; give it several chances to disobey.
            await Ticks(120);

            Assert.Null(VanillaCurrentTrack(), "vanilla SystemMusicEngine.currentTrack");
        }

        /// <summary>
        /// The situation thread is what drives every playlist choice. It runs off
        /// the main thread on a 300ms loop, so a throw in fact collection would
        /// kill it quietly and leave the scores frozen at their initial values.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task SituationAssessorIsRunning()
        {
            await OnClient();

            var assessor = VS.MusicEngine.SituationAssessor;
            Assert.NotNull(assessor, "situation assessor");
            Assert.Equal(System.Enum.GetValues<Situations.Situation>().Length,
                assessor.Assessments.Count, "one assessment per situation");

            // Facts are a struct rebuilt each pass; Alive can only be true if the
            // collector got all the way through without throwing.
            await Until(() => assessor.SituationalFacts.Alive, 200, "facts collected");

            var facts = assessor.SituationalFacts;
            Assert.NotEqual(0L, facts.Now, "facts timestamp");
            Log("scores: " + string.Join(", ",
                assessor.Assessments.Select(a => a.Situation + "=" + a.Score.ToString("0.00"))));
        }

        // ---- helpers ----------------------------------------------------------

        static System.Collections.Generic.List<Engine.MusicTrack> LoadedTracks()
        {
            var curator = typeof(Engine.MusicEngine)
                .GetField("musicCurator", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(VS.MusicEngine);
            Assert.NotNull(curator, "music curator");
            return ((Engine.MusicCurator)curator).Tracks;
        }

        static IMusicTrack VanillaCurrentTrack()
        {
            var music = VS.ClientMain.clientSystems.OfType<SystemMusicEngine>().FirstOrDefault();
            Assert.NotNull(music, "SystemMusicEngine instance");

            return typeof(SystemMusicEngine)
                .GetField("currentTrack", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(music) as IMusicTrack;
        }

        static string Describe(Engine.MusicTrack t) =>
            t.Name + "[" + (t.Situation == "" ? "-" : t.Situation) + "]";
    }
}
