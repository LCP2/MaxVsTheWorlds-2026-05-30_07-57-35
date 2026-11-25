using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1463: one Rocket Crate per Reef area a1-a30, placed in its stronghold. One test (Rule 1), failing
    /// on the pre-change main (no Reef area authors rocketCrates, so zero crates are built). Tier 2: builds
    /// the real Reef through MapRuntime.Build, measures the built Rocket Crate pickup positions, checks
    /// clearance of cover (0.8 m) and robots and canisters (1.4 m), and flood-fills the walk grid from the
    /// area's entry mouths (0.5 m Max radius, as MV-875) to prove each crate is reachable.
    /// </summary>
    public sealed class MV1463ReefRocketCratesTests
    {
        private const string Table =
            "a1 11.5 39.0;a2 21.0 9.5;a3 57.0 17.0;a4 83.0 10.5;a5 110.5 5.5;a6 149.0 14.0;a7 186.5 22.5;" +
            "a8 186.0 68.5;a9 187.0 83.5;a10 182.0 118.0;a11 159.0 123.0;a12 90.0 117.0;a13 71.0 106.0;" +
            "a14 78.5 72.5;a15 128.0 78.0;a16 156.0 73.0;a17 143.5 46.5;a18 125.0 42.0;a19 79.5 43.5;" +
            "a20 51.0 34.5;a21 46.0 74.0;a22 11.0 67.0;a23 18.5 134.5;a24 18.0 155.5;a25 42.5 165.5;" +
            "a26 128.0 163.5;a27 139.0 156.0;a28 183.5 167.5;a29 219.5 148.5;a30 249.5 160.0";

        private const float Cell = 0.1f;
        private const float MaxRadius = 0.5f;
        private GameObject _host;

        [SetUp]
        public void SetUp() { PickupWallet.Reset(); RocketCrateCensus.Reset(); Pickup.ResetRegistry(); }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            foreach (PickupDirector d in Object.FindObjectsByType<PickupDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            PickupWallet.Reset(); RocketCrateCensus.Reset(); Pickup.ResetRegistry();
        }

        [Test]
        public void ReefA1ToA30_EachBuildsOneReachableClearRocketCrateInItsStronghold()
        {
            WorldConfig reef = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(reef, "world3_config.json failed to load");
            Assert.IsTrue(WorldMapLoader.TryLoad(reef, out MapData map, out string loadReason), loadReason);
            Assert.IsTrue(MapValidation.Validate(map, out string valReason), valReason);

            _host = new GameObject("MV1463 Host");
            MapRuntime.Build(map, _host.transform);

            var crates = Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(p => p.Kind == PickupKind.RocketCrate).ToList();
            var failures = new List<string>();
            foreach (string row in Table.Split(';'))
            {
                string[] t = row.Split(' ');
                var at = new Vector2(float.Parse(t[1], System.Globalization.CultureInfo.InvariantCulture),
                                     float.Parse(t[2], System.Globalization.CultureInfo.InvariantCulture));
                var near = crates.Where(p => Vector2.Distance(
                    new Vector2(p.transform.position.x, p.transform.position.z), at) <= 0.1f).ToList();
                if (near.Count != 1)
                {
                    failures.Add(t[0] + ": built Rocket Crates within 0.1 m of " + at + " = " + near.Count + " (expected 1)");
                    continue;
                }
                Vector2 pos = new Vector2(near[0].transform.position.x, near[0].transform.position.z);

                WorldArea area = reef.areas.First(a => a.id == t[0]);
                string zoneId = (area.index >= 1 && area.index <= reef.dials.areaCount) ? "area" + area.index : area.id;
                MapZone zone = map.Zone(zoneId);

                foreach (MapEntity e in map.entities)
                {
                    if (e == null) continue;
                    if (e.Kind == EntityKind.Cover && zone.Footprint.Overlaps(e.ToCover().Footprint) && e.ToCover().DistanceTo(pos) < 0.8f)
                        failures.Add(t[0] + ": crate within 0.8 m of cover " + e.id);
                    if (e.Kind == EntityKind.Canister && Vector2.Distance(e.CenterXz, pos) < 1.4f)
                        failures.Add(t[0] + ": crate within 1.4 m of canister " + e.id);
                }
                foreach (WorldGarrisonEntry g in area.garrison)
                    if (g != null && Vector2.Distance(new Vector2(g.x, g.z), pos) < 1.4f)
                        failures.Add(t[0] + ": crate within 1.4 m of robot " + g.kind);

                if (!Reachable(map, zone, pos))
                    failures.Add(t[0] + ": crate at " + pos + " is not reachable from the area's entry gate");
            }
            Assert.IsEmpty(failures, "MV-1463 Reef Rocket Crate failures:\n" + string.Join("\n", failures));
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
