using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1055: World 3's open floor read as flat and empty, nothing breaking it up at eye level.
    /// Fails on 12419fa (the commit before this fix): neither <c>ReefKit.BuildHydroponicBed</c> nor
    /// <c>MaxWorlds.Arena.ReefHydroponics</c> exist there, so this does not compile.
    ///
    /// Asserts RESOLVED state only, through the same real load path MV745World3HullDressingTests and
    /// MV1054OceanVoidTests already prove out (<see cref="MapRuntime.Build"/> -&gt;
    /// <see cref="WorldMaterials.Apply"/> -&gt; <see cref="ReefKit.DressHull"/>): every placed bed's
    /// own combined renderer bounds height, its own collider count, and its own centre's measured
    /// clearance from the SAME cover/gate/garrison data the placement pass itself read — never an
    /// authored constant compared to itself.
    /// </summary>
    public sealed class MV1055HydroponicBedTests
    {
        [Test]
        public void World3HydroponicBeds_AreLowAndColliderFree_AndClearOfCoverGatesAndGarrison()
        {
            WorldConfig cfg3 = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg3, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg3, out MapData map3, out string reason3), reason3);

            var root3 = new GameObject("MV1055 World3 Probe Root");
            try
            {
                // Exactly BackyardPath.Awake's own order: geometry, the world's palette sweep, then
                // the Reef-only hull pass this ticket adds bed placement to.
                MapBuild build = MapRuntime.Build(map3, root3.transform);
                var wm = new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
                wm.Apply(WorldCatalog.Get(2).Palette);
                ReefKit.DressHull(root3.transform);

                int placed = ReefHydroponics.PlaceBeds(root3.transform, map3, cfg3, build.Cover);
                Assert.That(placed, Is.GreaterThan(0),
                    "expected World 3's own authored v4 layout (30 large areas) to have room for at least one hydroponic bed");

                var gateMouths = new List<Vector2>();
                foreach (MapEntity e in map3.entities)
                    if (e != null && e.Kind == EntityKind.AreaGate) gateMouths.Add(new Vector2(e.x, e.z));

                var garrisonPoints = new List<Vector2>();
                foreach (WorldArea area in cfg3.areas)
                {
                    if (area?.garrison == null) continue;
                    foreach (WorldGarrisonEntry g in area.garrison)
                        if (g != null) garrisonPoints.Add(new Vector2(g.x, g.z));
                }

                HydroponicBed[] beds = root3.GetComponentsInChildren<HydroponicBed>(true);
                Assert.That(beds.Length, Is.EqualTo(placed), "PlaceBeds's own return count must match what it actually built");

                foreach (HydroponicBed bed in beds)
                {
                    Bounds? combined = null;
                    foreach (Renderer r in bed.GetComponentsInChildren<Renderer>(true))
                    {
                        if (combined == null) { combined = r.bounds; continue; }
                        Bounds b = combined.Value;
                        b.Encapsulate(r.bounds);
                        combined = b;
                    }

                    Assert.IsNotNull(combined, $"{bed.name} must carry at least one renderer");
                    Assert.That(combined.Value.size.y, Is.LessThanOrEqualTo(0.3f),
                        $"{bed.name} stands {combined.Value.size.y:F2} m tall -- must read as ground clutter, never cover");

                    Assert.That(bed.GetComponentsInChildren<Collider>(true), Is.Empty,
                        $"{bed.name} must carry no collider");

                    var centre = new Vector2(bed.transform.position.x, bed.transform.position.z);

                    foreach (CoverPiece piece in build.Cover)
                    {
                        float dist = piece.Cover.DistanceTo(centre);
                        Assert.That(dist, Is.GreaterThanOrEqualTo(ReefHydroponics.MinClearance),
                            $"{bed.name} sits {dist:F2} m from cover piece {piece.Cover.Name}, needs >= {ReefHydroponics.MinClearance} m");
                    }

                    foreach (Vector2 gate in gateMouths)
                    {
                        float dist = Vector2.Distance(centre, gate);
                        Assert.That(dist, Is.GreaterThanOrEqualTo(ReefHydroponics.MinClearance),
                            $"{bed.name} sits {dist:F2} m from a gate mouth, needs >= {ReefHydroponics.MinClearance} m");
                    }

                    foreach (Vector2 garrison in garrisonPoints)
                    {
                        float dist = Vector2.Distance(centre, garrison);
                        Assert.That(dist, Is.GreaterThanOrEqualTo(ReefHydroponics.MinClearance),
                            $"{bed.name} sits {dist:F2} m from a garrison point, needs >= {ReefHydroponics.MinClearance} m");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(root3);
            }
        }
    }
}
