using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using VintageSymphony.Situations;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using VS = VintageSymphony.VintageSymphony;

namespace VintageSymphony.Tests
{
    /// <summary>
    /// The report that prompted this: a player indoors, a drifter outside, and the music
    /// swapping between combat and peaceful every few seconds at the highest music
    /// frequency. Two things were wrong. The curator followed the scores the instant they
    /// crossed, and the scores themselves saw no walls - a drifter running at the stone
    /// from outside read as an attack.
    /// </summary>
    public class PlaylistSwitching
    {
        static Situations.Scoring.SituationAssessor Assessor => VS.MusicEngine.SituationAssessor;
        static Situations.Facts.SituationalFacts Facts => Assessor.SituationalFacts;

        static float WeightedScore(Situations.Situation s) =>
            Assessor.Assessments.First(a => a.Situation == s).WeightedScore;

        // ---- the gate ---------------------------------------------------------

        static readonly Situations.Situation Fight = Situations.Situation.Fight;
        static readonly Situations.Situation Idle = Situations.Situation.Idle;
        static readonly Situations.Situation Danger = Situations.Situation.Danger;

        /// <summary>
        /// Pure logic on a fake clock. Entering a playlist takes a clear lead held for
        /// the dwell, and a lapse in the lead starts the dwell over. Idle is not a
        /// dynamic situation, so nothing here waits on a minimum play time.
        /// </summary>
        [VsTest]
        public async Task EnteringAPlaylistTakesAClearLeadForTheDwell()
        {
            await Ticks(1);
            long now = 100_000L;
            var gate = new Engine.PlaylistSwitchGate(() => now);

            Assert.True(gate.Allows(null, Fight, 0f, null), "anything may start when nothing is selected");
            Assert.False(gate.Allows(Idle, Idle, 0f, now), "no change wanted");

            Assert.False(gate.Allows(Idle, Fight, 0.3f, now), "the tick the lead appears");
            now += 2_000L;
            Assert.False(gate.Allows(Idle, Fight, 0.3f, now), "2s into the dwell");
            now += 1_000L;
            Assert.True(gate.Allows(Idle, Fight, 0.3f, now), "the dwell is over");

            // A lapse - the score dipped for a tick - starts the dwell over.
            Assert.False(gate.Allows(Idle, Fight, 0.3f, now), "a new lead");
            now += 2_000L;
            Assert.False(gate.Allows(Idle, Fight, 0f, now), "the lead lapses");
            Assert.False(gate.Allows(Idle, Fight, 0.3f, now), "and is back");
            now += 2_000L;
            Assert.False(gate.Allows(Idle, Fight, 0.3f, now), "2s into the restarted dwell");
            now += 1_000L;
            Assert.True(gate.Allows(Idle, Fight, 0.3f, now), "the restarted dwell is over");

            Assert.True(gate.Allows(Fight, Situations.Situation.Dead, 19f, now), "death interrupts at once");
        }

        /// <summary>
        /// The hold that stops the flip-flop: a combat track plays for at least thirty
        /// seconds however clearly something else leads.
        /// </summary>
        [VsTest]
        public async Task CombatMusicPlaysForItsMinimumHoweverClearTheLead()
        {
            await Ticks(1);
            long now = 100_000L;
            var gate = new Engine.PlaylistSwitchGate(() => now);
            var fightStarted = now;

            Assert.False(gate.Allows(Fight, Idle, 0.3f, fightStarted), "the tick the lead appears");
            now += 3_000L;
            Assert.False(gate.Allows(Fight, Idle, 0.3f, fightStarted), "dwell over, track 3s into a 30s minimum");
            now += 10_000L;
            Assert.False(gate.Allows(Fight, Idle, 0.3f, fightStarted), "13s in");
            now += 17_000L;
            Assert.True(gate.Allows(Fight, Idle, 0.3f, fightStarted), "30s in");
        }

