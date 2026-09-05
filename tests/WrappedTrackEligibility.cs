using System;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using VS = VintageSymphony.VintageSymphony;

namespace VintageSymphony.Tests
{
    public class WrappedTrackEligibility
    {
        [VsTest, RequiresClient]
        public async Task VillageTrackIsRejectedOutsideItsStructure()
        {
            await OnClient();

            var track = new SurfaceMusicTrack
            {
                Location = new AssetLocation("game", "nadiya-spring"),
                Priority = 1.5f,
                MinSunlight = 0,
                InStructureLocationCode = "vstestkit:nonexistent-village"
            };
            var gameEngine = VS.ClientMain.clientSystems.OfType<SystemMusicEngine>().First();
            track.Initialize(Capi.Assets, Capi, gameEngine);
            var wasAllowed = SurfaceMusicTrack.ShouldPlayMusic;
            var wasCooldown = SurfaceMusicTrack.globalCooldownUntilMs;
            try
            {
                SurfaceMusicTrack.ShouldPlayMusic = true;
                SurfaceMusicTrack.globalCooldownUntilMs = 0;
                var props = new TrackedPlayerProperties { sunSlight = 15 };
                var pos = Capi.World.Player.Entity.Pos.AsBlockPos;
                var climate = Capi.World.BlockAccessor.GetClimateAt(pos);
                Assert.Equal("notinstructurerange", track.GetPlayTestCode(props, climate, pos),
                    "the real vanilla track rejects this location");

                var playback = Playback(props);
                playback.Play(new Engine.Playlist(Situations.Situation.Calm,
                    new[] { new Engine.MusicTrackWrapper(track) }));
                playback.NextTrack();
                Assert.Null(playback.CurrentTrack,
                    "even the final selection fallback must reject the village track");
            }
            finally
            {
                if (track.IsActive) track.FadeOut(0);
                SurfaceMusicTrack.ShouldPlayMusic = wasAllowed;
                SurfaceMusicTrack.globalCooldownUntilMs = wasCooldown;
            }
        }

        [VsTest, RequiresClient]
        public async Task MixedPoolRespectsEligibilityEvenWhenAllTracksAreOnCooldown()
        {
            await OnClient();

            var vanilla = new GatedTrack();
            var wrapper = new Engine.MusicTrackWrapper(vanilla);
            var custom = new CustomTrack();
            var cooldowns = new Engine.TrackCooldownManager(() => 0L);
            cooldowns.SetCooldownDuration(60000);
            cooldowns.PutOnCooldown(wrapper);
            cooldowns.PutOnCooldown(custom);
            var playback = new Engine.Playback(Capi.Logger, cooldowns,
                () => new TrackedPlayerProperties { sunSlight = 15 }, () => 0L);
            playback.Play(new Engine.Playlist(Situations.Situation.Calm,
                new Engine.MusicTrack[] { wrapper, custom }));

            playback.NextTrack();
            Assert.True(playback.CurrentTrack == custom,
                "an ineligible high priority vanilla track cannot crowd out custom music");
            Assert.Equal(0, vanilla.Starts, "ineligible track never starts");

            // Once eligible, the same wrapped track can be selected normally.
            vanilla.Allowed = true;
            playback.NextTrack();
            Assert.True(playback.CurrentTrack == wrapper, "eligible vanilla music still plays");
            Assert.Equal(1, vanilla.Starts, "eligible track starts once");
        }

        /// <summary>
        /// The game marks each of its tracks for survival, creative or both. Whether the
        /// mod keeps that split is a setting; the game's other rules stay either way.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task ThePlaylistSplitIsASetting()
        {
            await OnClient();

            var props = VS.ClientMain.playerProperties;
            var otherMode = props.PlayListCode == "creative" ? "survival" : "creative";
            var track = new SurfaceMusicTrack
            {
                Location = new AssetLocation("game", "playlist-test"),
                OnPlayList = otherMode,
                MinSunlight = 0
            };
            var gameEngine = VS.ClientMain.clientSystems.OfType<SystemMusicEngine>().First();
            track.Initialize(Capi.Assets, Capi, gameEngine);
            var wrapper = new Engine.MusicTrackWrapper(track);

            var wasAllowed = SurfaceMusicTrack.ShouldPlayMusic;
            var wasCooldown = SurfaceMusicTrack.globalCooldownUntilMs;
            var wasFrequency = ClientSettings.MusicFrequency;
            var wasHonoured = VS.Configuration.HonourGamePlaylists;
            try
            {
                // Everything but the playlist opened up: full hours, no cooldown.
                SurfaceMusicTrack.ShouldPlayMusic = true;
                ClientSettings.MusicFrequency = 3;
                SurfaceMusicTrack.globalCooldownUntilMs = 0;
                var pos = Capi.World.Player.Entity.Pos.AsBlockPos;
                var climate = Capi.World.BlockAccessor.GetClimateAt(pos);
                Assert.Equal("notonplaylist", track.GetPlayTestCode(props, climate, pos),
                    "the game refuses a track written for the other mode");

                VS.Configuration.HonourGamePlaylists = true;
                Assert.False(wrapper.ShouldPlay(props, climate, pos), "honoured: the split holds");

                VS.Configuration.HonourGamePlaylists = false;
                Assert.True(wrapper.ShouldPlay(props, climate, pos), "waived: the track may play");
                Assert.Equal(otherMode, track.OnPlayList, "the track's own playlist is put back");
            }
            finally
            {
                VS.Configuration.HonourGamePlaylists = wasHonoured;
                ClientSettings.MusicFrequency = wasFrequency;
                SurfaceMusicTrack.ShouldPlayMusic = wasAllowed;
                SurfaceMusicTrack.globalCooldownUntilMs = wasCooldown;
            }
        }

        static Engine.Playback Playback(TrackedPlayerProperties props) =>
            new Engine.Playback(Capi.Logger, new Engine.TrackCooldownManager(() => 0L),
                () => props, () => 0L);

        // No audio is needed to observe which track selection actually starts.
        class GatedTrack : SurfaceMusicTrack
        {
            public bool Allowed;
            public int Starts;
            public GatedTrack()
            {
                Location = new AssetLocation("game", "eligibility-test");
                Priority = 100;
            }
            public override bool ShouldPlay(TrackedPlayerProperties props, ClimateCondition conds, BlockPos pos) => Allowed;
            public override void BeginPlay(TrackedPlayerProperties props) => Starts++;
        }

        class CustomTrack : Engine.MusicTrack
        {
            public CustomTrack()
            {
                Location = new AssetLocation("vstestpack", "eligible");
                MinSunlight = 0;
            }
            public override bool ShouldPlay(TrackedPlayerProperties props, ClimateCondition conds, BlockPos pos) =>
                throw new InvalidOperationException("Custom tracks must use the mod's restrictions.");
            public override void BeginPlay(TrackedPlayerProperties props) { }
        }
    }
}
