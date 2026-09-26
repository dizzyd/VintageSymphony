using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using VS = VintageSymphony.VintageSymphony;
using static VintageSymphony.Situations.Situation;

namespace VintageSymphony.Tests
{
    /// <summary>
    /// Music the game starts by script, which this engine used to play over (see
    /// SituationalFactsCollector for what counts). This proves it now stands down for
    /// it, with the real game track and the real engine, and gets going again after.
    /// </summary>
    public class ScriptedTracks
    {
        static Situations.Scoring.SituationAssessor Assessor => VS.MusicEngine.SituationAssessor;
        static Situations.Facts.SituationalFacts Facts => Assessor.SituationalFacts;

        static Situations.Situation Leader =>
            Assessor.Assessments.First().Situation;

        static SystemMusicEngine Vanilla() =>
            VS.ClientMain.clientSystems.OfType<SystemMusicEngine>().First();

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task AScriptedTrackSilencesTheEngineUntilItEnds()
        {
            await OnClient();

            var wasMusicLevel = ClientSettings.MusicLevel;
            MusicTrack scripted = null;

            try
            {
                ClientSettings.MusicLevel = 20;

                await Until(() => !Facts.ScriptedTrackPlaying, 200, "nothing scripted before the test");

                // The way BEResonator and BehaviorBoss do it. A short one: the loader
                // decodes it whole, and the fact is meant to flip while that happens.
                scripted = Capi.StartTrack(new AssetLocation("game:music/setting-sun.ogg"), 99f,
                    EnumSoundType.Music);
                Assert.NotNull(scripted, "the game accepted the scripted track");

                await Until(() => Facts.ScriptedTrackPlaying, 200, "the scripted track is noticed");
                await Until(() => Leader == Silence, 200, "Silence leads");
                Log("silence weighted " + Assessor.Assessments.First().WeightedScore.ToString("0.00"));

                // Urgent: the curator follows without the dwell time, and plays nothing.
                // The playback tick does not start until ten seconds into the world, so
                // the first test in a session waits for that too.
                await Until(() => VS.MusicEngine.Playback.CurrentPlaylist?.Situation == Silence, 1500,
                    "the curator selects the Silence playlist");
                await Until(() => VS.MusicEngine.Playback.CurrentTrack == null, 1500,
                    "the engine has no track of its own sounding");

                // Let it end the way a firepit's song does.
                scripted.FadeOut(0f);
                Vanilla().StopTrack(scripted);

                await Until(() => !Facts.ScriptedTrackPlaying, 200, "the scripted track is over");
                await Until(() => Leader != Silence, 600, "Silence gives way");
            }
            finally
            {
                if (scripted != null)
                {
                    scripted.FadeOut(0f);
                    Vanilla().StopTrack(scripted);
                }

                ClientSettings.MusicLevel = wasMusicLevel;
            }
        }
        /// <summary>
        /// The overlap itself: this engine has a track sounding when the game starts one
        /// of its own. The game's loop would never have started ours over its scripted
        /// track; ours has to stop instead, and stay stopped rather than pick the next.
        /// </summary>
        [VsTest(TimeoutMs = 180000), RequiresClient]
        public async Task AScriptedTrackStopsTheEngineOwnMusic()
        {
            await OnClient();

            var engine = VS.MusicEngine;
            var curator = Curator(engine);
            var wasMusicLevel = ClientSettings.MusicLevel;
            MusicTrack scripted = null;

            try
            {
                // Everything but Silence: a pool with music filed under Silence would
                // sound during the silence and prove the opposite.
                curator.Tracks = VanillaTracks().OfType<SurfaceMusicTrack>()
                    .Select(OpenedUp).ToList();
                Assert.Greater(curator.Tracks.Count, 1, "tracks to play");
                ClientSettings.MusicLevel = 20;

                // Not Silence: a test before this one may have left it selected, and
                // a skip inside that empty playlist starts nothing.
                await Until(() => engine.Playback.CurrentPlaylist != null
                                  && engine.Playback.CurrentPlaylist.Situation != Silence, 1500,
                    "the curator picks a playlist with music");
                engine.NextTrack();
                // Sounding, not merely loading: IsPlaying is true for a track still being
                // decoded, and stopping one of those is a different path from fading one.
                await Until(() => engine.CurrentMusicTrack?.Sound?.IsPlaying == true, 900, "a track of ours is sounding");
                var ours = engine.CurrentMusicTrack;
                var oursSound = ours.Sound;
                Log("ours: " + ours.Title + " in " + engine.Playback.CurrentPlaylist);

                scripted = Capi.StartTrack(new AssetLocation("game:music/setting-sun.ogg"), 99f,
                    EnumSoundType.Music);
                Assert.NotNull(scripted, "the game accepted the scripted track");

                await Until(() => engine.Playback.CurrentPlaylist?.Situation == Silence, 1500,
                    "the curator moves to Silence");
                await Until(() => engine.Playback.CurrentTrack == null, 300, "our track is let go of");
                // The fade's end disposes the sound and clears the track's hold on it; only
                // ask a sound the track still holds whether it is playing.
                await Until(() => ours.Sound != oursSound || !oursSound.IsPlaying, 300, "our track's sound has stopped");
                Assert.False(ours.IsPlaying, "and the track knows it");

                // And stays quiet: nothing else of ours starts while the game's plays on.
                await Ticks(120);
                Assert.True(Facts.ScriptedTrackPlaying, "the scripted track is still going");
                Assert.Null(engine.Playback.CurrentTrack, "nothing of ours started during the silence");
                Assert.False(curator.Tracks.Any(t => t.IsPlaying), "no track of ours is sounding");
            }
            finally
            {
                if (scripted != null)
                {
                    scripted.FadeOut(0f);
                    Vanilla().StopTrack(scripted);
                }

                engine.Playback.StopTrack(0f);
                ClientSettings.MusicLevel = wasMusicLevel;
                curator.Tracks = new List<Engine.MusicTrack>();
            }
        }

        /// <summary>A vanilla track's audio, playable in every situation except Silence.</summary>
        static Engine.MusicTrack OpenedUp(SurfaceMusicTrack vanilla)
        {
            var path = vanilla.Location.Path;
            path = path.Substring("music/".Length, path.Length - "music/".Length - ".ogg".Length);

            var track = new Engine.MusicTrack
            {
                Location = new AssetLocation(vanilla.Location.Domain, path),
                Situation = string.Join("|", System.Enum.GetValues<Situations.Situation>()
                    .Where(s => s != Silence).Select(s => s.ToString())),
                MinSunlight = 0
            };

            track.Initialize(Capi.Assets, Capi, Vanilla());
            return track;
        }

        static Engine.MusicCurator Curator(Engine.MusicEngine engine)
        {
            var curator = typeof(Engine.MusicEngine)
                .GetField("musicCurator", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(engine) as Engine.MusicCurator;
            Assert.NotNull(curator, "music curator");
            return curator;
        }

        static IMusicTrack[] VanillaTracks() =>
            typeof(SystemMusicEngine)
                .GetField("shuffledTracks", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(Vanilla()) as IMusicTrack[];
    }
}
