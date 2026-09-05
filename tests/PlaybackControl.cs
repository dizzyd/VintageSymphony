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
    /// Stopping, skipping and choosing - the three things a player notices when they are
    /// wrong, and all three were.
    ///
    /// A track fades out over two seconds. The engine used to drop its reference to it the
    /// instant the fade was ordered and start the next one in the same call, so .music next
    /// laid one song over another, and .music stop only paused for as long as the between-
    /// track pause happened to be - zero, at the highest music frequency, so it read as
    /// "stop starts a new song". Selection sorted on the pack's Priority with no randomness
    /// in it at all, so the highest number among the tracks that fit that moment won every
    /// time and a pack's two 1.05 daytime tracks were the whole daytime rotation.
    ///
    /// The sound assertions need a client, so everything here is [RequiresClient].
    /// </summary>
    public class PlaybackControl
    {
        // ---- selection --------------------------------------------------------

        /// <summary>
        /// The bug the players saw: two tracks nudged to 1.05 in a pack of otherwise
        /// default tracks were the only two that ever played. Before the fix this drew
        /// exactly those two, 500 times out of 500.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task SelectionSpreadsAcrossTracksOfEqualPriority()
        {
            await OnClient();

            var pack = new List<Engine.MusicTrack>();
            for (var i = 0; i < 20; i++)
            {
                pack.Add(Track("common" + i, 1f));
            }

            var favoured = new[] { Track("favoured-a", 1.05f), Track("favoured-b", 1.05f) };
            pack.AddRange(favoured);

            const int draws = 500;
            var winners = new Dictionary<string, int>();
            for (var i = 0; i < draws; i++)
            {
                var pick = Engine.TrackSelector.Select(pack);
                Assert.NotNull(pick, "a track was drawn");
                winners.TryGetValue(pick.Name, out var seen);
                winners[pick.Name] = seen + 1;
            }

            var favouredShare = favoured.Sum(t => winners.TryGetValue(t.Name, out var n) ? n : 0) / (double)draws;

            Log("distinct winners over " + draws + " draws: " + winners.Count +
                ", share taken by the two 1.05 tracks: " + favouredShare.ToString("P1"));

            Assert.Greater(winners.Count, 5, "distinct tracks drawn out of " + pack.Count);
            Assert.Less(favouredShare, 0.4, "share of draws taken by the two 1.05 tracks");
        }

        /// <summary>
        /// The other half of it: priority is a nudge, but a real one. A track lifted well
        /// clear of the roll - which is a gauss around 1 - still wins.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task AStronglyPreferredTrackStillWins()
        {
            await OnClient();

            var pack = new List<Engine.MusicTrack>();
            for (var i = 0; i < 20; i++)
            {
                pack.Add(Track("ordinary" + i, 1f));
            }

            var insistent = Track("insistent", 3f);
            pack.Add(insistent);

            var won = 0;
            for (var i = 0; i < 100; i++)
            {
                if (Engine.TrackSelector.Select(pack)?.Name == insistent.Name)
                {
                    won++;
                }
            }

            Assert.Equal(100, won, "draws won by the track at priority 3");
        }

        /// <summary>
        /// BeginSort rolls the wrapped track's start priority; selection reads it off the
        /// wrapper. Without the copy back the wrapper sorts on whatever the value was when
        /// it was built, which for the game's own music is never updated at all.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task WrappedTracksCarryTheirStartPriorityRoll()
        {
            await OnClient();

            var wrapped = VanillaTracks().OfType<SurfaceMusicTrack>().FirstOrDefault();
            Assert.NotNull(wrapped, "a vanilla surface track to wrap");

            var wrapper = new Engine.MusicTrackWrapper(wrapped);
            for (var i = 0; i < 10; i++)
            {
                wrapper.BeginSort();
                Assert.Equal(wrapped.StartPriority, wrapper.StartPriority,
                    "the wrapper carries the roll BeginSort just made");
            }

            Assert.GreaterOrEqual(Engine.TrackSelector.SelectionPriority(wrapper), wrapper.StartPriority,
                "and selection sees it");
        }

        // ---- cooldown ---------------------------------------------------------

        /// <summary>
        /// The cooldown was derived from the between-track pause table, whose top row is
        /// all zeroes because the highest music frequency means continuous music. That
        /// left no cooldown at all at that setting, so the tracks that won the draw were
        /// free to win it again immediately.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task TracksStayOnCooldownAtEveryMusicFrequency()
        {
            await OnClient();

            var playback = VS.MusicEngine.Playback;
            var cooldowns = Cooldowns();

            try
            {
                for (var frequency = 0; frequency <= 3; frequency++)
                {
                    playback.SetMusicFrequency(frequency);
                    Assert.Greater(cooldowns.CooldownDuration, 0L,
                        "track cooldown in ms at music frequency " + frequency);
                }

                playback.SetMusicFrequency(3);
                Assert.GreaterOrEqual(cooldowns.CooldownDuration, 8L * 60L * 1000L,
                    "cooldown at the highest music frequency is at least the game's own floor");
                Log("cooldown at 'very often': " + cooldowns.CooldownDuration / 1000 + "s");
            }
            finally
            {
                playback.SetMusicFrequency(ClientSettings.MusicFrequency);
            }
        }

        // ---- stopping and skipping --------------------------------------------

        /// <summary>
        /// Skipping is a change of track, not an addition. Nothing may start while the
        /// track being replaced is still audible - the fade is two seconds long, and the
        /// engine has to own it for that whole time.
        /// </summary>
        [VsTest(TimeoutMs = 240000), RequiresClient]
        public async Task NextWaitsForTheStoppedTrackToGoQuiet()
        {
            await OnClient();

            await WithMusicPlaying(async (engine, tracks) =>
            {
                var first = engine.CurrentMusicTrack;
                engine.NextTrack();

                Assert.Null(engine.CurrentMusicTrack,
                    "next does not start a track while the previous one is fading");
                Assert.True(engine.Playback.IsFadingOut,
                    "the stopped track is still owned while it fades");

                var mostAtOnce = 0;
                for (var i = 0; i < 900 && engine.CurrentMusicTrack?.IsPlaying != true; i++)
                {
                    await Ticks(1);
                    mostAtOnce = System.Math.Max(mostAtOnce, tracks.Count(t => t.IsPlaying));
                }

                Assert.NotNull(engine.CurrentMusicTrack, "a track follows the fade out");
                Assert.True(engine.CurrentMusicTrack.IsPlaying, "and it is playing");
                Assert.LessOrEqual(mostAtOnce, 1, "most tracks audible at once across the change");
                Assert.False(engine.Playback.IsFadingOut, "nothing is left fading");

                Log("skipped " + first.Name + " -> " + engine.CurrentMusicTrack.Name);
            });
        }

        /// <summary>
        /// .music stop has to hold silence even at the music frequency whose ordinary
        /// pause is zero seconds long, which is the setting it was borrowing.
        /// </summary>
        [VsTest(TimeoutMs = 240000), RequiresClient]
        public async Task StopHoldsSilenceAtTheHighestMusicFrequency()
        {
            await OnClient();

            await WithMusicPlaying(async (engine, tracks) =>
            {
                var playback = engine.Playback;
                playback.SetMusicFrequency(3);

                var seconds = playback.Stop();
                Assert.Greater(seconds, 60, "seconds of silence a manual stop holds");

                await Until(() => !tracks.Any(t => t.IsPlaying), 300, "the stopped track goes quiet");

                // Four seconds of engine ticks. The old behaviour started a new track on
                // the first one, a second after the stop.
                await Ticks(120);
                Assert.False(tracks.Any(t => t.IsPlaying), "nothing started while the stop was in force");
                Assert.Null(engine.CurrentMusicTrack, "and nothing was selected");
                Assert.True(playback.IsPaused, "the stop is still in force");

                // It is a pause, not a mute: next ends it.
                engine.NextTrack();
                await Until(() => engine.CurrentMusicTrack?.IsPlaying == true, 900,
                    "next ends the stop and starts a track");
                Log("stop held " + seconds + "s, next ended it");
            });
        }

        /// <summary>
        /// Once everything that fits has played, the engine goes round again rather than
        /// sitting in silence - but the track it just played is the one thing that must not
        /// come back first. A skip that plays the same song again is not a skip.
        ///
        /// Driven off a pool of two, so from the third skip on every selection is the
        /// round-again case and the rule makes the rotation strictly alternate. What is
        /// watched is every track the engine starts, not just the ones these skips ask
        /// for: a situation the assessor rates highly enough switches to a dynamic
        /// playlist, which skips a track of its own accord.
        /// </summary>
        [VsTest(TimeoutMs = 240000), RequiresClient]
        public async Task ASkipDoesNotHandBackTheTrackThatJustPlayed()
        {
            await OnClient();

            var engine = VS.MusicEngine;
            var curator = Curator(engine);
            var wasMusicLevel = ClientSettings.MusicLevel;

            try
            {
                var pool = VanillaTracks().OfType<SurfaceMusicTrack>().Take(2)
                    .Select(AlwaysPlayable).ToList();
                Assert.Equal(2, pool.Count, "tracks in the test pool");

                curator.Tracks = pool;
                ClientSettings.MusicLevel = 20;

                await Until(() => engine.Playback.CurrentPlaylist?.Tracks.Count == 2, 900,
                    "the curator picks up the test pool");

                // Every track the engine starts, in order, whoever asked for it.
                var played = new List<string>();
                Engine.MusicTrack watching = null;

                void Sample()
                {
                    var track = engine.CurrentMusicTrack;
                    if (track == watching)
                    {
                        return;
                    }

                    if (track != null)
                    {
                        played.Add(track.Name);
                    }

                    watching = track;
                }

                for (var skip = 1; skip <= 6; skip++)
                {
                    engine.NextTrack();

                    var playing = false;
                    for (var i = 0; i < 900 && !playing; i++)
                    {
                        await Ticks(1);
                        Sample();
                        playing = engine.CurrentMusicTrack?.IsPlaying == true;
                    }

                    Assert.True(playing, "a track is playing after skip " + skip);
                }

                Log("played through: " + string.Join(" -> ", played));

                Assert.GreaterOrEqual(played.Count, 6, "tracks started across six skips");
                for (var i = 1; i < played.Count; i++)
                {
                    Assert.NotEqual(played[i - 1], played[i],
                        "track " + (i + 1) + " of the rotation repeats the one before it");
                }
            }
            finally
            {
                engine.Playback.StopTrack(0f);
                ClientSettings.MusicLevel = wasMusicLevel;

                // Hand the pool back to the patch to refill, the way a config change does.
                curator.Tracks = new List<Engine.MusicTrack>();
            }
        }

        // ---- helpers ----------------------------------------------------------

        /// <summary>
        /// The same audio as a vanilla track, with everything that could exclude it opened
        /// up: any situation, any hour, no daylight needed. What is being tested is the
        /// order tracks come out in, not whether they fit the moment the box happens to be
        /// in.
        /// </summary>
        static Engine.MusicTrack AlwaysPlayable(SurfaceMusicTrack vanilla)
        {
            // Initialize rebuilds "music/<path>.ogg", so it wants the bare name back.
            var path = vanilla.Location.Path;
            path = path.Substring("music/".Length, path.Length - "music/".Length - ".ogg".Length);

            var track = new Engine.MusicTrack
            {
                Location = new AssetLocation(vanilla.Location.Domain, path),
                Situation = string.Join("|", System.Enum.GetNames(typeof(Situations.Situation))),
                MinSunlight = 0
            };

            track.Initialize(Capi.Assets, Capi, Vanilla());
            return track;
        }


        /// <summary>
        /// Runs the body with music playing, and puts the player's settings back afterwards.
        ///
        /// The pool is the vanilla audio opened up by <see cref="AlwaysPlayable"/>, not the
        /// game's tracks themselves: wrapped vanilla tracks now answer to the game's own
        /// rules - its start-up cooldown, its hour windows, its playlists - so whether one
        /// of them fits the moment the box happens to be in is the game's business, and
        /// these tests are about stopping and skipping. The mod's own pack is a 200MB
        /// download the test box need not have.
        /// </summary>
        static async Task WithMusicPlaying(System.Func<Engine.MusicEngine, List<Engine.MusicTrack>, Task> body)
        {
            var engine = VS.MusicEngine;
            var curator = Curator(engine);
            var wasMusicLevel = ClientSettings.MusicLevel;

            try
            {
                curator.Tracks = VanillaTracks().OfType<SurfaceMusicTrack>().Select(AlwaysPlayable).ToList();
                Assert.Greater(curator.Tracks.Count, 1, "tracks to choose between");

                ClientSettings.MusicLevel = 20;

                await Until(() => engine.Playback.CurrentPlaylist != null, 1500,
                    "the curator picks a playlist");

                engine.NextTrack();
                await Until(() => engine.CurrentMusicTrack?.IsPlaying == true, 900,
                    "a track is playing to start from");

                await body(engine, curator.Tracks);
            }
            finally
            {
                engine.Playback.StopTrack(0f);
                engine.Playback.SetMusicFrequency(ClientSettings.MusicFrequency);
                ClientSettings.MusicLevel = wasMusicLevel;

                // Hand the pool back to the patch to refill, the way a config change does.
                curator.Tracks = new List<Engine.MusicTrack>();
            }
        }

        /// <summary>
        /// A track the selector will accept: Initialize is what seeds the roll BeginSort
        /// makes, so a track that skipped it would sort on a constant and prove nothing.
        /// </summary>
        static Engine.MusicTrack Track(string name, float priority)
        {
            var track = new Engine.MusicTrack
            {
                Location = new AssetLocation("vstestpack", name),
                Priority = priority
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

        static Engine.TrackCooldownManager Cooldowns()
        {
            var cooldowns = typeof(Engine.MusicEngine)
                .GetField("trackCooldownManager", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(VS.MusicEngine) as Engine.TrackCooldownManager;
            Assert.NotNull(cooldowns, "track cooldown manager");
            return cooldowns;
        }

        static SystemMusicEngine Vanilla() =>
            VS.ClientMain.clientSystems.OfType<SystemMusicEngine>().First();

        static IMusicTrack[] VanillaTracks() =>
            typeof(SystemMusicEngine)
                .GetField("shuffledTracks", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(Vanilla()) as IMusicTrack[];
    }
}
