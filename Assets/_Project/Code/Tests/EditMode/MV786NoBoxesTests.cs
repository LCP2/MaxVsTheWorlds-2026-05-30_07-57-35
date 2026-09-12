using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-786: World 2 authors 81 cover pieces as <c>shape: "box"</c> and a wall run as one long slab.
    /// The authored rect is the collision footprint the level design depends on; what is DRAWN inside
    /// it was still a flat primitive box either way. This gives each of the five authored
    /// <c>dressing</c> values its own turned form (Silt hopper, Standpipe cluster, Burst main, Pump
    /// set, Collapsed grating) and gives a wall run panels, ribs, pilasters, a coping and a kerb —
    /// while leaving every collider, position, size, dressing value and count exactly as authored.
    ///
    /// <see cref="StormdrainKit.BuildWallPanels"/>, <see cref="StormdrainKit.BuildSiltHopper"/>,
    /// <see cref="StormdrainKit.BuildCollapsedGrating"/> and <see cref="StormdrainKit.BuildBurstMain"/>
    /// do not exist before this ticket, and <see cref="StormdrainKit.BuildStandpipe"/>'s own signature
    /// changes — so this fails to COMPILE on the base commit (3e88c693924c8207ddf5b54bb367082615edab4f),
    /// the same "doesn't exist there yet" failure MV755StormdrainDressingTests documents for its own
    /// base commit.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1) asserting RESOLVED state only (Rule 2,
    /// Tier 2): zero renderers under a built cover piece or wall run wear a primitive mesh; the five
    /// dressings resolve to five distinct mesh-name sets; every cover block's own BoxCollider stays
    /// exactly the authored footprint; a 16 m wall run resolves to exactly 4 panels and 5 pilasters;
    /// and the burst main's bore mesh resolves a smaller maximum radius than its own outer tube.
    /// </summary>
    public sealed class MV786NoBoxesTests
    {
        private static readonly Regex PrimitiveMeshName = new Regex(@"^(Cube|Cylinder|Quad|Plane)$");

        [Test]
        public void NoBoxesFix_ResolvesTurnedCoverFormsAndPanelledWalls()
        {
            AssertWorldTwoArenaCarriesNoPrimitiveMeshesUnderCoverOrWalls();
            AssertFiveDressingsResolveToDistinctMeshNameSets();
            AssertCoverColliderFootprintsAreUnchanged();
            AssertSixteenMetreWallRunBuildsFourPanelsAndFivePilasters();
            AssertBurstMainBoreIsNarrowerThanItsOuterTube();
        }

        private static void AssertWorldTwoArenaCarriesNoPrimitiveMeshesUnderCoverOrWalls()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV786 host").transform;
            try
            {
                MapBuild build = MapRuntime.Build(map, host);
                StormdrainDressing.Dress(host, map, build.Cover);

                Transform dressingHost = host.Find("Stormdrain Dressing");
                Assert.IsNotNull(dressingHost, "the dressing host was never built");

                Transform coverHost = dressingHost.Find("Cover");
                Assert.IsNotNull(coverHost, "the cover dressing host was never built");
                Assert.IsTrue(coverHost.childCount > 0, "World 2 must dress at least one cover piece for this test to mean anything");
                AssertNoPrimitiveMeshes(coverHost, "cover piece");

                Transform wallPanelsHost = dressingHost.Find("Wall Panels");
                Assert.IsNotNull(wallPanelsHost, "the wall panels host was never built");
                Assert.IsTrue(wallPanelsHost.childCount > 0, "World 2 must build at least one wall run for this test to mean anything");
                AssertNoPrimitiveMeshes(wallPanelsHost, "wall run");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        private static void AssertNoPrimitiveMeshes(Transform root, string label)
        {
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Assert.IsNotNull(mf.sharedMesh, $"{mf.name} under a {label} carries no mesh");
                Assert.IsFalse(PrimitiveMeshName.IsMatch(mf.sharedMesh.name),
                    $"{mf.name} under a {label} still wears the built-in primitive mesh '{mf.sharedMesh.name}'");
            }
        }

        private static void AssertFiveDressingsResolveToDistinctMeshNameSets()
        {
            var size = new Vector3(2f, 1.6f, 2f);
            var probeHost = new GameObject("MV786 dressing probe").transform;
            try
            {
                var sets = new (string label, HashSet<string> names)[]
                {
                    ("Standpipe", MeshNames(StormdrainKit.BuildStandpipe(probeHost, Vector3.zero, size))),
                    ("Collapsed Grating", MeshNames(StormdrainKit.BuildCollapsedGrating(probeHost, Vector3.zero, size))),
                    ("Silt Hopper", MeshNames(StormdrainKit.BuildSiltHopper(probeHost, Vector3.zero, size))),
                    ("Pump Housing", MeshNames(StormdrainKit.BuildPumpHousing(probeHost, Vector3.zero, size))),
                    ("Burst Main", MeshNames(StormdrainKit.BuildBurstMain(probeHost, Vector3.zero, size))),
                };

                for (int i = 0; i < sets.Length; i++)
                    Assert.IsTrue(sets[i].names.Count > 0, $"{sets[i].label} built no mesh at all");

                for (int i = 0; i < sets.Length; i++)
                    for (int j = i + 1; j < sets.Length; j++)
                        Assert.IsFalse(sets[i].names.SetEquals(sets[j].names),
                            $"{sets[i].label} and {sets[j].label} resolved to the exact same mesh-name set — " +
                            "two dressings must never build the same form");
            }
            finally
            {
                Object.DestroyImmediate(probeHost.gameObject);
            }
        }

        private static HashSet<string> MeshNames(GameObject root)
        {
            var set = new HashSet<string>();
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) set.Add(mf.sharedMesh.name);
            return set;
        }

        private static void AssertCoverColliderFootprintsAreUnchanged()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV786 collider host").transform;
            try
            {
                MapBuild build = MapRuntime.Build(map, host);
                Assert.IsTrue(build.Cover.Count > 0, "World 2 must author at least one cover piece for this test to mean anything");

                int checkedCount = 0;
                foreach (CoverPiece piece in build.Cover)
                {
                    if (piece.Body == null) continue;
                    var collider = piece.Body.GetComponent<BoxCollider>();
                    if (collider == null) continue; // a cylinder-shaped piece keeps its CapsuleCollider, untouched either way

                    Assert.AreEqual(piece.Cover.Size, collider.size,
                        $"{piece.Body.name}'s BoxCollider must stay exactly the authored w x d x h footprint");
                    Assert.AreEqual(Vector3.zero, collider.center,
                        $"{piece.Body.name}'s BoxCollider must stay centred on the block's own origin");
                    checkedCount++;
                }
                Assert.Greater(checkedCount, 0, "no box-shaped cover piece was actually checked");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        private static void AssertSixteenMetreWallRunBuildsFourPanelsAndFivePilasters()
        {
            var wallHost = new GameObject("MV786 wall probe").transform;
            try
            {
                StormdrainKit.BuildWallPanels(wallHost, Vector3.zero, new Vector3(16f, 3f, 0.4f),
                    alongX: true, wallMaterial: null);

                Transform run = wallHost.Find("Wall Run");
                Assert.IsNotNull(run, "no Wall Run was built");

                var children = run.Cast<Transform>().ToList();
                int panelCount = children.Count(t => t.name.StartsWith("Panel") && !t.name.Contains("Rib"));
                int pilasterCount = children.Count(t => t.name.StartsWith("Pilaster"));

                Assert.AreEqual(4, panelCount, "a 16 m wall run must build exactly 4 panels");
                Assert.AreEqual(5, pilasterCount, "a 16 m wall run must build exactly 5 pilasters");
            }
            finally
            {
                Object.DestroyImmediate(wallHost.gameObject);
            }
        }

        private static void AssertBurstMainBoreIsNarrowerThanItsOuterTube()
        {
            var burstHost = new GameObject("MV786 burst probe").transform;
            try
            {
                GameObject burst = StormdrainKit.BuildBurstMain(burstHost, Vector3.zero, new Vector3(2f, 1.6f, 3f));

                Transform outer = burst.transform.Find("Outer");
                Transform bore = burst.transform.Find("Bore");
                Assert.IsNotNull(outer, "the burst main built no Outer tube");
                Assert.IsNotNull(bore, "the burst main built no Bore");

                float outerMaxRadius = MaxRadiusXZ(outer.GetComponent<MeshFilter>().sharedMesh);
                float boreMaxRadius = MaxRadiusXZ(bore.GetComponent<MeshFilter>().sharedMesh);

                Assert.Less(boreMaxRadius, outerMaxRadius,
                    $"the bore's own max radius ({boreMaxRadius:F3}) must be smaller than the outer tube's ({outerMaxRadius:F3})");
            }
            finally
            {
                Object.DestroyImmediate(burstHost.gameObject);
            }
        }

        private static float MaxRadiusXZ(Mesh mesh) =>
            mesh.vertices.Max(v => new Vector2(v.x, v.z).magnitude);
    }
}
