using System.Linq;
using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1200 (the one new test, per CC_AUTONOMY's testing policy): the City grows from one area
    /// (Market Street) to a full 30-area sequential route — labyrinths, set pieces, 58 relays
    /// (authored as <see cref="WorldReplicator"/>, World 2's own machinery) that must all be destroyed
    /// before gate g29 opens into the new exit area, a30. Drives the real loader against the shipped
    /// <c>city_config.json</c>; every assertion below reads a RESOLVED value (Rule 2, Tier 2) — never
    /// an authored constant restated as itself.
    ///
    /// Fails on the base commit (before this ticket's swap): <c>Assert.AreEqual(30, cfg.dials.areaCount)</c>
    /// fails with "Expected: 30 But was: 1".
    /// </summary>
    public sealed class MV1200CityThirtyAreasTests
    {
        [Test]
        public void CityGrowsToThirtyAreasWithFiftyEightRelaysGatingTheExit()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(cfg, "the City's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);
            Assert.IsTrue(MapValidation.Validate(map, out string validateReason), validateReason);

            Assert.AreEqual(30, cfg.dials.areaCount);

            WorldArea a30 = cfg.AreaByIndex(30);
            Assert.IsNotNull(a30, "AreaByIndex(30) must resolve");
            Assert.AreEqual("a30", a30.id);
            Assert.IsTrue(a30.IsExitRole, "a30 must be exit-role");

            int totalReplicators = cfg.areas.Sum(a => a.replicators?.Length ?? 0);
            Assert.AreEqual(58, totalReplicators, "the City must author exactly 58 replicators across its areas");

            WorldGate gateIntoA30 = System.Array.Find(cfg.gates, g => g.to != null && g.to.area == "a30");
            Assert.IsNotNull(gateIntoA30, "there must be a gate whose 'to' is a30");
            Assert.IsTrue(GateCondition.TryParse(gateIntoA30.opensWith, out GateCondition condition, out string reason), reason);
            Assert.AreEqual(GateConditionKind.ReplicatorsDestroyed, condition.Kind);
            Assert.IsTrue(condition.ReplicatorsAll, "the gate into a30 must require every replicator destroyed");

            int cityIndex = WorldCatalog.IndexOf(WorldIds.City);
            Assert.AreNotEqual(-1, cityIndex, "WorldCatalog must carry a row for WorldIds.City");
            Assert.AreEqual(0.5f, WorldCatalog.Get(cityIndex).ExitDoorPos, 0.0001f);
        }
    }
}
