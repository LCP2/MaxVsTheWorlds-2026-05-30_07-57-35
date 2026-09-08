using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>MV-735: <c>MapValidation</c>'s doorway check compares a RESOLVED hole length against
    /// the exact <c>MinDoorway</c> floor with no tolerance, so a gate authored exactly at the floor
    /// (every one of World 2's 23 gates) can flip pass/fail on a recompile — observed live as gate
    /// <c>g21</c> measuring 2.99999619 m in one Unity batchmode process and exactly 3.0 m in another,
    /// same input. Fails on base commit d9b885c: a resolved hole of 2.999996 m (MinDoorway - 0.000004,
    /// the observed case) is rejected as "under 3 m", when it should read as the same doorway as an
    /// exact 3 m.</summary>
    public sealed class MV735DoorwayEpsilonTests
    {
        private static MapData TwoRoomWorld(float doorway)
        {
            return new MapData
            {
                zones = new[]
                {
                    new MapZone { id = "roomA", type = "entry", x = 0f, z = 0f, width = 6f, depth = 6f },
                    new MapZone { id = "roomB", type = "entry", x = 6f, z = 0f, width = 6f, depth = 6f },
                },
                links = new[]
                {
                    new MapLink { from = "roomA", to = "roomB", doorway = doorway },
                },
                entities = new[]
                {
                    new MapEntity { id = "spawn", kind = "playerspawn", x = 0f, z = 0f },
                },
            };
        }

        [Test]
        public void ResolvedHole_AHairUnderTheFloor_StillPasses()
        {
            // The observed case: a gate authored at the 3 m floor resolves a hole a hair short of
            // 3.0 m due to float rounding in ResolveDoorPosition's two-sided average. Must pass —
            // the doorway IS 3 m, the resolution just cannot land on it exactly.
            MapData map = TwoRoomWorld(MapValidation.MinDoorway - 0.000004f);

            Assert.IsTrue(MapValidation.Validate(map, out string reason), reason);
        }

        [Test]
        public void ResolvedHole_GenuinelyNarrow_StillFailsAndNamesTheDoorway()
        {
            // A doorway that is actually too narrow (half a metre under the floor, not a rounding
            // hair) must still fail — the epsilon must not swallow a real violation.
            MapData map = TwoRoomWorld(MapValidation.MinDoorway - 0.5f);

            Assert.IsFalse(MapValidation.Validate(map, out string reason));
            Assert.IsTrue(reason.Contains("roomA") && reason.Contains("roomB"),
                $"expected the failure to name the doorway between 'roomA' and 'roomB', got: {reason}");
        }
    }
}
