using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Dressing;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-75 — the set-dressing plan.
    ///
    /// The yard is now full of props, and exactly one thing about that can break the game: a prop
    /// standing where the fight happens. Dressing carries no colliders, so it can't physically stop
    /// anyone — but a robot chest-deep in a hedge, or a fence panel across the boss doorway, is a
    /// broken-looking game either way. These tests are the reason nobody has to walk the yard and
    /// check by eye every time a prop moves.
    /// </summary>
    public sealed class DressingPlanTests
    {
        private const float ShedZ = 15f;         // BackyardPath's shipped shed position
        private const float SpawnRadius = 3.5f;

        private static BackyardPathLayout Layout => BackyardPathLayout.Default;
        private static ArenaCover[] Cover => BackyardCover.Default;

        private static List<DressProp> Plan(int seed = 75) =>
            DressingPlan.Build(Layout, ShedZ, Cover, seed);

        [Test]
        public void ThePlan_StandsNothingWhereTheFightIs()
        {
            Assert.IsTrue(DressingPlan.Validate(Layout, Plan(), ShedZ, Cover, out string reason),
                $"the shipped yard fails its own placement rules: {reason}");
        }

        [Test]
        public void ThePlan_IsTheSameYardEveryTime()
        {
            List<DressProp> a = Plan(), b = Plan();

            Assert.AreEqual(a.Count, b.Count, "the same seed produced a different number of props");
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Model, b[i].Model, $"prop {i} differs by model");
                Assert.That(Vector3.Distance(a[i].Position, b[i].Position), Is.LessThan(1e-4f),
                    $"prop {i} ({a[i].Model}) moved between two builds of the same seed");
            }
        }

        [Test]
        public void ThePlan_LeavesTheMissionLineClear()
        {
            // Patio → shed → gate. Anything taller than an ankle standing on this line is in the way
            // of the one route the player has to walk, whether or not it collides. It starts a metre
            // in from the back wall: the wall (and the gate in it) is behind Max, not on his path.
            float from = Layout.StartZ + 1f;
            var line = new Rect(-3f, from, 6f, Layout.GateZ - from);

            foreach (var p in Plan())
            {
                if (p.Height <= DressingPlan.LowPropMax) continue;
                if (p.MinY >= DressingPlan.Headroom) continue;
                if (p.Zone == DressZone.Shed) continue;      // the hutch's own footprint is already solid

                // Cover dressing is exempt because the cover BLOCKS already are: the gameplay
                // stream's own invariant (BackyardCover.CentreLaneHalfWidth) keeps them off this
                // line, and a shrub is allowed to overhang the block it stands on.
                if (p.Zone == DressZone.Cover) continue;

                Assert.IsFalse(line.Overlaps(p.Footprint),
                    $"{p.Model} at {p.Position} stands on the mission line");
            }
        }

        [Test]
        public void ThePlan_LeavesTheBossDoorwayOpen()
        {
            var doorway = new Rect(-(Layout.GateHalfWidth - 0.1f), Layout.GateZ - 1.5f,
                                   (Layout.GateHalfWidth - 0.1f) * 2f, 3f);

            foreach (var p in Plan())
            {
                if (p.Height <= DressingPlan.LowPropMax) continue;

                Assert.IsFalse(doorway.Overlaps(p.Footprint),
                    $"{p.Model} at {p.Position} blocks the way into the boss arena");
            }
        }

        [Test]
        public void ThePlan_NeverCrowdsTheFactorysSpawnRing()
        {
            // Robots pour out of the hutch on a ring (YT-70). A prop standing on it would have them
            // spawning inside a shrub. The shed's own dressing is exempt: it sits ON the hutch.
            var hutch = new Vector2(0f, ShedZ);

            foreach (var p in Plan())
            {
                if (p.Zone == DressZone.Shed) continue;
                if (p.Height <= DressingPlan.LowPropMax) continue;

                float d = Vector2.Distance(new Vector2(p.Position.x, p.Position.z), hutch);
                Assert.Greater(d, SpawnRadius + 0.5f,
                    $"{p.Model} at {p.Position} stands on the robots' spawn ring");
            }
        }

        [Test]
        public void TheFence_RunsRightRoundTheYardAndStopsAtTheDoorway()
        {
            var fence = Plan().Where(p => p.Model == KitModels.FencePanel || p.Model == KitModels.FenceGate)
                              .ToList();

            Assert.Greater(fence.Count, 60, "the yard isn't fenced");
            Assert.AreEqual(1, fence.Count(p => p.Model == KitModels.FenceGate),
                "the yard should have exactly one gate — the one Max came in through");

            // Every wall line has fence on it: sample the middle of each and expect a panel nearby.
            var walls = new[]
            {
                new Vector2(-Layout.LawnHalfWidth, 8f), new Vector2(Layout.LawnHalfWidth, 8f),
                new Vector2(-Layout.ArenaHalfWidth, 33f), new Vector2(Layout.ArenaHalfWidth, 33f),
                new Vector2(0f, Layout.StartZ), new Vector2(0f, Layout.ArenaEndZ),
            };

            foreach (var w in walls)
            {
                bool covered = fence.Any(p =>
                    Vector2.Distance(new Vector2(p.Position.x, p.Position.z), w) < 2.5f);
                Assert.IsTrue(covered, $"no fence along the wall at {w}");
            }

            // …and none of it across the boss doorway. (The patio's back wall has a gate in it on
            // purpose — that one is scenery behind Max, not a wall across his path.)
            foreach (var p in fence.Where(p => Mathf.Abs(p.Position.z - Layout.GateZ) < 1f))
                Assert.Greater(Mathf.Abs(p.Position.x) + 0.01f, Layout.GateHalfWidth,
                    $"a fence panel at {p.Position} would have to be walked through to reach the boss");
        }

        [Test]
        public void TheYard_ActuallyLooksLikeAGarden()
        {
            var plan = Plan();

            Assert.GreaterOrEqual(plan.Count(p => p.Model != null && p.Model.StartsWith("tree_")), 4,
                "a backyard has trees in it");
            Assert.GreaterOrEqual(plan.Count(p => p.Model != null && p.Model.StartsWith("flower_")), 20,
                "a gardened yard has flowers in it");
            Assert.GreaterOrEqual(plan.Count(p => p.Model == KitModels.BedRow || p.Model == KitModels.BedEnd), 3,
                "somebody dug a flower bed");
            Assert.GreaterOrEqual(plan.Count(p => p.Model != null && p.Model.StartsWith("path_stone")), 5,
                "the stepping stones that show the way out of the patio");
            Assert.GreaterOrEqual(plan.Count(p => p.Zone == DressZone.Shed), 4,
                "the Mower Hutch should have been made into a shed");
        }

        [Test]
        public void EachCoverBlock_IsDressedAsTheThingItIsNamedAfter()
        {
            var dressed = Plan().Where(p => p.Zone == DressZone.Cover).ToList();
            Assert.IsNotEmpty(dressed, "the cover blocks are still bare greybox");

            foreach (var c in Cover)
            {
                bool onIt = dressed.Any(p => c.DistanceTo(new Vector2(p.Position.x, p.Position.z)) < 0.5f);
                Assert.IsTrue(onIt, $"{c.Name} has nothing standing on it");
            }
        }

        [Test]
        public void ThePlan_StaysWithinTheFrameBudget()
        {
            // Every prop is a draw call unless it batches, and the kit is only cheap while it's
            // small. If a change wants more than this, it wants a look at the frame time first.
            Assert.LessOrEqual(Plan().Count, 400, "the yard has grown a forest");
        }

        [Test]
        public void Validate_RejectsAShrubInTheDoorway()
        {
            var bad = new List<DressProp>
            {
                new DressProp(KitModels.Bushes[0], new Vector3(0f, 0f, Layout.GateZ),
                              new Vector3(1.4f, 1.4f, 1.4f)),
            };

            Assert.IsFalse(DressingPlan.Validate(Layout, bad, ShedZ, Cover, out string reason));
            Assert.That(reason, Does.Contain("fight"));
        }

        [Test]
        public void Validate_RejectsAPropStandingOffTheEdgeOfTheYard()
        {
            var bad = new List<DressProp>
            {
                new DressProp(KitModels.TreeTall, new Vector3(0f, 0f, 80f), new Vector3(3f, 6f, 3f)),
            };

            Assert.IsFalse(DressingPlan.Validate(Layout, bad, ShedZ, Cover, out string reason));
            Assert.That(reason, Does.Contain("edge of the yard"));
        }

        // --- the kit itself ---------------------------------------------------------------------

        [Test]
        public void EveryModelThePlanAsksFor_IsActuallyInTheKit()
        {
            string dir = Path.Combine(Application.dataPath, "_Project/Resources/GardenKit");
            Assert.IsTrue(Directory.Exists(dir), $"the kit folder is missing: {dir}");

            foreach (var p in Plan())
            {
                if (p.Model == null) continue;   // the shed's plain timber
                Assert.IsTrue(File.Exists(Path.Combine(dir, p.Model + ".fbx")),
                    $"the plan places '{p.Model}', which isn't in the kit");
            }
        }

        [Test]
        public void EverySurfaceInTheKit_HasAColourWeChose()
        {
            // The kit's models are split by material name (bark, leafs, timber, stone…) and those
            // names are the only thing telling us what a prop is made of. If one of them isn't in
            // the palette, that surface silently falls back to a flat prop grey — which is exactly
            // the "imported asset pack" look the recolour exists to prevent.
            var used = new SortedSet<string>();

            foreach (string model in KitModelsInPlan())
            {
                var prefab = GardenKit.Prefab(model);
                Assert.IsNotNull(prefab, $"'{model}' didn't load from Resources/{GardenKit.ResourceRoot}");

                foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>())
                foreach (var m in r.sharedMaterials)
                    if (m != null) used.Add(m.name);
            }

            Assert.IsNotEmpty(used, "the kit imported with no materials at all — the names are gone");

            foreach (string name in used)
                Assert.IsTrue(KitMaterials.Knows(name),
                    $"the kit uses a surface called '{name}' and we never chose a colour for it");
        }

        [Test]
        public void NoKitColour_ClipsToCreamInTheYardsSunlight()
        {
            // The yard is lit by a 2.2-intensity key and URP/Lit multiplies albedo by it, so a fence
            // painted the honest pine colour off a swatch comes out cream in every sunlit panel.
            // That is not a hypothetical — it's what the first pass at this palette did, and it's
            // invisible in a colour picker. The ceiling is the fix; this is what holds it.
            float key = BackyardLook.Default.KeyIntensity;
            Assert.Greater(key, 1f, "this test only means anything under a key light brighter than 1");

            foreach (string name in KitMaterials.Names)
            {
                Color c = KitMaterials.ColorOf(name);
                Assert.LessOrEqual(c.maxColorComponent, KitMaterials.SunlitAlbedoCeiling,
                    $"'{name}' is too bright to survive a {key:0.0}× key — it will read as cream, not as {name}");
            }
        }

        private static IEnumerable<string> KitModelsInPlan() =>
            Plan().Select(p => p.Model).Where(m => m != null).Distinct();
    }
}
