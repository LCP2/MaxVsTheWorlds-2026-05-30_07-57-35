using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Editor;
using MaxWorlds.Models;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The Backyard set dressing (YT-75), now generated FROM THE MAP.
    ///
    /// The ticket's acceptance is "the yard reads as a real backyard, and the props don't obstruct
    /// movement or the mission path". The first half is Lee's eye. The second half is arithmetic, and
    /// this is where it gets checked — before a build, not after a playtest. Three claims:
    ///
    ///   * the dressing follows the level's own walls, so a room the map grows is a room the garden
    ///     grows with it — that is the whole of this change, and it is asserted, not assumed;
    ///   * nothing the dressing places can affect the fight; and
    ///   * the kit it places is actually in the project, at the size the placement maths assumed.
    /// </summary>
    public sealed class BackyardDressingTests
    {
        private static MapData Map => MapLibrary.Load(MapLibrary.BackyardSlice);

        private static List<DressingProp> Set() => BackyardDressingSet.Build(Map);

        private static ArenaCover[] Cover => BackyardCover.Default;

        // --- the art follows the map ------------------------------------------------------------

        /// <summary>
        /// The point of the whole change. The yard used to be thirteen hand-listed fence runs along a
        /// straight corridor; the map now has a nook off the lawn's left and a shed off its right, and
        /// a hand-listed fence would have left both of them bare while fencing walls that no longer
        /// exist. So: EVERY face of every wall that looks into a room gets a fence. No exceptions, no
        /// list.
        /// </summary>
        [Test]
        public void EveryInnerWallFaceIsFenced_IncludingTheRoomsThatAreNew()
        {
            MapData map = Map;
            var panels = Set()
                .Where(p => p.Key == PropCatalog.FencePanel || p.Key == PropCatalog.FenceGate)
                .ToList();

            foreach (WallFace face in MapGeometry.Faces(map))
            {
                if (!face.FacesRoom) continue;

                Vector2 mid = (face.A + face.B) * 0.5f;
                Assert.IsTrue(panels.Any(p => p.DistanceTo(mid) < 0.5f),
                    $"the wall face at {mid} looks into a room and has no fence on it — the dressing " +
                    "is still following something other than the map");
            }
        }

        /// <summary>Named rather than counted: it is the shed and the nook — the rooms that did not
        /// exist when the fence was written out by hand — that this has to cover.</summary>
        [Test]
        public void TheShedAndTheNookAreBothFenced()
        {
            MapData map = Map;
            var set = Set();

            foreach (string id in new[] { "shed", "nook", "gatehouse", "compost" })
            {
                MapZone zone = map.Zone(id);
                Assert.IsTrue(
                    set.Any(p => p.Key == PropCatalog.FencePanel && Near(zone, p.CenterXz, 1.5f)),
                    $"'{id}' has no fence on it");
            }
        }

        /// <summary>Inside the room, within <paramref name="band"/> of one of its edges.</summary>
        private static bool Near(MapZone zone, Vector2 at, float band)
        {
            if (at.x < zone.XMin - band || at.x > zone.XMax + band) return false;
            if (at.y < zone.ZMin - band || at.y > zone.ZMax + band) return false;

            return at.x <= zone.XMin + band || at.x >= zone.XMax - band
                || at.y <= zone.ZMin + band || at.y >= zone.ZMax - band;
        }

        // --- the set itself -------------------------------------------------------------------

        [Test]
        public void TheShippedYard_ClearsEveryPlacementRule()
        {
            Assert.IsTrue(BackyardDressingSet.Validate(Map, Set(), Cover, out string why), why);
        }

        [Test]
        public void TheShippedYard_IsActuallyDressed()
        {
            var set = Set();

            // Not an arbitrary number — a fence line round six rooms plus planting is dozens of
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
            Rect[] interiors = BackyardDressingSet.Interiors(Map);

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
            Rect[] doors = BackyardDressingSet.Doorways(Map);

            // Every link cuts one — including the ways into the nook and the shed, which the old
            // hand-listed pair of doorways knew nothing about.
            Assert.AreEqual(Map.links.Length, doors.Length, "a link in the map cut no doorway");

            foreach (DressingProp prop in Set())
            {
                if (prop.IsFlat || prop.Zone == DressingZone.Factory) continue;

                foreach (Rect door in doors)
                    Assert.IsFalse(door.Overlaps(prop.Footprint),
                        $"{prop.Key} at {prop.CenterXz} is standing in a doorway");
            }
        }

        [Test]
        public void TheSteppingStonesFollowTheRouteToTheFactory()
        {
            MapData map = Map;
            List<Vector2> route = BackyardDressingSet.Route(map);
            var stones = Set().Where(p => p.Key == PropCatalog.PathStone).ToList();

            // The route turns now — spawn, up the lawn, right into the shed — so "near the centre
            // line" is no longer a thing to assert. Near THE ROUTE is.
            Assert.Greater(stones.Count, 5, "the path is a few scattered rocks, not a path");
            Assert.GreaterOrEqual(route.Count, 3, "the route to the factory does not go through a room");

            foreach (DressingProp s in stones)
            {
                Assert.IsTrue(s.IsFlat, "a stepping stone you can hide behind is not a stepping stone");
                Assert.Less(DistanceToRoute(route, s.CenterXz), 1.5f,
                    $"a stone at {s.CenterXz} has wandered off the mission line");
            }

            // …and the line it paints ends at the thing the player came to break.
            MapEntity factory = map.First(EntityKind.Factory);
            Assert.Less(stones.Min(s => (s.CenterXz - factory.CenterXz).magnitude), 6f,
                "the path stops short of the factory");
        }

        private static float DistanceToRoute(List<Vector2> route, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < route.Count; i++)
            {
                Vector2 a = route[i], ab = route[i + 1] - route[i];
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                best = Mathf.Min(best, (p - (a + ab * t)).magnitude);
            }
            return best;
        }

        [Test]
        public void TheShedDressingStaysInsideTheSpawnRing()
        {
            Vector2 ring = Map.First(EntityKind.Factory).CenterXz;

            foreach (DressingProp prop in Set().Where(p => p.Zone == DressingZone.Factory))
            {
                Assert.LessOrEqual(prop.FarthestFrom(ring),
                    MapValidation.SpawnRadius - BackyardDressingSet.SpawnClearance,
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

            Assert.IsFalse(BackyardDressingSet.Validate(Map, bad, Cover, out string why));
            StringAssert.Contains("fight space", why);
        }

        [Test]
        public void ABushInTheBossGateIsRejected()
        {
            var bad = Set();
            MapEntity gate = Map.First(EntityKind.Gate);

            bad.Add(new DressingProp(PropCatalog.BushDetailed, gate.CenterXz,
                                     PropCatalog.ScaleToHeight(PropCatalog.BushDetailed, 1f)));

            Assert.IsFalse(BackyardDressingSet.Validate(Map, bad, Cover, out string why));
            StringAssert.Contains("doorway", why);
        }

        [Test]
        public void ABushInTheDoorwayIntoTheShedIsRejected()
        {
            // The doorway the corridor-shaped dressing did not know existed.
            var bad = Set();
            bad.Add(new DressingProp(PropCatalog.BushDetailed, new Vector2(12f, 15f),
                                     PropCatalog.ScaleToHeight(PropCatalog.BushDetailed, 1f)));

            Assert.IsFalse(BackyardDressingSet.Validate(Map, bad, Cover, out string why));
            StringAssert.Contains("doorway", why);
        }

        [Test]
        public void ShedDressingThatReachesTheSpawnRingIsRejected()
        {
            var bad = Set();
            Vector2 factory = Map.First(EntityKind.Factory).CenterXz;

            bad.Add(new DressingProp(PropCatalog.LogStack,
                                     factory + new Vector2(0f, MapValidation.SpawnRadius),
                                     Vector3.one, 0f, DressingZone.Factory));

            Assert.IsFalse(BackyardDressingSet.Validate(Map, bad, Cover, out string why));
            StringAssert.Contains("spawn ring", why);
        }

        [Test]
        public void APropThatIsntInTheKitIsRejected()
        {
            var bad = Set();
            bad.Add(new DressingProp("garden_gnome", Vector2.zero, Vector3.one));

            Assert.IsFalse(BackyardDressingSet.Validate(Map, bad, Cover, out string why));
            StringAssert.Contains("garden_gnome", why);
        }

        // --- the kit survives the yard's sunlight ------------------------------------------------

        [Test]
        public void NoKitMaterialClipsToCreamInTheSun()
        {
            // The yard's key light is 2.2× and URP/Lit multiplies albedo by it, so an albedo much
            // past 0.6 is white before the tonemapper sees it. Kenney paints his kit in bright
            // pastels for an UNLIT look — wood is (1.00, 0.56, 0.38) — and the first import kept
            // them: every sunlit fence panel came out bleached cream while the same panel in shadow
            // stayed brown. One material, two fences. This is what stops that coming back, and it
            // asserts on the COMMITTED .mat assets, because those are what actually ship.
            float key = BackyardLook.Default.KeyIntensity;
            Assert.Greater(key, 1f, "this test only means anything under a key brighter than 1×");

            var materials = AssetDatabase.FindAssets("t:Material", new[] { KitImport.MaterialDir })
                                         .Select(AssetDatabase.GUIDToAssetPath)
                                         .Select(AssetDatabase.LoadAssetAtPath<Material>)
                                         .Where(m => m != null)
                                         .ToList();

            Assert.IsNotEmpty(materials, $"no kit materials found in {KitImport.MaterialDir}");

            foreach (var m in materials)
            {
                Assert.IsTrue(m.HasProperty("_BaseColor"), $"{m.name} has no _BaseColor to check");

                Color c = m.GetColor("_BaseColor");
                Assert.LessOrEqual(c.maxColorComponent, KitImport.SunlitAlbedoCeiling + 1e-4f,
                    $"{m.name} is albedo {c.maxColorComponent:0.00} — under a {key:0.0}× key it renders " +
                    "as a highlight, not as a colour. Give it a tone in KitImport.Recolour.");
            }
        }

        [Test]
        public void AKitColourWeNeverClassified_IsDimmedRatherThanBlownOut()
        {
            // The backstop for the next kit, or the next prop in this one: a material nobody chose a
            // colour for should come out dull, not white. Hue and internal contrast survive; only the
            // brightness is pulled down.
            Color blinding = KitImport.UnderTheSun(new Color(1f, 0.56f, 0.38f));

            Assert.LessOrEqual(blinding.maxColorComponent, KitImport.SunlitAlbedoCeiling + 1e-4f);
            Assert.That(blinding.g / blinding.r, Is.EqualTo(0.56f).Within(0.01f), "the hue drifted");

            Color safe = new Color(0.3f, 0.2f, 0.1f);
            Assert.AreEqual(safe, KitImport.UnderTheSun(safe), "a colour already under the ceiling is left alone");
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
