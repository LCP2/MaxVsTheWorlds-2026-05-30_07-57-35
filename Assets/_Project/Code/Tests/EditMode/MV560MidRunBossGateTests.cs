using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-560: a boss partway through a run (areas 12/20 in the level design, not just the final area)
    /// was structurally impossible to author — <see cref="MapValidation"/>'s old boss-gate rule demanded
    /// <c>all-sheds-destroyed</c> on EVERY gate touching a boss area, entry and exit alike, which would
    /// both make a mid-run boss unreachable (the entry would wait on sheds beyond it the player could
    /// never have destroyed) and lock the player in (the exit would carry the same never-satisfied
    /// condition). This test pins the fix's two edges: a mid-run boss entered by the new
    /// <c>sheds-destroyed-before</c> condition and exited by an ordinary combat gate now validates
    /// (AC2), while a boss entry gate authored with no shed condition at all still fails, with the same
    /// message shape as before (AC3).
    /// </summary>
    public sealed class MV560MidRunBossGateTests
    {
        /// <summary>Entry stub / shed area / mid-run boss / final area, four in a line — the smallest
        /// world that can even ask the question "does a boss NOT at the end validate". <paramref
        /// name="entryOpensWith"/> is the condition authored on the gate INTO the boss area;
        /// <paramref name="exitOpensWith"/> is authored on the gate OUT of it.</summary>
        private static WorldConfig MidRunBossWorld(string entryOpensWith, string exitOpensWith) => new WorldConfig
        {
            world = "Test World",
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", index = 0, role = "entry",
                    origin = new WorldAreaOrigin { x = -2f, z = -6f },
                    size = new WorldAreaSize { w = 4f, d = 6f },
                },
                new WorldArea
                {
                    id = "a1", index = 1, role = "shed", hasShed = true,
                    origin = new WorldAreaOrigin { x = -15f, z = 0f },
                    size = new WorldAreaSize { w = 30f, d = 30f },
                    sheds = new[] { new WorldShed { x = 0f, z = 15f } },
                },
                new WorldArea
                {
                    id = "midboss", index = 2, role = "boss",
                    origin = new WorldAreaOrigin { x = -15f, z = 30f },
                    size = new WorldAreaSize { w = 30f, d = 20f },
                },
                new WorldArea
                {
                    id = "a2", index = 3, role = "exit",
                    origin = new WorldAreaOrigin { x = -15f, z = 50f },
                    size = new WorldAreaSize { w = 30f, d = 20f },
                },
            },
            gates = new[]
            {
                new WorldGate
                {
                    id = "g0", width = 3f, opensWith = "start",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                },
                new WorldGate
                {
                    id = "g1", width = 3f, opensWith = entryOpensWith,
                    from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "midboss", wall = "S", pos = 0.5f },
                },
                new WorldGate
                {
                    id = "g2", width = 3f, opensWith = exitOpensWith,
                    from = new WorldGateEndpoint { area = "midboss", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a2", wall = "S", pos = 0.5f },
                },
            },
        };

        [Test]
        public void MidRunBoss_ShedsDestroyedBeforeEntry_Validates_ButNoShedConditionAtAllStillFails()
        {
            // AC2: the entry gate uses the new condition, the exit gate is an ordinary combat gate — the
            // old rule rejected BOTH (it demanded all-sheds-destroyed on every touching gate), which is
            // exactly why a mid-run boss could never be authored before this fix.
            bool ok = WorldMapLoader.TryLoad(MidRunBossWorld("sheds-destroyed-before", "primary"),
                                              out MapData map, out string loadReason);
            Assert.IsTrue(ok, loadReason);
            Assert.IsNotNull(map);

            // AC3: an entry gate authored with an ordinary (non-shed) condition must still fail, with
            // the rule's message naming both accepted conditions.
            bool rejected = MapValidation.ValidateWorldConfig(MidRunBossWorld("primary", "primary"), out string reason);

            Assert.IsFalse(rejected, "a boss entry gate with no shed condition at all must fail validation");
            StringAssert.Contains("all-sheds-destroyed", reason);
            StringAssert.Contains("sheds-destroyed-before", reason);
        }
    }
}
