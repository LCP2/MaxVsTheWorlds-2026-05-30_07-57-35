using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1140 (the one new test, per CC_AUTONOMY's testing policy): World 2 grows by ten areas
    /// (a22-a31, approved by Lee 7 Oct 2026, design review D6) and the finale — final arena, boss and
    /// exit door — moves from a21 to the new a31. Drives the real loader/builder on the shipped
    /// <c>world2_config.json</c>; every assertion below reads a RESOLVED value (Rule 2, Tier 2), never
    /// an authored constant.
    ///
    /// Fails on base commit 574e63d (before this ticket's additions were applied):
    /// <c>Assert.AreEqual(31, cfg.dials.areaCount)</c> fails with "Expected: 31 But was: 21".
    /// </summary>
    public sealed class MV1140World2TenAreasTests
    {
        [Test]
        public void World2GrowsToA31_FinaleAndExitMoveFromA21()
        {
            // Same BuildBody collider-strip [Error] noise every full-World2-build EditMode test in this
            // suite carries (see other World2 EditMode tests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            Assert.AreEqual(31, cfg.dials.areaCount);

            WorldArea a31 = cfg.AreaByIndex(31);
            Assert.IsNotNull(a31, "AreaByIndex(31) must resolve");
            Assert.AreEqual("a31", a31.id);
            Assert.IsTrue(a31.IsBossRole, "a31 must be boss-role");
            WorldBoss[] a31Bosses = a31.Bosses();
            Assert.AreEqual(1, a31Bosses.Length, "a31 must author exactly one boss");
            Assert.AreEqual("sludgequeen", a31Bosses[0].id);
            Assert.AreEqual(340f, a31Bosses[0].x, 0.01f);
            Assert.AreEqual(48f, a31Bosses[0].z, 0.01f);

            WorldArea a21 = cfg.Area("a21");
            Assert.IsNotNull(a21, "a21 must still exist");
            Assert.IsFalse(a21.IsBossRole, "a21 must lose its boss role to a31");
            Assert.AreEqual(0, a21.Bosses().Length, "a21 must author no boss");

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV1140 Map Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);

                Assert.AreEqual(1, built.Bosses.Count, "World 2 must build exactly one boss actor");
                MonoBehaviour boss = built.Bosses[0];
                MapZone bossZone = map.ZoneAt(boss.transform.position.x, boss.transform.position.z);
                Assert.IsNotNull(bossZone, "the boss actor must stand inside some built zone");
                Assert.AreEqual(31, bossZone.AreaIndex, "the boss actor must stand in a31's own zone");
            }
            finally
            {
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }

            WorldTransitionEntry entry = WorldTransitions.For(1);
            Assert.IsNotNull(entry, "World 2 must author a WorldTransitions row");
            Vector2 doorMouth = entry.ExitDoorMouth(cfg);
            Assert.AreEqual(352f, doorMouth.x, 0.01f, "World 2's exit door mouth X");
            Assert.AreEqual(48.5f, doorMouth.y, 0.01f, "World 2's exit door mouth Z");

            WorldGate g24 = System.Array.Find(cfg.gates, g => g.id == "g24");
            Assert.IsNotNull(g24, "gate g24 not found");
            Assert.IsTrue(GateCondition.TryParse(g24.opensWith, out GateCondition g24Condition, out string g24Reason), g24Reason);
            Assert.AreEqual(GateConditionKind.ReplicatorsDestroyed, g24Condition.Kind);
            Assert.IsFalse(g24Condition.ReplicatorsAll, "g24 must now be an explicit list, not 'all'");
            foreach (string excluded in new[] { "a23", "a24", "a26", "a27", "a28", "a30" })
                Assert.IsFalse(System.Linq.Enumerable.Contains(g24Condition.ReplicatorAreaIds, excluded),
                    $"g24's list must not contain '{excluded}'");

            WorldGate g47 = System.Array.Find(cfg.gates, g => g.id == "g47");
            Assert.IsNotNull(g47, "gate g47 not found");
            Assert.IsTrue(GateCondition.TryParse(g47.opensWith, out GateCondition g47Condition, out string g47Reason), g47Reason);
            Assert.AreEqual(GateConditionKind.ReplicatorsDestroyed, g47Condition.Kind);
            Assert.IsTrue(g47Condition.ReplicatorsAll, "g47 must be the all-replicators form");
        }
    }
}
