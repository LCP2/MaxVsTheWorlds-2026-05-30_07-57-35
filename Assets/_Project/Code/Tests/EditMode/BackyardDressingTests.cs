using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Models;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The Backyard set dressing (YT-75).
    ///
    /// The ticket's acceptance is "the yard reads as a real backyard, and the props don't obstruct
    /// movement or the mission path". The first half is Lee's eye. The second half is arithmetic, and
    /// this is where it gets checked — before a build, not after a playtest. Two claims:
    ///
    ///   * nothing the dressing places can affect the fight, and
    ///   * the kit it places is actually in the project, at the size the placement maths assumed.
    /// </summary>
    public sealed class BackyardDressingTests
    {
        private const float ShedZ = 15f;          // BackyardPath.shedZ
        private const float SpawnRadius = 3.5f;   // BackyardPath.shedSpawnRadius

        private static BackyardPathLayout Layout => BackyardPathLayout.Default;

        private static List<DressingProp> Set() =>
            BackyardDressingSet.Build(Layout, ShedZ, SpawnRadius);

        // --- the set itself -------------------------------------------------------------------

        [Test]
        public void TheShippedYard_ClearsEveryPlacementRule()
        {
            Assert.IsTrue(
                BackyardDressingSet.Validate(Layout, Set(), BackyardCover.Default, ShedZ, SpawnRadius,
                                             out string why),
                why);
        }

        [Test]
        public void TheShippedYard_IsActuallyDressed()
        {
            var set = Set();

            // Not an arbitrary number — a fence line round three rooms plus planting is dozens of
            // props. A handful would mean a generator that silently produced almost nothing.
            Assert.Greater(set.Count, 100, "the yard is barely dressed");

            Assert.IsTrue(set.Any(p => p.Key == PropCatalog.FenceGate), "no way into the garden");
            Assert.IsTrue(set.Any(p => p.Key == PropCatalog.PathStone), "no path to follow");
            Assert.IsTrue(set.Any(p => Trees.Contains(p.Key)), "a backyard with no trees");
            Assert.IsTrue(set.Any(p => p.Zone == DressingZone.Factory), "the shed is undressed");
        }

        private static readonly string[] Trees =
        {
            PropCatalog.TreeDefault, PropCatalog.TreeOak, PropCatalog.TreeFat,
            PropCatalog.TreeThin, PropCatalog.TreeSmall,
        };

        [Test]
        public void TheYardIsTheSameEveryTime()
        {
            // The WebGL link, CI and Lee's editor have to be looking at the same garden, or a
            // "the shrub by the gate is wrong" report means nothing.
            var a = Set();
            var b = Set();

            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Key, b[i].Key, $"prop {i} changed identity between builds");
                Assert.AreEqual(a[i].CenterXz, b[i].CenterXz, $"prop {i} moved between builds");
            }
        }

        [Test]
        public void EveryPropIsAModelWeActuallyHave()
        {
            foreach (DressingProp prop in Set())
                Assert.IsTrue(PropCatalog.Has(prop.Key), $"'{prop.Key}' is not in the kit");
        }

        [Test]
        public void NothingTallStandsInTheFightSpace()
        {
            Rect[] interiors = BackyardDressingSet.Interiors(Layout);

            foreach (DressingProp prop in Set())
            {
                if (prop.IsFlat || prop.Zone == DressingZone.Factory) continue;

                foreach (Rect room in interiors)
                    Assert.IsFalse(room.Overlaps(prop.Footprint),
                        $"{prop.Key} at {prop.CenterXz} is standing in the middle of a room");
            }
        }

        [Test]
        public void NothingTallStandsInADoorway()
        {
            Rect[] doors = BackyardDressingSet.Doorways(Layout);

            foreach (DressingProp prop in Set())
            {
                if (prop.IsFlat || prop.Zone == DressingZone.Factory) continue;

                foreach (Rect door in doors)
                    Assert.IsFalse(door.Overlaps(prop.Footprint),
                        $"{prop.Key} at {prop.CenterXz} is standing in a doorway");
            }
        }

        [Test]
        public void TheSteppingStonesRunUpTheMissionLine()
        {
            var stones = Set().Where(p => p.Key == PropCatalog.PathStone).ToList();

            Assert.Greater(stones.Count, 5, "the path is a few scattered rocks, not a path");
            foreach (DressingProp s in stones)
            {
                Assert.IsTrue(s.IsFlat, "a stepping stone you can hide behind is not a stepping stone");
                Assert.Less(Mathf.Abs(s.CenterXz.x), 1.5f, "the path has wandered off the mission line");
            }
        }

        [Test]
        public void TheShedDressingStaysInsideTheSpawnRing()
        {
            var ring = new Vector2(0f, ShedZ);

            foreach (DressingProp prop in Set().Where(p => p.Zone == DressingZone.Factory))
            {
                Assert.LessOrEqual(prop.FarthestFrom(ring), SpawnRadius - BackyardDressingSet.SpawnClearance,
                    $"{prop.Key} reaches the ring the factory spawns robots on");
            }
        }

        // --- the rules have teeth -------------------------------------------------------------

        [Test]
        public void ATreeInTheMiddleOfTheLawnIsRejected()
        {
            var bad = Set();
            bad.Add(new DressingProp(PropCatalog.TreeDefault, new Vector2(0f, 8f),
                                     PropCatalog.ScaleToHeight(PropCatalog.TreeDefault, 4f)));

            Assert.IsFalse(
                BackyardDressingSet.Validate(Layout, bad, BackyardCover.Default, ShedZ, SpawnRadius,
                                             out string why));
            StringAssert.Contains("fight space", why);
        }

        [Test]
        public void ABushInTheBossGateIsRejected()
        {
            var bad = Set();
            bad.Add(new DressingProp(PropCatalog.BushDetailed, new Vector2(0f, Layout.GateZ),
                                     PropCatalog.ScaleToHeight(PropCatalog.BushDetailed, 1f)));

            Assert.IsFalse(
                BackyardDressingSet.Validate(Layout, bad, BackyardCover.Default, ShedZ, SpawnRadius,
                                             out string why));
            Assert.IsNotNull(why);
        }

        [Test]
        public void ShedDressingThatReachesTheSpawnRingIsRejected()
        {
            var bad = Set();
            bad.Add(new DressingProp(PropCatalog.LogStack, new Vector2(0f, ShedZ + SpawnRadius),
                                     Vector3.one, 0f, DressingZone.Factory));

            Assert.IsFalse(
                BackyardDressingSet.Validate(Layout, bad, BackyardCover.Default, ShedZ, SpawnRadius,
                                             out string why));
            StringAssert.Contains("spawn ring", why);
        }

        [Test]
        public void APropThatIsntInTheKitIsRejected()
        {
            var bad = Set();
            bad.Add(new DressingProp("garden_gnome", Vector2.zero, Vector3.one));

            Assert.IsFalse(
                BackyardDressingSet.Validate(Layout, bad, BackyardCover.Default, ShedZ, SpawnRadius,
                                             out string why));
            StringAssert.Contains("garden_gnome", why);
        }

        // --- the kit is really in the project --------------------------------------------------

        [Test]
        public void EveryKitPropHasAPrefab()
        {
            foreach (string key in PropCatalog.Keys)
            {
                Assert.IsNotNull(ModelLibrary.Load(PropCatalog.ResourceKey(key)),
                    $"no prefab for '{key}' — run MaxWorlds ▸ Art ▸ Import Garden Kit and commit the result");
            }
        }

        [Test]
        public void EveryKitPrefabIsTheSizeThePlacementMathsAssumes()
        {
            foreach (string key in PropCatalog.Keys)
            {
                var prefab = ModelLibrary.Load(PropCatalog.ResourceKey(key));
                if (prefab == null) continue;   // reported by EveryKitPropHasAPrefab

                Bounds b = Measure(prefab);
                Vector3 want = PropCatalog.Size(key);

                // The catalog is what the fence maths, the edge band and the spawn-ring clearance are
                // all computed from. If a model is swapped for one a different size, every one of
                // those numbers is quietly wrong — so it fails here instead.
                Assert.AreEqual(want.x, b.size.x, 0.02f, $"{key} is not as wide as the catalog says");
                Assert.AreEqual(want.y, b.size.y, 0.02f, $"{key} is not as tall as the catalog says");
                Assert.AreEqual(want.z, b.size.z, 0.02f, $"{key} is not as deep as the catalog says");

                Assert.AreEqual(0f, b.center.x, 0.02f, $"{key} is not centred on its pivot");
                Assert.AreEqual(0f, b.center.z, 0.02f, $"{key} is not centred on its pivot");
                Assert.AreEqual(0f, b.min.y, 0.02f, $"{key} does not sit on the ground");
            }
        }

        [Test]
        public void NoKitPropCarriesACollider()
        {
            foreach (string key in PropCatalog.Keys)
            {
                var prefab = ModelLibrary.Load(PropCatalog.ResourceKey(key));
                if (prefab == null) continue;

                Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true),
                    $"{key} has a collider — dressing is scenery, it must never block anything");
            }
        }

        [Test]
        public void EveryKitPropSaysItBringsItsOwnMaterial()
        {
            foreach (string key in PropCatalog.Keys)
            {
                var prefab = ModelLibrary.Load(PropCatalog.ResourceKey(key));
                if (prefab == null) continue;

                // Without this the material layer repaints the whole kit in one flat surface colour,
                // and the art pass has achieved nothing.
                Assert.IsNotNull(prefab.GetComponent<KeepsOwnMaterial>(),
                    $"{key} would be repainted by the world-material pass");
            }
        }

        [Test]
        public void NoKitPropWillRenderMagenta()
        {
            foreach (string key in PropCatalog.Keys)
            {
                var prefab = ModelLibrary.Load(PropCatalog.ResourceKey(key));
                if (prefab == null) continue;

                foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
                {
                    foreach (Material m in r.sharedMaterials)
                    {
                        // YT-58's lesson: a material whose shader the pipeline can't render looks fine
                        // in the editor and ships magenta.
                        Assert.IsNotNull(m, $"{key} has an empty material slot");
                        Assert.IsNotNull(m.shader, $"{key}'s material has no shader");
                        Assert.IsTrue(m.shader.isSupported,
                            $"{key} uses '{m.shader.name}', which this pipeline cannot render");
                    }
                }
            }
        }

        /// <summary>Bounds of a prefab's geometry in its own space — prefabs can't be measured through
        /// Renderer.bounds, which wants a transform in a scene.</summary>
        private static Bounds Measure(GameObject prefab)
        {
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            Assert.IsNotEmpty(filters, $"{prefab.name} has no mesh");

            Bounds total = default;
            bool first = true;

            foreach (MeshFilter f in filters)
            {
                if (f.sharedMesh == null) continue;

                Bounds local = f.sharedMesh.bounds;
                Matrix4x4 toRoot = ToRoot(f.transform, prefab.transform);
                Bounds inRoot = Transform(local, toRoot);

                if (first) { total = inRoot; first = false; }
                else total.Encapsulate(inRoot);
            }

            Assert.IsFalse(first, $"{prefab.name} has no mesh");
            return total;
        }

        private static Matrix4x4 ToRoot(Transform t, Transform root)
        {
            Matrix4x4 m = Matrix4x4.identity;
            for (Transform cur = t; cur != null && cur != root; cur = cur.parent)
                m = Matrix4x4.TRS(cur.localPosition, cur.localRotation, cur.localScale) * m;
            return m;
        }

        private static Bounds Transform(Bounds b, Matrix4x4 m)
        {
            Vector3 c = m.MultiplyPoint3x4(b.center);
            Vector3 e = b.extents;
            Vector3 x = m.MultiplyVector(new Vector3(e.x, 0f, 0f));
            Vector3 y = m.MultiplyVector(new Vector3(0f, e.y, 0f));
            Vector3 z = m.MultiplyVector(new Vector3(0f, 0f, e.z));

            var extents = new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));

            return new Bounds(c, extents * 2f);
        }
    }
}
