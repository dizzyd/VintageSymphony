using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using VS = VintageSymphony.VintageSymphony;

namespace VintageSymphony.Tests
{
    /// <summary>
    /// The request behind this: someone who builds underground keeps and gets cave music
    /// in them. The room fact comes from the game's own room registry - a flood fill
    /// from the player's feet that the cellar and greenhouse code also lean on - and
    /// the surface it is measured against is the worldgen terrain height, which
    /// building never moves. Both are read on the client, and neither is checked by
    /// the compiler, so this is where they get proved.
    ///
    /// The plot's ground block is P(x, 0, z), and it sits at the worldgen surface, so
    /// negative y here is genuinely under the terrain.
    /// </summary>
    public class Rooms
    {
        static Situations.Scoring.SituationAssessor Assessor => VS.MusicEngine.SituationAssessor;
        static Situations.Facts.SituationalFacts Facts => Assessor.SituationalFacts;

        static float Score(Situations.Situation s) =>
            Assessor.Assessments.First(a => a.Situation == s).Score;

        static readonly Situations.Situation Cave = Situations.Situation.Cave;
        static readonly Situations.Situation Keep = Situations.Situation.Keep;

        static string Scores() =>
            "cave " + Score(Cave).ToString("0.00") + ", keep " + Score(Keep).ToString("0.00") +
            ", exits " + Facts.RoomExitCount + ", surface " + Facts.DistanceToSurface.ToString("0.0");

        /// <summary>
        /// A sealed granite room ten blocks down. Cave has nothing to say in it and Keep
        /// is certain. Then a tunnel out of it turns it back into a hole in the ground.
        /// </summary>
        [VsTest(TimeoutMs = 180000), RequiresClient]
        public async Task ASealedRoomUnderTheGroundIsAKeepNotACave()
        {
            // The flat world's ground is two blocks deep; below that there is no world
            // to dig a room into. Standard worldgen has a hundred.
            if (P(0, 0, 0).Y < 20)
            {
                Skip("needs real terrain: VSTK_PLAYSTYLE=vstestkit-standard in its own --slot");
            }

            World.Fill(P(5, -11, 5), P(11, -5, 11), "game:rock-granite");
            World.Fill(P(6, -10, 6), P(10, -6, 10), "game:air");

            // Let the client have the granite before standing on it: teleported onto
            // blocks it has not received yet, the player falls straight through them.
            await Ticks(40);
            var floor = P(8, -10, 8);
            await Player.Teleport(floor);
            await Ticks(20);
            Log("standing at " + Player.Me.Entity.Pos.AsBlockPos + " for " + floor);
            Assert.Close(Player.Me.Entity.Pos.Y, floor.Y, 1.5, "the player stays on the room floor");

            await Until(() => Facts.IsInEnclosedRoom, 400, "the room registry finds the sealed room");
            Log("inside: " + Scores());
            // Ten under the plot's ground - give or take the lie of the land, since the
            // plot's origin is its corner and the terrain rolls a few blocks across it.
            Assert.Greater(Facts.DistanceToSurface, 6f, "the room is under the terrain");

            // Cave drops the tick the room is seen; Keep eases up, so it gets a wait.
            await Until(() => Score(Cave) < 0.05f, 300, "Cave gives up inside a sealed room");
            await Until(() => Score(Keep) > 0.9f, 600, "Keep becomes certain");
            Log("settled: " + Scores());

            // A tunnel out of the east wall, longer than the fourteen blocks the registry
            // will call one room. Sideways rather than up: a shaft to the sky lights the
            // room, and cave music wants the dark as much as it wants the depth.
            World.Fill(P(11, -10, 8), P(28, -9, 8), "game:air");

            await Until(() => !Facts.IsInEnclosedRoom, 600, "the tunnel counts as a way out");
            Log("opened: " + Scores());
            await Until(() => Score(Keep) < 0.1f, 600, "Keep fades once the room is open");
            await Until(() => Score(Cave) > 0.3f, 300, "Cave comes back in an open hole");
            Log("cave again: " + Scores());
        }

        /// <summary>
        /// The same sealed box built on the surface. Enclosed, so not a cave - but it
        /// is not under anything, so not a keep either. A house is a house.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task ASealedHouseOnTheSurfaceIsNotAKeep()
        {
            World.Fill(P(6, 1, 6), P(10, 5, 10), "game:rock-granite");
            World.Fill(P(7, 1, 7), P(9, 4, 9), "game:air");
            await Player.Teleport(P(8, 1, 8));
            await Ticks(20);

            await Until(() => Facts.IsInEnclosedRoom, 400, "the room registry finds the house");
            Log("inside: " + Scores());
            Assert.Less(Facts.DistanceToSurface, 1f, "the floor is at the terrain, not under it");

            await Ticks(100);
            Log("later: " + Scores());
            Assert.Less(Score(Keep), 0.05f, "Keep in a house on the surface");
            Assert.Less(Score(Cave), 0.05f, "Cave in a house on the surface");
        }
    }
}
