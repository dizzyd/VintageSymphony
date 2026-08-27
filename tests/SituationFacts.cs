using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using VS = VintageSymphony.VintageSymphony;

namespace VintageSymphony.Tests
{
    /// <summary>
    /// SituationalFactsCollector is the other half that only fails in-world: it
    /// identifies enemies by entity code prefix, learns "home" from bed block codes,
    /// walks blocks looking for resonators and raytraces for line of sight. Every one
    /// of those is a string or an asset shape the game is free to move between
    /// versions, and none of it is checked by the compiler.
    ///
    /// The scores themselves are deliberately not asserted to exact values - the
    /// evaluators blend and smooth, and pinning numbers here would just be a change
    /// detector. What matters is that a fact moves at all when the world changes.
    /// </summary>
    public class SituationFacts
    {
        static Situations.Scoring.SituationAssessor Assessor => VS.MusicEngine.SituationAssessor;
        static Situations.Facts.SituationalFacts Facts => Assessor.SituationalFacts;

        static float Score(Situations.Situation s) =>
            Assessor.Assessments.First(a => a.Situation == s).Score;

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task ADrifterNearbyIsSeenAsAnEnemy()
        {
            await Player.Teleport(P(8, 1, 8));
            await Ticks(10);

            // No enemies yet: distance is positive infinity, not merely large.
            await Until(() => float.IsPositiveInfinity(Facts.EnemyDistance), 200,
                "no enemy before spawning one");

            var drifter = World.SpawnEntity("game:drifter-normal", P(10, 1, 8));
            Assert.NotNull(drifter, "drifter spawned");
            await Ticks(20);

            await Until(() => Facts.EnemyDistance < 20f, 300, "enemy distance drops");
            Log("enemy distance " + Facts.EnemyDistance.ToString("0.0") +
                ", visible " + Facts.VisibleEnemyDistance.ToString("0.0") +
                ", danger " + Score(Situations.Situation.Danger).ToString("0.00"));

            // Line of sight has to resolve too, but it is sampled rather than
            // guaranteed: IsEntityVisible raytraces to the entity's *feet*, so a ray
            // from eye height to a drifter standing on the ground two blocks away can
            // clip the soil in front and read as blocked. Poll instead of sampling
            // once - what is being tested is that the raytrace path works at all.
            await Until(() => Facts.VisibleEnemyDistance < 20f, 300, "enemy comes into view");
            Log("visible at " + Facts.VisibleEnemyDistance.ToString("0.0"));

            drifter.Die(Vintagestory.API.Common.EnumDespawnReason.Removed);
            await Ticks(20);
        }

        /// <summary>
        /// Home is learned from the client's BlockChanged event by matching bed codes
        /// - PathStartsWith("bed") plus a "-feet-" segment. 1.22 beds are still
        /// bed-{material}-{part}-{side}, and this is what proves it.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task PlacingABedFootRegistersAHome()
        {
            var homes = VS.Instance.AttributeStorage.PlayerHomeLocations;
            var before = homes.Count;

            var bed = P(6, 1, 6);
            World.SetBlock("game:bed-wood-feet-north", bed);
            await Ticks(20);

            Log("home locations: " + before + " -> " + homes.Count);
            Assert.Greater(homes.Count, before, "home locations after placing a bed foot");
            Assert.True(homes.Any(v => v.X == bed.X && v.Y == bed.Y && v.Z == bed.Z),
                "the bed position was recorded");

            await Until(() => !float.IsPositiveInfinity(Facts.DistanceFromHome), 200,
                "distance from home becomes finite");
            Log("distance from home " + Facts.DistanceFromHome.ToString("0.0"));
        }

        /// <summary>
        /// Sunlight reaches the mod as ClientMain.playerProperties.sunSlight, which
        /// the game recomputes from the client's own light map every 20ms. Roofing the
        /// player over and relighting is what proves the mod is reading live light and
        /// not a stale copy.
        ///
        /// It does not get all the way to cave darkness: skylight spreads sideways one
        /// level per block, so a roof that fits inside a 16-wide plot always leaks. The
        /// drop is the assertion; CaveEvaluatorScoresDarkDepth covers the rest.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task RoofingThePlayerDropsTheSunlightFact()
        {
            await Player.Teleport(P(8, 1, 8));
            await Ticks(20);

            var openSun = Facts.SunLevel;
            Assert.Greater(openSun, 10f, "sunlight out in the open");

            World.Fill(P(4, 3, 4), P(12, 14, 12), "game:rock-granite");
            // A bulk SetBlock does not relight, and without that the client's light map
            // keeps reporting open sky however much rock is overhead.
            Vs.Sapi.WorldManager.FullRelight(P(2, 0, 2), P(14, 20, 14));
            await Ticks(60);

            await Until(() => Facts.SunLevel < openSun, 300, "sunlight falls under the rock");
            Log("sun " + openSun.ToString("0.0") + " -> " + Facts.SunLevel.ToString("0.0"));
        }

        /// <summary>
        /// The other half of the cave path, off the world: a flat test world cannot
        /// produce real cave darkness, so feed the evaluator the facts a cave would
        /// produce and check the mapping still holds.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task CaveEvaluatorScoresDarkDepth()
        {
            await OnClient();
            var evaluator = new Situations.Evaluator.CaveEvaluator();

            var inACave = new Situations.Facts.SituationalFacts
            {
                DistanceToSurface = 30f,
                SunLevel = 0f,
                DistanceFromHome = float.PositiveInfinity,
            };
            var outdoors = new Situations.Facts.SituationalFacts
            {
                DistanceToSurface = 0f,
                SunLevel = 22f,
                DistanceFromHome = float.PositiveInfinity,
            };

            Assert.Greater(evaluator.Evaluate(Situations.Situation.Cave, inACave), 0.9f,
                "cave score deep underground in the dark");
            Assert.Equal(0f, evaluator.Evaluate(Situations.Situation.Cave, outdoors),
                "cave score standing in daylight");

            // A cave next to the player's bed is home, not a cave.
            var underTheHouse = inACave;
            underTheHouse.DistanceFromHome = 10f;
            Assert.Equal(0f, evaluator.Evaluate(Situations.Situation.Cave, underTheHouse),
                "cave score right under home");
        }
    }
}