        /// <summary>
        /// The exit the first version of the gate did not have. A drifter loitering three
        /// blocks outside the wall after a fight leaves Danger ahead of Fight by about
        /// 0.13 - under the margin - and that held combat music for as long as the
        /// drifter stayed. A lead of any size that persists for ten seconds now counts.
        /// </summary>
        [VsTest]
        public async Task APersistentLeadUnderTheMarginStillEndsCombat()
        {
            await Ticks(1);
            long now = 100_000L;
            var gate = new Engine.PlaylistSwitchGate(() => now);
            var fightStarted = now - 60_000L;
            const float thinLead = 0.13f;

            Assert.False(gate.Allows(Fight, Danger, thinLead, fightStarted), "the tick the lead appears");
            now += 3_000L;
            Assert.False(gate.Allows(Fight, Danger, thinLead, fightStarted), "3s: no margin, so no dwell");
            now += 6_000L;
            Assert.False(gate.Allows(Fight, Danger, thinLead, fightStarted), "9s of a thin lead");
            now += 1_000L;
            Assert.True(gate.Allows(Fight, Danger, thinLead, fightStarted), "10s of a thin lead");

            // The persistence is of the lead, not the leader: trailing Danger and then
            // Idle is still trailing. And a tick without any lead starts it over.
            Assert.False(gate.Allows(Fight, Danger, thinLead, fightStarted), "a new thin lead");
            now += 6_000L;
            Assert.False(gate.Allows(Fight, Idle, 0.05f, fightStarted), "6s, now behind Idle");
            now += 4_000L;
            Assert.True(gate.Allows(Fight, Idle, 0.05f, fightStarted), "10s behind one thing or another");

            Assert.False(gate.Allows(Fight, Danger, thinLead, fightStarted), "a new thin lead");
            now += 9_000L;
            Assert.False(gate.Allows(Fight, Danger, 0f, fightStarted), "9s, then Fight draws level");
            now += 1_000L;
            Assert.False(gate.Allows(Fight, Danger, thinLead, fightStarted), "a fresh lead starts the count over");
        }

        // ---- curator and playback together ------------------------------------

        /// <summary>
        /// The whole path from ranking to sounding track, on a fake clock and hand-set
        /// scores, for the case the isolated pieces could not show. A pack with fight
        /// music and no danger music; a fight is over, the player is indoors, a drifter
        /// loiters outside. Danger leads at 0.525 but has nothing to play, Idle trails at
        /// 0.075 with music, Fight has fallen to zero. Selection used to look only within
        /// 0.2 of the top and come back empty, which left the Fight playlist in place and
        /// combat music running for as long as the drifter stayed.
        /// </summary>
        [VsTest(TimeoutMs = 240000), RequiresClient]
        public async Task ObsoleteCombatMusicGivesWayWhenTheLeaderHasNoMusic()
        {
            await OnClient();

            var wasMusicLevel = ClientSettings.MusicLevel;
            long now = 1_000_000L;

            var vanilla = VanillaSurfaceTracks().Take(2).ToList();
            Assert.Equal(2, vanilla.Count, "vanilla tracks to borrow audio from");
            var fightTrack = OpenedUp(vanilla[0], "fight");
            var idleTrack = OpenedUp(vanilla[1], "idle");

            var playback = new Engine.Playback(Capi.Logger, new Engine.TrackCooldownManager(() => now),
                () => VS.ClientMain.playerProperties, () => now);
            playback.SetMusicFrequency(3);

            var ranked = System.Enum.GetValues<Situations.Situation>()
                .Select(s => new Situations.Scoring.SituationAssessment(s, 0f)).ToList();
            void Score(Situations.Situation s, float weighted)
            {
                var a = ranked.First(x => x.Situation == s);
                a.Score = weighted / s.Attributes().Weight;
                ranked = ranked.OrderByDescending(x => x.WeightedScore).ToList();
            }

            var curator = new Engine.MusicCurator(Capi, () => ranked, playback, () => now);
            curator.Tracks = new List<Engine.MusicTrack> { fightTrack, idleTrack };

            try
            {
                ClientSettings.MusicLevel = 20;

                // The fight.
                Score(Fight, 1.0f);
                curator.Update(1f);
                playback.Update(1f);
                Assert.NotNull(playback.CurrentPlaylist, "a playlist during the fight");
                Assert.Equal(Fight, playback.CurrentPlaylist.Situation, "the playlist during the fight");
                await Until(() => fightTrack.IsPlaying, 300, "the combat track sounds");

                // Indoors, drifter outside, no danger music in the pack.
                Score(Fight, 0f);
                Score(Danger, 0.525f);
                Score(Idle, 0.075f);

                var switchedAt = -1;
                for (var second = 1; second <= 45 && switchedAt < 0; second++)
                {
                    now += 1_000L;
                    curator.Update(1f);
                    playback.Update(1f);
                    await Ticks(1);
                    if (playback.CurrentPlaylist?.Situation == Idle)
                    {
                        switchedAt = second;
                    }
                }

                Log("combat gave way to the Idle playlist after " + switchedAt + "s");
                Assert.Greater(switchedAt, 0, "the Idle playlist was selected within 45s");
                Assert.GreaterOrEqual(switchedAt, 30, "not before the combat track's 30s minimum");
                Assert.LessOrEqual(switchedAt, 32, "and not long after it");

                // The combat track fades over two real seconds and the fade's deadline is
                // on the fake clock, so from here the clock runs at the speed of the ticks -
                // a clock that raced ahead would trip the engine's "fade did not finish"
                // safety net and cut the track instead of letting it fade.
                for (var i = 0; i < 400 && !(idleTrack.IsPlaying && !fightTrack.IsPlaying); i++)
                {
                    now += 50L;
                    playback.Update(0.05f);
                    await Ticks(1);
                }

                Assert.True(idleTrack.IsPlaying, "an Idle track is sounding");
                Assert.False(fightTrack.IsPlaying, "and the combat track is not");
            }
            finally
            {
                playback.StopTrack(0f);
                ClientSettings.MusicLevel = wasMusicLevel;
            }
        }

