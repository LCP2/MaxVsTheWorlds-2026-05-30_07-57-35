using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1308 (the one new test, per CC_AUTONOMY's testing policy), rewritten by MV-1418 when Lee's
    /// V5.1 City map (23 Nov 2026) replaced the old pylon rules: a pylon may now stand far from its
    /// relay, or in a different area. Feeders are counted per relay across ALL areas' pylons - 1 per
    /// relay for a2-a12, 2 for a13-a29 (98 total). In the area a pylon stands in, its centre is at
    /// least 0.85 m from every cover rect (1.4 m footprint + 0.15 m gap), at least 1.6 m from every
    /// other pylon, and at least 2 m from that area's gates. The old 6-10 m ring check is gone.
    ///
    /// Real entry points: the actual city_config.json via WorldLibrary. Tier 2 (Rule 2): every
    /// assertion reads a measured distance or count, never an authored constant echoed back.
    /// // Guards MV-1308
    /// </summary>
    public sealed class MV1308CityPylonPlacementTests
    {
        private const float MinPylonSeparation = 1.6f;
        private const float MinPylonCoverDistance = 0.85f;
        private const float MinGateClearance = 2f;
        private const float Tolerance = 0.001f;

        [Test]
        public void CityPylons_SatisfyFeederCountsAndClearanceRules()
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

            var feedersByRelay = new Dictionary<string, int>();
            foreach (WorldArea area in city.areas)
                foreach (WorldPylon p in area.pylons ?? Array.Empty<WorldPylon>())
                {
                    if (area.index >= 2 && area.index <= 29) totalPylons++; // a30's own four are MV-1309's
                    feedersByRelay.TryGetValue(p.feeds ?? "", out int n);
                    feedersByRelay[p.feeds ?? ""] = n + 1;
                }

            foreach (WorldArea area in city.areas)
            {
                if (area.index < 2 || area.index > 29) continue; // a1/stub feed nothing; a30 is MV-1309's own

                int expectedPerRelay = area.index <= 12 ? 1 : 2;
                foreach (WorldReplicator relay in area.replicators ?? Array.Empty<WorldReplicator>())
                {
                    feedersByRelay.TryGetValue(relay.id, out int count);
                    if (count != expectedPerRelay)
                        violations.Add($"'{relay.id}' has {count} pylon(s), expected {expectedPerRelay}");
                }

                WorldPylon[] pylons = area.pylons ?? Array.Empty<WorldPylon>();
                foreach (WorldPylon p in pylons)
                {
                    foreach (WorldCover c in area.cover ?? Array.Empty<WorldCover>())
                    {
                        float dist = RectPointDistance(c.x - c.width * 0.5f, c.x + c.width * 0.5f,
                            c.z - c.depth * 0.5f, c.z + c.depth * 0.5f, p.x, p.z);
                        if (dist < MinPylonCoverDistance - Tolerance)
                            violations.Add($"'{p.id}' is {dist:0.###} m from cover '{c.id}', under {MinPylonCoverDistance} m");
                    }

                    foreach (Vector2 gatePoint in GatePointsFor(area.id, areasById, gatesByFromArea, gatesByToArea))
                    {
                        float dist = Vector2.Distance(new Vector2(p.x, p.z), gatePoint);
                        if (dist < MinGateClearance - Tolerance)
                            violations.Add($"'{p.id}' is {dist:0.###} m from a gate, under {MinGateClearance} m");
                    }
                }

                for (int i = 0; i < pylons.Length; i++)
                for (int j = i + 1; j < pylons.Length; j++)
                {
                    float dist = Vector2.Distance(new Vector2(pylons[i].x, pylons[i].z),
                        new Vector2(pylons[j].x, pylons[j].z));
                    if (dist < MinPylonSeparation - Tolerance)
                        violations.Add($"'{pylons[i].id}' and '{pylons[j].id}' are {dist:0.###} m apart, under {MinPylonSeparation} m");
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
