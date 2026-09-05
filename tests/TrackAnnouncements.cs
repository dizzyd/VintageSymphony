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

namespace VintageSymphony.Tests
{
    /// <summary>
    /// A track that starts is announced in chat, with its artist when the pack names
    /// one, once per track per sitting, and not at all when the player has said no.
    /// The rule is plain logic on a fake chat; the wiring - that a real track start
    /// reaches a real chat line - is the part only the game can prove.
    /// </summary>
    public class TrackAnnouncements
    {
        [VsTest]
        public async Task ATrackIsAnnouncedOnceWithItsArtist()
        {
            await Ticks(1);
            Engine.TrackAnnouncer.Forget();

            var said = new List<string>();
            var wanted = true;
            var announcer = new Engine.TrackAnnouncer(said.Add, () => wanted);

            var credited = new Engine.MusicTrack { Location = new AssetLocation("vstestpack", "hearth") };
            credited.Title = "By the Hearth";
            credited.Artist = "Someone Kind";
            var anonymous = new Engine.MusicTrack { Location = new AssetLocation("vstestpack", "drums") };
            anonymous.Title = "War Drums";

            announcer.TrackStarted(credited);
            Assert.Equal("Now playing By the Hearth (Someone Kind)", said.LastOrDefault(), "the credited track");

            announcer.TrackStarted(anonymous);
            Assert.Equal("Now playing War Drums", said.LastOrDefault(), "a track nobody claimed");

            announcer.TrackStarted(credited);
            announcer.TrackStarted(anonymous);
            Assert.Equal(2, said.Count, "lines said after each track played twice");

            // Another announcer in the same sitting - a rejoined world - knows them too.
            new Engine.TrackAnnouncer(said.Add, () => true).TrackStarted(credited);
            Assert.Equal(2, said.Count, "lines said after a fresh announcer saw an old track");

            Engine.TrackAnnouncer.Forget();
            wanted = false;
            announcer.TrackStarted(credited);
            Assert.Equal(2, said.Count, "lines said with announcements switched off");

            wanted = true;
            announcer.TrackStarted(credited);
            Assert.Equal(3, said.Count, "switched back on, a forgotten track is news again");

            Engine.TrackAnnouncer.Forget();
        }

        /// <summary>
        /// The pack this mod ships puts its artists in brackets after the titles, since
        /// the game's format has no field for them. Read as the game reads the pack -
        /// through the JSON parser, then initialised - the bracket comes off and becomes
        /// the credit. A title that names no one, or one that already has an artist, is
        /// left as it is.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task AGameFormatTitleGivesUpItsArtist()
        {
            await OnClient();

            var credited = FromConfig("{ \"file\": \"run-faster\", \"title\": \"Run Faster (David Fesliyan)\" }");
            Assert.Equal("Run Faster", credited.Title, "the title with its bracket gone");
            Assert.Equal("David Fesliyan", credited.Artist, "the artist read from the bracket");
            Assert.Equal("Now playing Run Faster (David Fesliyan)", Engine.TrackAnnouncer.Describe(credited), "as announced");

            var named = FromConfig("{ \"file\": \"funeral\", \"title\": \"Funeral (slow)\", \"artist\": \"Someone\" }");
            Assert.Equal("Funeral (slow)", named.Title, "a title left alone when the artist is given");
            Assert.Equal("Someone", named.Artist, "and that artist kept");

            var plain = FromConfig("{ \"file\": \"mist\", \"title\": \"Mist\" }");
            Assert.Equal("Mist", plain.Title, "a title with no bracket");
            Assert.True(string.IsNullOrEmpty(plain.Artist), "names nobody");
        }

        static Engine.MusicTrack FromConfig(string json)
        {
            var track = Newtonsoft.Json.JsonConvert.DeserializeObject<Engine.MusicTrack>(json);
            track.Location = new AssetLocation("vstestpack", track.Location?.Path ?? "track");
            track.Initialize(Capi.Assets, Capi, Vanilla());
            return track;
        }

        /// <summary>
        /// The wiring: playback tells the announcer, the announcer tells chat. The pool
        /// is vanilla audio opened up so that something plays regardless of the hour the
        /// box happens to be in, and the wrapper's pretty title is what should be said.
        /// </summary>
        [VsTest(TimeoutMs = 240000), RequiresClient]
        public async Task AStartingTrackReachesTheChat()
        {
            await OnClient();

            var engine = VS.MusicEngine;
            var curator = Curator(engine);
            var wasMusicLevel = ClientSettings.MusicLevel;
            var wasAnnouncing = VS.Configuration.AnnounceTracks;
            var lines = new List<string>();
            ChatLineDelegate listen = (groupId, message, chatType, data) => lines.Add(message);

            try
            {
                Engine.TrackAnnouncer.Forget();
                VS.Configuration.AnnounceTracks = true;
                Capi.Event.ChatMessage += listen;

                var pool = VanillaTracks().OfType<SurfaceMusicTrack>().Take(2)
                    .Select(AlwaysPlayable).ToList();
                Assert.Equal(2, pool.Count, "tracks in the test pool");
                curator.Tracks = pool;
                ClientSettings.MusicLevel = 20;

                await Until(() => engine.Playback.CurrentPlaylist?.Tracks.Count == 2, 900,
                    "the curator picks up the test pool");
                engine.NextTrack();
                await Until(() => engine.CurrentMusicTrack?.IsPlaying == true, 900, "a track starts");

                var playing = engine.CurrentMusicTrack;
                var expected = Engine.TrackAnnouncer.Describe(playing);
                await Until(() => lines.Contains(expected), 100, "the chat line arrives");
                Log("chat said: " + expected);
                Assert.True(expected.StartsWith("Now playing "), "the line's shape");
                Assert.False(expected.Contains("game:"), "the title was made readable: " + expected);
            }
            finally
            {
                Capi.Event.ChatMessage -= listen;
                engine.Playback.StopTrack(0f);
                ClientSettings.MusicLevel = wasMusicLevel;
                VS.Configuration.AnnounceTracks = wasAnnouncing;
                Engine.TrackAnnouncer.Forget();
                curator.Tracks = new List<Engine.MusicTrack>();
            }
        }

        // ---- helpers, as PlaybackControl has them -----------------------------

        static Engine.MusicTrack AlwaysPlayable(SurfaceMusicTrack vanilla)
        {
            var path = vanilla.Location.Path;
            path = path.Substring("music/".Length, path.Length - "music/".Length - ".ogg".Length);

            var track = new Engine.MusicTrack
            {
                Location = new AssetLocation(vanilla.Location.Domain, path),
                Situation = string.Join("|", System.Enum.GetNames(typeof(Situations.Situation))),
                MinSunlight = 0
            };
            track.Title = path;

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

        static SystemMusicEngine Vanilla() =>
            VS.ClientMain.clientSystems.OfType<SystemMusicEngine>().First();

        static IMusicTrack[] VanillaTracks() =>
            typeof(SystemMusicEngine)
                .GetField("shuffledTracks", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(Vanilla()) as IMusicTrack[];
    }
}
