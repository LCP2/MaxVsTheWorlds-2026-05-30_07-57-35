using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1308 (the one new test, per CC_AUTONOMY's testing policy): the City's a2-a29 relays now
    /// each author the feeding pylon(s) MV-1294's own Pylon/WorldPylon machinery needs — 1 per relay
    /// for a2-a12, 2 for a13-a29 (98 total, matching MV1315PylonClearanceRuleTests' own derivation),
    /// each 6-10 m from its relay and clear of every cover piece/gate by
    /// <see cref="MapValidation.RequiredPylonCoverClearance"/> (2.0 m, or 1.85 m for the named
    /// a5_rep1 exemption, MV-1315) and clear of every other pylon by 3 m.
    ///
    /// Fails on 800bae5 (the commit before this ticket, MV-1315's own HEAD): city_config.json's
    /// a2-a29 author no "pylons" at all, so every relay's feeder count reads 0 against the required
    /// 1 or 2. See the fix comment for the captured failure output.
    ///
    /// Real entry points throughout: the actual city_config.json, loaded through the real
    /// <see cref="WorldLibrary"/>, same technique <c>MV1315PylonClearanceRuleTests</c> already uses
    /// for this same file — no built <see cref="MapRuntime"/>/scene needed, since every value this
    /// test reads (an area's own origin/size/cover/replicators/pylons/gates) is already resolved,
    /// loaded JSON data, not an authored constant echoed back at itself.
    ///
    /// ONE new test (MV-465 Rule 1). Tier 2 (Rule 2): every assertion reads a resolved value — a
    /// measured ring distance, a measured footprint-edge/point clearance, a measured pylon-to-pylon
    /// separation — never a bare authored constant, never a rendered pixel.
    /// // Guards MV-1308
    /// </summary>
    public sealed class MV1308CityPylonPlacementTests
    {
        private const float RingMin = 6f;
        private const float RingMax = 10f;
        private const float MinPylonSeparation = 3f;
        private const float Tolerance = 0.001f;

        [Test]
        public void CityPylons_SatisfyFeederCountsRingAndClearanceRules()
        {
            WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(city, "city_config.json failed to load — see the error log above.");

            var areasById = new Dictionary<string, WorldArea>();
            foreach (WorldArea a in city.areas) areasById[a.id] = a;

            var gatesByFromArea = new Dictionary<string, WorldGate>();
            var gatesByToArea = new Dictionary<string, WorldGate>();
            foreach (WorldGate g in city.gates)
            {
                gatesByFromArea[g.from.area] = g;
                gatesByToArea[g.to.area] = g;
            }

            var violations = new List<string>();
            int totalPylons = 0;

            foreach (WorldArea area in city.areas)
            {
                if (area.index < 2 || area.index > 29) continue; // a1/stub feed nothing; a30 is MV-1309's own

                int expectedPerRelay = area.index <= 12 ? 1 : 2;
                WorldReplicator[] relays = area.replicators ?? Array.Empty<WorldReplicator>();
                WorldPylon[] pylons = area.pylons ?? Array.Empty<WorldPylon>();
                totalPylons += pylons.Length;

                var byFeeds = pylons.GroupBy(p => p.feeds).ToDictionary(g => g.Key, g => g.ToList());

                foreach (WorldReplicator relay in relays)
                {
                    List<WorldPylon> feeders = byFeeds.TryGetValue(relay.id, out List<WorldPylon> list)
                        ? list : new List<WorldPylon>();
                    if (feeders.Count != expectedPerRelay)
                    {
                        violations.Add($"'{relay.id}' has {feeders.Count} pylon(s), expected {expectedPerRelay}");
                        continue;
                    }

                    float requiredClearance = MapValidation.RequiredPylonCoverClearance(relay.id);

                    foreach (WorldPylon p in feeders)
                    {
                        float ring = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(relay.x, relay.z));
                        if (ring < RingMin - Tolerance || ring > RingMax + Tolerance)
                            violations.Add($"'{p.id}' is {ring:0.###} m from '{relay.id}', outside " +
                                           $"[{RingMin}, {RingMax}] m");

                        foreach (WorldCover c in area.cover ?? Array.Empty<WorldCover>())
                        {
                            float dist = RectPointDistance(c.x - c.width * 0.5f, c.x + c.width * 0.5f,
                                c.z - c.depth * 0.5f, c.z + c.depth * 0.5f, p.x, p.z);
                            if (dist < requiredClearance - Tolerance)
                                violations.Add($"'{p.id}' is {dist:0.###} m from cover '{c.id}', under " +
                                               $"{requiredClearance} m");
                        }

                        foreach (Vector2 gatePoint in GatePointsFor(area.id, areasById, gatesByFromArea, gatesByToArea))
                        {
                            float dist = Vector2.Distance(new Vector2(p.x, p.z), gatePoint);
                            if (dist < requiredClearance - Tolerance)
                                violations.Add($"'{p.id}' is {dist:0.###} m from a gate, under {requiredClearance} m");
                        }
                    }
                }

                for (int i = 0; i < pylons.Length; i++)
                for (int j = i + 1; j < pylons.Length; j++)
                {
                    float dist = Vector2.Distance(new Vector2(pylons[i].x, pylons[i].z),
                        new Vector2(pylons[j].x, pylons[j].z));
                    if (dist < MinPylonSeparation - Tolerance)
                        violations.Add($"'{pylons[i].id}' and '{pylons[j].id}' are {dist:0.###} m apart, under " +
                                       $"{MinPylonSeparation} m");
                }
            }

            Assert.AreEqual(98, totalPylons, "MV-1308's 98-pylon figure no longer matches city_config.json");
            Assert.IsTrue(violations.Count == 0, string.Join("; ", violations));
        }

        private static float RectPointDistance(float xMin, float xMax, float zMin, float zMax, float px, float pz)
        {
            float dx = Mathf.Max(0f, Mathf.Max(xMin - px, px - xMax));
            float dz = Mathf.Max(0f, Mathf.Max(zMin - pz, pz - zMax));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>The world point of this area's own entry gate (if any) and exit gate (if any) —
        /// mirrors <c>WorldMapLoader.ResolveDoorPosition</c>'s own averaged-fraction formula via
        /// <see cref="WorldArea.WallSpan"/>/<see cref="WorldArea.WallCoord"/>, since that method
        /// itself is private.</summary>
        private static IEnumerable<Vector2> GatePointsFor(string areaId, Dictionary<string, WorldArea> areasById,
            Dictionary<string, WorldGate> gatesByFromArea, Dictionary<string, WorldGate> gatesByToArea)
        {
            if (gatesByToArea.TryGetValue(areaId, out WorldGate entry)) yield return ResolveGatePoint(areasById, entry);
            if (gatesByFromArea.TryGetValue(areaId, out WorldGate exit)) yield return ResolveGatePoint(areasById, exit);
        }

        private static Vector2 ResolveGatePoint(Dictionary<string, WorldArea> areasById, WorldGate g)
        {
            WorldArea fromArea = areasById[g.from.area];
            WorldArea toArea = areasById[g.to.area];
            WallEnums.TryParse(g.from.wall, out Wall fromWall);
            WallEnums.TryParse(g.to.wall, out Wall toWall);

            Span fromSpan = fromArea.WallSpan(fromWall);
            Span toSpan = toArea.WallSpan(toWall);
            float posFrom = fromSpan.Min + Mathf.Clamp01(g.from.pos) * fromSpan.Length;
            float posTo = toSpan.Min + Mathf.Clamp01(g.to.pos) * toSpan.Length;
            float along = (posFrom + posTo) * 0.5f;
            float fixedCoord = fromArea.WallCoord(fromWall);

            return fromArea.WallRunsAlongX(fromWall) ? new Vector2(along, fixedCoord) : new Vector2(fixedCoord, along);
        }
    }
}