        /// <summary>
        /// The same audio as a vanilla track, opened up to one situation and nothing else
        /// that could exclude it. Initialize rebuilds "music/&lt;path&gt;.ogg", so it wants
        /// the bare name back.
        /// </summary>
        static Engine.MusicTrack OpenedUp(SurfaceMusicTrack vanilla, string situation)
        {
            var path = vanilla.Location.Path;
            path = path.Substring("music/".Length, path.Length - "music/".Length - ".ogg".Length);

            var track = new Engine.MusicTrack
            {
                Location = new AssetLocation(vanilla.Location.Domain, path),
                Situation = situation,
                MinSunlight = 0
            };

            track.Initialize(Capi.Assets, Capi, VanillaEngine());
            return track;
        }

        static SystemMusicEngine VanillaEngine() =>
            VS.ClientMain.clientSystems.OfType<SystemMusicEngine>().First();

        static IEnumerable<SurfaceMusicTrack> VanillaSurfaceTracks() =>
            (typeof(SystemMusicEngine)
                .GetField("shuffledTracks", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(VanillaEngine()) as IMusicTrack[] ?? new IMusicTrack[0])
            .OfType<SurfaceMusicTrack>();

        // ---- the facts --------------------------------------------------------

        /// <summary>
        /// A sealed stone room with the player in it and a drifter outside. Whatever the
        /// drifter does out there - and what it does is run at the wall - the player
        /// cannot see it, so nothing it does may count as an attack and Fight stays
        /// below the calm situations. Before the fix the run animation alone put Fight
        /// at 0.8 against Idle's 0.55.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task AnEnemyBehindAWallIsNotAFight()
        {
            World.Fill(P(6, 1, 6), P(10, 5, 10), "game:rock-granite");
            World.Fill(P(7, 1, 7), P(9, 4, 9), "game:air");
            await Player.Teleport(P(8, 1, 8));
            await Ticks(10);

            await Until(() => float.IsPositiveInfinity(Facts.EnemyDistance), 200,
                "no enemy before spawning one");

            var drifter = World.SpawnEntity("game:drifter-normal", P(14, 1, 8));
            Assert.NotNull(drifter, "drifter spawned");
            try
            {
                await Until(() => Facts.EnemyDistance < 15f, 300, "the drifter is in range");

                // Neither of these may be fresh: the teleport into the room used to read
                // as a wound, and the attack timer used to count from the world's start.
                Log("at the start: last damage " + Facts.SecondsSinceLastDamage.ToString("0.0") +
                    "s ago, last attack " + Facts.SecondsSinceLastAttack.ToString("0.0") + "s ago");
                Assert.Greater(Facts.SecondsSinceLastDamage, 60f, "seconds since the player was last hurt");

                // Fifteen seconds of it doing what drifters do outside a house.
                var worstFight = 0f;
                var closest = float.PositiveInfinity;
                var closestVisible = float.PositiveInfinity;
                var freshestAttack = float.PositiveInfinity;
                for (var i = 0; i < 30; i++)
                {
                    await Ticks(10);
                    worstFight = Math.Max(worstFight, WeightedScore(Situations.Situation.Fight));
                    closest = Math.Min(closest, Facts.EnemyDistance);
                    closestVisible = Math.Min(closestVisible, Facts.VisibleEnemyDistance);
                    freshestAttack = Math.Min(freshestAttack, Facts.SecondsSinceLastAttack);
                }

                Log("drifter came within " + closest.ToString("0.0") +
                    ", closest in sight " + closestVisible +
                    ", most recent attack " + freshestAttack.ToString("0.0") + "s ago" +
                    ", Fight peaked at " + worstFight.ToString("0.00") +
                    " against Idle " + WeightedScore(Situations.Situation.Idle).ToString("0.00") +
                    " and Danger " + WeightedScore(Situations.Situation.Danger).ToString("0.00"));

                Assert.Less(closest, 15f, "the drifter stayed in range");
                Assert.True(float.IsPositiveInfinity(closestVisible), "an enemy behind stone is never in sight");
                Assert.Less(worstFight, 0.5f, "Fight's weighted score with a drifter outside the wall");
            }
            finally
            {
                drifter.Die(EnumDespawnReason.Removed);
                await Ticks(20);
            }
        }

        /// <summary>
        /// The report's own picture: a window, a weapon in hand, a drifter right outside.
        /// Line of sight is blocked by the glass, but the first fix still let the distance
        /// term and the weapon term score Fight at 0.59 here - ahead of Danger. Neither
        /// may count without an enemy in sight, so Fight has nothing to score from.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task AnArmedPlayerBehindAWindowIsNotFighting()
        {
            World.Fill(P(6, 1, 6), P(10, 5, 10), "game:rock-granite");
            World.Fill(P(7, 1, 7), P(9, 4, 9), "game:air");
            World.Fill(P(10, 1, 7), P(10, 4, 9), "game:glass-plain");
            await Player.Teleport(P(8, 1, 8));
            await Player.Hold("game:club-generic-wood");
            await Ticks(10);

            await Until(() => Facts.IsHoldingWeapon, 200, "the club reads as a weapon");
            await Until(() => float.IsPositiveInfinity(Facts.EnemyDistance), 200,
                "no enemy before spawning one");

            var drifter = World.SpawnEntity("game:drifter-normal", P(11, 1, 8));
            Assert.NotNull(drifter, "drifter spawned");
            try
            {
                await Until(() => Facts.EnemyDistance < 6f, 300, "the drifter is at the window");

                // The first samples can still carry a score smoothed down from an earlier
                // test, so the strict reading starts five seconds in.
                var worstFight = 0f;
                var settledFight = 0f;
                var closest = float.PositiveInfinity;
                var closestVisible = float.PositiveInfinity;
                for (var i = 0; i < 30; i++)
                {
                    await Ticks(10);
                    var fight = WeightedScore(Fight);
                    worstFight = Math.Max(worstFight, fight);
                    if (i >= 10)
                    {
                        settledFight = Math.Max(settledFight, fight);
                    }
                    closest = Math.Min(closest, Facts.EnemyDistance);
                    closestVisible = Math.Min(closestVisible, Facts.VisibleEnemyDistance);
                }

                Log("drifter came within " + closest.ToString("0.0") +
                    ", closest in sight " + closestVisible +
                    ", Fight peaked at " + worstFight.ToString("0.00") +
                    " and at " + settledFight.ToString("0.00") + " once settled, against Danger " +
                    WeightedScore(Danger).ToString("0.00"));

                Assert.Less(closest, 6f, "the drifter stayed at the window");
                Assert.True(float.IsPositiveInfinity(closestVisible), "an enemy behind glass is never in sight");
                Assert.Less(settledFight, 0.1f, "Fight's weighted score, armed, with a drifter outside the window");
            }
            finally
            {
                drifter.Die(EnumDespawnReason.Removed);
                var hand = Player.Me.InventoryManager.ActiveHotbarSlot;
                hand.Itemstack = null;
                hand.MarkDirty();
                await Ticks(20);
            }
        }

    }
}
