using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using VS = VintageSymphony.VintageSymphony;

namespace VintageSymphony.Tests
{
    /// <summary>
    /// The parts of fact collection that were rewritten for cost: finding a playing
    /// resonator through the chunks' block entities instead of reading every block in
    /// range, and finding the nearest visible enemy with one raytrace instead of one per
    /// enemy. Both are cheaper only if they still give the same answers, which is what
    /// these are for.
    /// </summary>
    public class FactCollection
    {
        static Situations.Facts.SituationalFacts Facts => VS.MusicEngine.SituationAssessor.SituationalFacts;

        static float Score(Situations.Situation s) => VS.MusicEngine.SituationAssessor
            .Assessments.First(a => a.Situation == s).Score;

        /// <summary>
        /// The scan runs on its own once-a-second tick, so this waits for a pass rather
        /// than for the 300ms fact loop.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task APlayingResonatorIsFoundThroughTheChunkBlockEntities()
        {
            await Player.Teleport(P(8, 1, 8));
            await Ticks(20);

            Assert.True(float.IsPositiveInfinity(Facts.PlayingResonatorDistance),
                "no resonator to hear before one is placed");

            var at = P(13, 1, 8);
            World.SetBlock("game:resonator-north", at);

            // The client keeps its own block entity, and it is the client's chunks the
            // scan walks - so wait for it to arrive and start it playing there.
            await OnClient();
            BlockEntityResonator be = null;
            await Until(() => (be = Capi.World.BlockAccessor.GetBlockEntity(at) as BlockEntityResonator) != null,
                300, "the resonator reaches the client");

            // Silent so far: a resonator that is merely present must not count.
            await Ticks(90);
            Assert.True(float.IsPositiveInfinity(Facts.PlayingResonatorDistance),
                "a resonator sitting idle is not playing");

            be.IsPlaying = true;

            await Until(() => !float.IsPositiveInfinity(Facts.PlayingResonatorDistance), 300,
                "the playing resonator is found");
            Log("resonator at 5 blocks reported as " + Facts.PlayingResonatorDistance.ToString("0.0"));
            Assert.Close(Facts.PlayingResonatorDistance, 5.0, 1.5, "distance to the resonator");

            be.IsPlaying = false;
            await OnServer();
            World.SetBlock("game:air", at);
        }

        /// <summary>
        /// The nearest enemy and the nearest *visible* enemy are different facts, and the
        /// evaluators weigh them differently. Now that the search stops at the first
        /// enemy it can see, a nearer one behind a wall must still not be the visible one.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task AnEnemyBehindAWallIsNearestButNotVisible()
        {
            await Player.Teleport(P(8, 1, 8));
            await Ticks(10);

            // A wall two blocks east, spanning the plot so the drifter behind it cannot
            // walk around it, and tall enough that it cannot climb over either - drifters
            // have canClimb, and a four block wall let one over mid-test.
            World.Fill(P(10, 1, 0), P(10, 10, 15), "game:rock-granite");
            var hidden = World.SpawnEntity("game:drifter-normal", P(11, 1, 8));

            // And one further away in the open.
            var seen = World.SpawnEntity("game:drifter-normal", P(8, 1, 14));
            Assert.NotNull(hidden, "hidden drifter");
            Assert.NotNull(seen, "visible drifter");

            await Until(() => !float.IsPositiveInfinity(Facts.VisibleEnemyDistance), 400,
                "an enemy comes into view");

            Log("nearest=" + Facts.EnemyDistance.ToString("0.0") +
                " visible=" + Facts.VisibleEnemyDistance.ToString("0.0"));

            // The one behind the wall is nearer, so it owns EnemyDistance...
            Assert.Less(Facts.EnemyDistance, 5f, "nearest enemy is the one behind the wall");
            // ...but the visible distance has to reach past it to the one in the open. If
            // the search had stopped at the nearest enemy regardless of sight, these two
            // would be equal.
            Assert.Greater(Facts.VisibleEnemyDistance, Facts.EnemyDistance,
                "the wall keeps the nearer enemy out of sight");

            hidden.Die(Vintagestory.API.Common.EnumDespawnReason.Removed);
            seen.Die(Vintagestory.API.Common.EnumDespawnReason.Removed);
            await Ticks(20);
        }

        /// <summary>
        /// Situation scores are meant to ease toward their target - that is what the
        /// per-situation smoothing flags are for. The engine was handing the assessor
        /// milliseconds where it wanted seconds, which made the smoothing factor
        /// 1 - exp(-60), i.e. a plain assignment, so every score snapped in one step.
        ///
        /// Asserted as a shape rather than a curve: over a big move, no single step may
        /// account for most of it.
        /// </summary>
        [VsTest(TimeoutMs = 180000), RequiresClient]
        public async Task ScoresEaseTowardTheirTargetRatherThanSnapping()
        {
            await Player.Teleport(P(8, 1, 8));

            // Fresh worlds report a recent attack until the clock passes the window, and
            // that decays through the scores; wait it out so the only thing moving is the
            // drifter.
            await Until(() => Facts.SecondsSinceLastAttack > 15f, 900, "startup transient decays");
            await Ticks(30);

            var samples = new List<float> { Score(Situations.Situation.Calm) };
            var drifter = World.SpawnEntity("game:drifter-normal", P(10, 1, 8));
            Assert.NotNull(drifter, "drifter");

            for (int i = 0; i < 30; i++)
            {
                await Ticks(6);
                samples.Add(Score(Situations.Situation.Calm));
            }

            drifter.Die(Vintagestory.API.Common.EnumDespawnReason.Removed);

            var total = System.Math.Abs(samples[^1] - samples[0]);
            var biggestStep = Enumerable.Range(1, samples.Count - 1)
                .Max(i => System.Math.Abs(samples[i] - samples[i - 1]));

            Log("calm " + samples[0].ToString("0.00") + " -> " + samples[^1].ToString("0.00") +
                " (moved " + total.ToString("0.00") + ", biggest single step " + biggestStep.ToString("0.00") + ")");

            Assert.Greater(total, 0.1f, "the score moved enough to judge");
            Assert.Less(biggestStep, total * 0.5f, "no single step carries most of the move");
        }
    }
}
