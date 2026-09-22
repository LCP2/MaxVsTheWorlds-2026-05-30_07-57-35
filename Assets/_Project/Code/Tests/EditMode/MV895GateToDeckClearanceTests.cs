using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-895 — Lee, playing the live build, could not walk north from a11's (Valve Loft) and a13's
    /// (Trolley Yard) own west floor gate. Builds World 2 through the real
    /// <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/>/<see cref="StormdrainDressing"/> path
    /// (testing policy Tier 2: RESOLVED geometry, not the authored JSON) and asserts a continuous 2 m
    /// gap, clear of every enabled non-trigger static collider, running from each of a10/a11/a12/a13's
    /// own floor entry-gate mouth north to that area's deck edge — the exact corridor Max tried to
    /// walk. The entry gate's own leaf/threshold colliders are excluded (whether a gate's hinge swing
    /// clears its own doorway is <c>GateSolidityTests</c>'/<c>AreaGateTests</c>' concern, not this
    /// one — this test is about what blocks Max once past a gate that already works as designed). Also
    /// writes every collider whose bounds fall inside a11's and a13's own footprints, with world-space
    /// bounds, to <c>Logs/mv895_a11_a13_colliders.txt</c> (AC1's report).
    ///
    /// Fails on base commit b034639: the ticket's own deck-edge-beam hypothesis is disproven by this
    /// data — every deck edge/parapet/beam/post this build constructs for a10-a13/a15 sits between
    /// Y=2.35 and Y=3.5 (see the report), never touching the Y=0-1.8 band a CharacterController walks
    /// in. a10, a12 and a13 instead fail on ordinary GROUND-LEVEL authored cover sitting inside the 2 m
    /// lane (a10_cover1/a10_cover2/a10_rep1; a12_cover4; a13_cover3/a13_cover4 — see the fix comment for
    /// the exact quoted failure and why this ticket could not just move them). a11 passes clean — no
    /// static collider blocks its own corridor at all.
    /// </summary>
    public sealed class MV895GateToDeckClearanceTests
    {
        private const float RequiredClearance = 2f;
        private const float CapsuleTop = 1.8f; // Max's CharacterController top (Backyard_Slice.unity: height 1.6, center 0.8)

        [Test]
        public void WestGateMouthToDeckEdgeStaysTwoMetresClearOfStaticColliders()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            WorldArea entry = cfg.areas.First(a => a.IsEntryRole);
            int areaCount = cfg.dials.areaCount;
            string entryZoneId = (entry.index >= 1 && entry.index <= areaCount) ? $"area{entry.index}" : entry.id;

            var root = new GameObject("MV895 World2 Root").transform;
            var failures = new List<string>();
            var report = new List<string>();
            try
            {
                MapBuild built = MapRuntime.Build(map, root);
                StormdrainDressing.Dress(root, map, built.Cover); // MV-831: dressing can add colliders authored data never shows
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                List<Collider> colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
                    .Where(c => c != null && c.enabled && !c.isTrigger)
                    .ToList();

                foreach (int index in new[] { 10, 11, 12, 13 })
                {
                    string zoneId = $"area{index}";
                    MapZone zone = map.Zone(zoneId);
                    Assert.IsNotNull(zone, $"MV-895: no zone for area index {index} ('{zoneId}')");

                    Vector2? mouth = EntryMouth(map, zoneId, entryZoneId, areaCount);
                    Assert.IsTrue(mouth.HasValue, $"MV-895: area '{zoneId}' has no floor gate leading in from a lower-order area");

                    MapEntity deckEntity = map.entities.FirstOrDefault(e => e != null && e.Kind == EntityKind.Deck && $"a{index}_deck1" == e.id);
                    Assert.IsNotNull(deckEntity, $"MV-895: area '{zoneId}' has no deck entity 'a{index}_deck1'");
                    float deckSouthZ = deckEntity.z - deckEntity.depth * 0.5f;

                    float bandXMin = mouth.Value.x - RequiredClearance * 0.5f;
                    float bandXMax = mouth.Value.x + RequiredClearance * 0.5f;
                    float bandZMin = mouth.Value.y;
                    float bandZMax = deckSouthZ;
                    if (bandZMax <= bandZMin) continue; // the gate mouth is already at/past the deck edge

                    List<Collider> blockers = colliders.Where(c =>
                    {
                        Bounds b = c.bounds;
                        if (b.max.x < bandXMin || b.min.x > bandXMax) return false;
                        if (b.max.z < bandZMin || b.min.z > bandZMax) return false;
                        if (b.min.y >= CapsuleTop || b.max.y <= 0f) return false; // above Max's head or below the floor
                        if (c.GetComponent<StructuralWall>() != null) return false; // the area's own boundary wall, not this corridor
                        if (c.gameObject.name == "Map Floor") return false;
                        // The entry gate's own leaf/threshold: whether its hinge swing clears the doorway
                        // is GateSolidityTests'/AreaGateTests' own concern, not this ticket's — MV-895 is
                        // about what blocks Max ONCE PAST a gate that already works as designed.
                        if (c.GetComponent<AreaGate>() != null) return false;
                        if (c.gameObject.name.EndsWith(" (Threshold)", System.StringComparison.Ordinal)) return false;
                        return true;
                    }).ToList();

                    if (blockers.Count > 0)
                        failures.Add($"area '{zoneId}': blocked between its own entry mouth ({mouth.Value.x:0.##}, {mouth.Value.y:0.##}) " +
                                     $"and deck edge z={deckSouthZ:0.##} by " +
                                     string.Join(", ", blockers.Select(b => $"{b.gameObject.name} [{b.GetType().Name}] {b.bounds.min:F2}..{b.bounds.max:F2}")));
                }

                // AC1: full collider report for a11 and a13, world-space bounds.
                foreach (int index in new[] { 11, 13 })
                {
                    MapZone zone = map.Zone($"area{index}");
                    if (zone == null) continue;
                    report.Add($"--- area{index} ({zone.Footprint}) ---");
                    foreach (Collider c in colliders
                        .Where(c => zone.Footprint.Overlaps(new Rect(c.bounds.min.x, c.bounds.min.z, c.bounds.size.x, c.bounds.size.z)))
                        .OrderBy(c => c.gameObject.name))
                    {
                        report.Add($"{c.gameObject.name} [{c.GetType().Name}] {c.bounds.min:F2}..{c.bounds.max:F2}");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }

            string dir = Path.Combine(Application.dataPath, "..", "Logs");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "mv895_a11_a13_colliders.txt"), report);

            Assert.IsTrue(failures.Count == 0, "MV-895 west-gate-to-deck clearance failures:\n" + string.Join("\n", failures));
        }

        /// <summary>Mirrors MV-875's own in-gate resolution: the floor (level-0) link leading into this
        /// zone from a lower-order area, resolved to its doorway mouth. For a11/a12/a13 that is their own
        /// west wall; a10's own entry is its south wall (it has no west floor gate) — the same "clear
        /// corridor from wherever Max actually enters" contract still applies.</summary>
        private static Vector2? EntryMouth(MapData map, string zoneId, string entryZoneId, int areaCount)
        {
            foreach (MapLink link in map.links)
            {
                if (link == null || (link.from != zoneId && link.to != zoneId)) continue;

                MapEntity gate = map.Entity(link.gate);
                if (gate == null || gate.level > 0) continue; // a [DECK] gate is sealed below deck height, not a floor doorway

                if (!MapGeometry.Doorway(map, link, out bool alongX, out float coord, out Span hole)) continue;

                string other = link.from == zoneId ? link.to : link.from;
                if (OrderRank(other, entryZoneId, areaCount) >= OrderRank(zoneId, entryZoneId, areaCount)) continue;

                return alongX ? new Vector2(hole.Mid, coord) : new Vector2(coord, hole.Mid);
            }
            return null;
        }

        private static int OrderRank(string zoneId, string entryZoneId, int areaCount)
        {
            if (zoneId == entryZoneId) return 0;
            if (zoneId.StartsWith("area", System.StringComparison.Ordinal) &&
                int.TryParse(zoneId.Substring(4), out int n) && n >= 1 && n <= areaCount)
                return n;
            return int.MaxValue;
        }
    }
}
