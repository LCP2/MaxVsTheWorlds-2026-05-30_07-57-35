using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1479: one Parts Cache per Reef and Mothership area that has a garrison, placed with the City's stronghold
    /// clearances (cover 0.8 m, robots and canisters 1.4 m, pylons 1.6 m, Rocket Crate 2 m) and reachable from the area's
    /// entry gates (0.5 m Max radius walk grid, as MV-1463). One test (Rule 1): on the pre-change main neither world
    /// authors partsCaches, so every area reports zero caches. Tier 2: resolved map entities, not authored constants.
    /// </summary>
    public sealed class MV1479ReefMothershipCachePlacementTests
    {
        private const float Cell = 0.1f;
        private const float MaxRadius = 0.5f;

        [Test]
        public void ReefAndMothership_EveryGarrisonedAreaHasExactlyOneClearReachablePartsCache()
        {
            var failures = new List<string>();
            foreach (string key in new[] { WorldLibrary.World3, WorldLibrary.Mothership })
            {
                WorldConfig cfg = WorldLibrary.Load(key);
                Assert.IsNotNull(cfg, key + " failed to load");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);
                Assert.IsTrue(MapValidation.Validate(map, out string valReason), valReason);

                foreach (WorldArea area in cfg.areas)
                {
                    if (area.garrison == null || area.garrison.Length == 0) continue;
                    string tag = key + " " + area.id;
                    var caches = map.entities.Where(e => e != null && e.Kind == EntityKind.PartsCache && e.id.StartsWith(area.id + "_")).ToList();
                    if (caches.Count != 1)
                    {
                        failures.Add(tag + ": " + caches.Count + " Parts Caches (expected exactly 1)");
                        continue;
                    }
                    Vector2 pos = caches[0].CenterXz;
                    string zoneId = (area.index >= 1 && area.index <= cfg.dials.areaCount) ? "area" + area.index : area.id;
                    MapZone zone = map.Zone(zoneId);
                    if (!zone.Footprint.Contains(pos)) failures.Add(tag + ": cache " + pos + " is outside the area");

                    foreach (MapEntity e in map.entities)
                    {
                        if (e == null) continue;
                        if (e.Kind == EntityKind.Cover && zone.Footprint.Overlaps(e.ToCover().Footprint) && e.ToCover().DistanceTo(pos) < 0.8f)
                            failures.Add(tag + ": cache within 0.8 m of cover " + e.id);
                        if (e.Kind == EntityKind.Canister && Vector2.Distance(e.CenterXz, pos) < 1.4f)
                            failures.Add(tag + ": cache within 1.4 m of canister " + e.id);
                        if (e.Kind == EntityKind.Pylon && Vector2.Distance(e.CenterXz, pos) < 1.6f)
                            failures.Add(tag + ": cache within 1.6 m of pylon " + e.id);
                        if (e.Kind == EntityKind.RocketCrate && Vector2.Distance(e.CenterXz, pos) < 2f)
                            failures.Add(tag + ": cache within 2 m of Rocket Crate " + e.id);
                    }
                    foreach (WorldGarrisonEntry g in area.garrison)
                        if (g != null && Vector2.Distance(new Vector2(g.x, g.z), pos) < 1.4f)
                            failures.Add(tag + ": cache within 1.4 m of robot " + g.kind);

                    if (!Reachable(map, zone, pos))
                        failures.Add(tag + ": cache at " + pos + " is not reachable from the area's entry gate");
                }
            }
            Assert.IsEmpty(failures, "MV-1479 Parts Cache placement failures:\n" + string.Join("\n", failures));
        }

        private static bool Reachable(MapData map, MapZone zone, Vector2 target)
        {
            var holes = new Dictionary<Wall, List<Span>>();
            var mouths = new List<Vector2>();
            foreach (MapLink link in map.links)
            {
                if (link == null || (link.from != zone.id && link.to != zone.id)) continue;
                MapEntity gate = map.Entity(link.gate);
                if (gate == null || gate.level > 0) continue;
                if (!MapGeometry.Doorway(map, link, out bool alongX, out float coord, out Span hole)) continue;
                Wall wall = alongX ? (Mathf.Abs(coord - zone.ZMin) <= 0.05f ? Wall.S : Wall.N)
                                   : (Mathf.Abs(coord - zone.XMin) <= 0.05f ? Wall.W : Wall.E);
                if (!holes.TryGetValue(wall, out List<Span> spans)) holes[wall] = spans = new List<Span>();
                spans.Add(hole);
                mouths.Add(alongX ? new Vector2(hole.Mid, coord) : new Vector2(coord, hole.Mid));
            }
            var blockers = new List<ArenaCover>();
            foreach (MapEntity e in map.entities)
            {
                if (e == null || (e.Kind != EntityKind.Cover && e.Kind != EntityKind.Replicator)) continue;
                ArenaCover b = e.ToCover();
                if (zone.Footprint.Overlaps(b.Footprint)) blockers.Add(b);
            }
            int nx = Mathf.Max(1, Mathf.RoundToInt(zone.width / Cell)), nz = Mathf.Max(1, Mathf.RoundToInt(zone.depth / Cell));
            var blocked = new bool[nx, nz];
            bool Open(Wall w, float c) => holes.TryGetValue(w, out List<Span> s) && s.Any(sp => sp.Contains(c));
            for (int ix = 0; ix < nx; ix++)
            {
                float x = zone.XMin + (ix + 0.5f) * Cell;
                for (int iz = 0; iz < nz; iz++)
                {
                    float z = zone.ZMin + (iz + 0.5f) * Cell;
                    bool b = (x - zone.XMin < MaxRadius && !Open(Wall.W, z)) || (zone.XMax - x < MaxRadius && !Open(Wall.E, z)) ||
                             (z - zone.ZMin < MaxRadius && !Open(Wall.S, x)) || (zone.ZMax - z < MaxRadius && !Open(Wall.N, x));
                    if (!b)
                    {
                        var p = new Vector2(x, z);
                        foreach (ArenaCover c in blockers) if (c.DistanceTo(p) < MaxRadius) { b = true; break; }
                    }
                    blocked[ix, iz] = b;
                }
            }
            int IxOf(float x) => Mathf.Clamp(Mathf.FloorToInt((x - zone.XMin) / Cell), 0, nx - 1);
            int IzOf(float z) => Mathf.Clamp(Mathf.FloorToInt((z - zone.ZMin) / Cell), 0, nz - 1);
            foreach (Vector2 m in mouths)
            {
                int sx = IxOf(m.x), sz = IzOf(m.y);
                if (blocked[sx, sz]) continue;
                var seen = new bool[nx, nz];
                var q = new Queue<(int, int)>();
                seen[sx, sz] = true;
                q.Enqueue((sx, sz));
                while (q.Count > 0)
                {
                    (int cx, int cz) = q.Dequeue();
                    foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x >= nx || z >= nz || seen[x, z] || blocked[x, z]) continue;
                        seen[x, z] = true;
                        q.Enqueue((x, z));
                    }
                }
                if (seen[IxOf(target.x), IzOf(target.y)]) return true;
            }
            return false;
        }
    }
}
