using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-781: World 2's floor is one flat value across ~55% of the frame, and 25 authored
    /// <c>world2_config.json</c> grates build nothing (<see cref="MapData.EntityKind"/> had no
    /// <c>Grate</c> case at all) — a Lurker rose out of blank concrete. This fails to COMPILE on base
    /// commit ac69810: <c>EntityKind.Grate</c>, <c>StormdrainKit.BuildGrate</c>/<c>Soffit</c>-tier
    /// grille/<c>StandingWater</c> colours and <c>StormdrainDressing.JointRects</c>/<c>PatchRects</c>
    /// do not exist there.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values only (Rule 2,
    /// Tier 2): the map actually built from World 2's own shipped config carries 25 Grate entities at
    /// the exact authored coordinates; each built grate actually has 7 grille-bar children and no
    /// Collider anywhere in its hierarchy; the grille and standing-water MATERIALS actually resolve
    /// darker/lighter than the actual World 2 floor material by the ticket's own margins; and the pure
    /// joint/patch layout functions are actually deterministic (same call, same output) and actually
    /// avoid every authored grate/deck/ramp/hatch/sludge rect.
    /// </summary>
    public sealed class MV781FloorCompositionTests
    {
        [Test]
        public void FloorComposition_BuildsAuthoredGratesAndDeterministicNonOverlappingJointsAndPatches()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            AssertGrateEntitiesMatchAuthoredCoordinates(cfg, map);

            var host = new GameObject("MV781 host").transform;
            try
            {
                MapRuntime.Build(map, host);
                AssertBuiltGratesHaveSevenGrilleBarsAndNoCollider(map, host);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host.gameObject);
            }

            AssertGrilleBelowFloorAndWaterSeparatedFromFloor();
            AssertJointAndPatchDeterminismAndNoOverlap(map);
        }

        private static void AssertGrateEntitiesMatchAuthoredCoordinates(WorldConfig cfg, MapData map)
        {
            var authored = new List<(float x, float z)>();
            foreach (WorldArea a in cfg.areas)
                foreach (WorldGrate g in a.grates ?? Array.Empty<WorldGrate>())
                    authored.Add((g.x, g.z));

            Assert.AreEqual(25, authored.Count,
                "world2_config.json's own authored grate count changed underneath this test");

            var built = map.entities.Where(e => e != null && e.Kind == EntityKind.Grate)
                .Select(e => (e.x, e.z)).OrderBy(p => p.x).ThenBy(p => p.z).ToList();
            var expected = authored.OrderBy(p => p.x).ThenBy(p => p.z).ToList();

            Assert.AreEqual(25, built.Count, "the built map must contain exactly 25 Grate entities");
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i].x, built[i].x, 0.001f, $"grate {i} X does not match world2_config.json");
                Assert.AreEqual(expected[i].z, built[i].z, 0.001f, $"grate {i} Z does not match world2_config.json");
            }
        }

        private static void AssertBuiltGratesHaveSevenGrilleBarsAndNoCollider(MapData map, Transform host)
        {
            var grateEntities = map.entities.Where(e => e != null && e.Kind == EntityKind.Grate).ToList();
            Assert.IsNotEmpty(grateEntities, "no Grate entities were resolved from the loaded map");

            Transform[] allTransforms = host.GetComponentsInChildren<Transform>(true);
            foreach (MapEntity e in grateEntities)
            {
                Transform built = allTransforms.FirstOrDefault(t => t.name == e.id);
                Assert.IsNotNull(built, $"grate '{e.id}' was never built");

                int grilleBars = 0;
                foreach (Transform child in built)
                    if (child.name.StartsWith("Grille Bar")) grilleBars++;
                Assert.AreEqual(7, grilleBars, $"grate '{e.id}' must have exactly 7 grille bars, found {grilleBars}");

                Assert.IsEmpty(built.GetComponentsInChildren<Collider>(true),
                    $"grate '{e.id}' must carry no Collider anywhere in its hierarchy — a grate is walked over");
            }
        }

        private static void AssertGrilleBelowFloorAndWaterSeparatedFromFloor()
        {
            BiomePalette previous = MaterialLibrary.Palette;
            try
            {
                MaterialLibrary.Palette = BiomePalette.Stormdrain;
                MaterialLibrary.Clear();

                Material floorMat = MaterialLibrary.Surface(SurfaceKind.Ground);
                Material grilleMat = MaterialLibrary.Tinted(SurfaceKind.Metal, StormdrainKit.Soffit);
                Material waterMat = MaterialLibrary.Tinted(SurfaceKind.Prop, StormdrainKit.StandingWater);
                Assert.IsNotNull(floorMat, "World 2's floor material failed to build");

                float floorLuma = MeanAlbedoLuma(floorMat);
                float grilleLuma = MeanAlbedoLuma(grilleMat);
                float waterLuma = MeanAlbedoLuma(waterMat);

                Assert.Less(grilleLuma, floorLuma,
                    $"the grille-bar material ({grilleLuma:F1} luma) must resolve darker than the World 2 " +
                    $"floor material ({floorLuma:F1} luma) — it is the floor's darkest tier");
                // MV-783 retoned the whole Stormdrain base onto an approved cool concrete, deliberately
                // DARKER StandingWater than the new floor (a wet pool reading as depth against a lighter
                // surround, rather than the brightest thing in the room MV-781/782 originally called for)
                // — a direction flip, not just a magnitude drift, so the assertion itself now runs the
                // other way. Still a real, checked separation: this catches standing water collapsing
                // onto the floor it sits on, whichever side of it that separation lands.
                Assert.That(floorLuma - waterLuma, Is.GreaterThanOrEqualTo(10f),
                    $"standing water ({waterLuma:F1} luma) must resolve at least 10 luma darker than the floor " +
                    $"({floorLuma:F1} luma)");
            }
            finally
            {
                MaterialLibrary.Palette = previous;
                MaterialLibrary.Clear();
            }
        }

        private static void AssertJointAndPatchDeterminismAndNoOverlap(MapData map)
        {
            var obstacles = new List<Rect>();
            foreach (MapEntity e in map.entities)
            {
                if (e == null) continue;
                if (e.Kind != EntityKind.Grate && e.Kind != EntityKind.Deck && e.Kind != EntityKind.Ramp &&
                    e.Kind != EntityKind.Hatch && e.Kind != EntityKind.Sludge) continue;

                Rect r = e.Kind == EntityKind.Grate
                    ? new Rect(e.x, e.z, e.width, e.depth)
                    : new Rect(e.x - e.width * 0.5f, e.z - e.depth * 0.5f, e.width, e.depth);
                obstacles.Add(r);
            }

            bool anyFloorZone = false;
            foreach (MapZone zone in map.zones)
            {
                if (zone == null || zone.level > 0) continue;
                anyFloorZone = true;

                Rect rect = zone.Footprint;
                List<Rect> joints1 = StormdrainDressing.JointRects(rect, obstacles);
                List<Rect> joints2 = StormdrainDressing.JointRects(rect, obstacles);
                Assert.AreEqual(joints1.Count, joints2.Count, $"zone '{zone.id}': joint count is not deterministic");
                for (int i = 0; i < joints1.Count; i++)
                    Assert.AreEqual(joints1[i], joints2[i], $"zone '{zone.id}': joint {i} position is not deterministic");

                List<(Rect rect, bool isWater)> patches1 = StormdrainDressing.PatchRects(rect, zone.id, obstacles);
                List<(Rect rect, bool isWater)> patches2 = StormdrainDressing.PatchRects(rect, zone.id, obstacles);
                Assert.AreEqual(patches1.Count, patches2.Count, $"zone '{zone.id}': patch count is not deterministic");
                for (int i = 0; i < patches1.Count; i++)
                {
                    Assert.AreEqual(patches1[i].rect, patches2[i].rect,
                        $"zone '{zone.id}': patch {i} position is not deterministic");
                    Assert.AreEqual(patches1[i].isWater, patches2[i].isWater,
                        $"zone '{zone.id}': patch {i} silt/water flag is not deterministic");
                }

                foreach (Rect j in joints1)
                    foreach (Rect o in obstacles)
                        Assert.IsFalse(j.Overlaps(o), $"zone '{zone.id}': a panel joint crosses obstacle rect {o}");

                foreach ((Rect pRect, bool _) in patches1)
                    foreach (Rect o in obstacles)
                        Assert.IsFalse(pRect.Overlaps(o), $"zone '{zone.id}': a patch crosses obstacle rect {o}");
            }
            Assert.IsTrue(anyFloorZone, "World 2's map has no floor-level zones to test against");
        }

        /// <summary>Mean luma (0-255) of a material's own baked albedo texture — the same "read the
        /// resolved texture back" idiom <c>MV777FrameContrastTests</c>/<c>MV780FactoryToneTests</c>
        /// already use for a <c>MaterialLibrary</c>-built material.</summary>
        private static float MeanAlbedoLuma(Material m)
        {
            Texture2D tex = (m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null) as Texture2D
                          ?? m.mainTexture as Texture2D;
            Assert.IsNotNull(tex, $"material '{m.name}' has no readable albedo texture to sample");

            Color32[] px = tex.GetPixels32();
            double sum = 0;
            foreach (Color32 c in px) sum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
            return (float)(sum / px.Length);
        }
    }
}
