using System;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.Common;
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
    }
}
