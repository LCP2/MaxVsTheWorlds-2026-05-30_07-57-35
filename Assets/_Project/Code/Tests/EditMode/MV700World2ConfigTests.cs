using System;
using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-700: world2_config.json goes from the MV-687/696 placeholder (3 areas — stub/a1/a23, 2 gates,
    /// no replicators, no overlays, boss id "big_bermuda") to World 2's full drawn level
    /// (world2_config.DESIGN.json): areas plus the entry stub, Replicators, condition-gated
    /// replicator gates, deck/floor overlay pairs sharing a resolved footprint, and the Wet
    /// Well's Sludgequeen. Fails on the MV-696 merge commit (699ee7d) — the placeholder there resolves
    /// 3 areas, 0 replicators, no condition-gated/boss gates, no overlays and boss id "big_bermuda".
    ///
    /// Counts updated by MV-852 (World 2 re-layout): a7 and a13 were deleted outright (24/25/three
    /// overlay pairs -> 22/23/two), and the replicator-gated door tested is now g31 (g12 was removed).
    /// </summary>
    public sealed class MV700World2ConfigTests
    {
        [Test]
        public void World2Config_ResolvesFullDesign_ReplicatorGatesOverlaysAndBoss()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "world2_config.json failed to load");

            // MV-852 deleted a7 (Silt Beds) and a13 (The Weir deck) outright, and their Replicators
            // with them — 24 areas/25 Replicators (MV-700) drop to 22/23.
            Assert.AreEqual(22, cfg.areas.Length, "World 2 authors 21 areas plus the entry stub");
            Assert.AreEqual(23, cfg.dials.areaCount);

            int totalReplicators = 0;
            foreach (WorldArea a in cfg.areas) totalReplicators += a.replicators?.Length ?? 0;
            Assert.AreEqual(23, totalReplicators, "World 2 authors 23 Replicators across its areas");

            WorldGate g31 = Array.Find(cfg.gates, g => g.id == "g31");
            WorldGate g24 = Array.Find(cfg.gates, g => g.id == "g24");
            Assert.IsNotNull(g31, "gate g31 not found");
            Assert.IsNotNull(g24, "gate g24 not found");
            Assert.IsTrue(GateCondition.TryParse(g31.opensWith, out GateCondition g31Condition, out string g31Reason), g31Reason);
            Assert.IsTrue(GateCondition.TryParse(g24.opensWith, out GateCondition g24Condition, out string g24Reason), g24Reason);
            Assert.AreEqual(GateConditionKind.ReplicatorsDestroyed, g31Condition.Kind,
                "g31 (MV-852's Replicator door into a12) must be gated on replicators-destroyed");
            Assert.AreEqual(GateConditionKind.ReplicatorsDestroyed, g24Condition.Kind,
                "g24 must be gated on replicators-destroyed");

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);
            Assert.IsTrue(MapValidation.Validate(map, out string mapReason), mapReason);

            // MV-852 deleted a13 (the third overlay pair, a13/a11) along with a11's own deck.
            foreach (var (overlayId, targetId) in new[] { ("a19", "a3"), ("a15", "a6") })
            {
                WorldArea overlay = cfg.Area(overlayId);
                WorldArea target = cfg.Area(targetId);
                Assert.IsNotNull(overlay, $"area '{overlayId}' not found");
                Assert.IsNotNull(target, $"area '{targetId}' not found");
                Assert.AreEqual(targetId, overlay.overlays, $"'{overlayId}' must overlay '{targetId}'");

                // Combat areas 1..dials.areaCount are renamed "area<N>" for their MapZone id
                // (WorldMapLoader.TryLoad) — the same convention every other zone-lookup test in this
                // suite uses (e.g. MapTests, BackyardSkyTests), not the DESIGN file's own "aN" id.
                MapZone overlayZone = map.Zone($"area{overlay.index}");
                MapZone targetZone = map.Zone($"area{target.index}");
                Assert.IsNotNull(overlayZone, $"zone 'area{overlay.index}' ('{overlayId}') was never built");
                Assert.IsNotNull(targetZone, $"zone 'area{target.index}' ('{targetId}') was never built");
                Assert.AreEqual(targetZone.Footprint, overlayZone.Footprint,
                    $"'{overlayId}' must share its resolved footprint with '{targetId}'");
            }

            WorldArea a23 = cfg.Area("a23");
            Assert.IsNotNull(a23, "area 'a23' not found");
            WorldBoss[] bosses = a23.Bosses();
            Assert.AreEqual(1, bosses.Length, "a23 must author exactly one boss");
            Assert.AreEqual("sludgequeen", bosses[0].id, "a23's boss must be the Sludgequeen");
        }
    }
}
