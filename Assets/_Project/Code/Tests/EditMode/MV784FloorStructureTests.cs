using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-784: World 2's floor is one flat plane with axis-aligned rectangles lifted onto it — a
    /// rectangle on a plane reads as a tile, and the floor beneath has no structure of its own. This
    /// fails to COMPILE on base commit 7246a19ea634152c9eb582d8d9b95b9aeb6773a5:
    /// <c>StormdrainDressing.BayRects</c>/<c>SiltRects</c>/<c>WaterRects</c> and
    /// <c>StormdrainKit.BuildBay</c>/<c>BuildCrack</c>/<c>BuildSiltStain</c>/<c>BuildWaterStain</c>/
    /// <c>GroundDry</c>/<c>GroundBase</c>/<c>GroundAccent</c>/<c>Crack</c> do not exist there.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values only (Rule 2,
    /// Tier 2): the largest floor-level area in World 2's own shipped config actually casts at least
    /// floor(w/3.2)*floor(d/3.2) bay slabs whose actually-built materials resolve to exactly three
    /// distinct luminances; every silt/water stain mesh actually built has at least 9 perimeter
    /// vertices; the standing-water and silt CORE materials actually resolve darker/lighter than the
    /// floor; the pure bay/stain/joint layout functions are actually deterministic (same call, same
    /// sorted output); and every renderer this ticket builds actually resolves <c>_OutlineOn</c> to 0.
    /// </summary>
    public sealed class MV784FloorStructureTests
    {
        [Test]
        public void FloorStructure_CastsThreeToneBaysWithStainsAndNoFloorLevelOutline()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            MapZone zone = LargestFloorZone(map);
            Assert.IsNotNull(zone, "World 2's map has no floor-level zones to test against");
            Rect zoneRect = zone.Footprint;

            List<Rect> obstacles = FloorObstacles(map);

            AssertDeterminism(zoneRect, zone.id, obstacles);

            var host = new GameObject("MV784 host").transform;
            BiomePalette previous = MaterialLibrary.Palette;
            try
            {
                MaterialLibrary.Palette = BiomePalette.Stormdrain;
                MaterialLibrary.Clear();

                List<StormdrainDressing.Bay> bays = StormdrainDressing.BayRects(zoneRect, zone.id);
                int expectedMinimum = Mathf.FloorToInt(zoneRect.width / 3.2f) * Mathf.FloorToInt(zoneRect.height / 3.2f);
                Assert.GreaterOrEqual(bays.Count, expectedMinimum,
                    $"zone '{zone.id}' ({zoneRect.width:F1}x{zoneRect.height:F1}) must cast at least " +
                    $"{expectedMinimum} bay slabs, cast {bays.Count}");

                var lumas = new HashSet<int>();
                foreach (StormdrainDressing.Bay bay in bays)
                {
                    GameObject go = StormdrainKit.BuildBay(host, bay.Rect, bay.Tone);
                    lumas.Add(Mathf.RoundToInt(MeanAlbedoLuma(RequireMaterial(go))));
                    if (bay.HasCrack)
                        AssertOutlineOff(StormdrainKit.BuildCrack(host,
                            new Vector3(bay.Rect.center.x, 0f, bay.Rect.center.y), bay.CrackHash));
                    AssertOutlineOff(go);
                }
                Assert.AreEqual(3, lumas.Count,
                    $"expected exactly 3 distinct bay material luminances (dark/base/light), found " +
                    $"{lumas.Count}: [{string.Join(", ", lumas)}]");

                foreach (Rect seg in StormdrainDressing.JointRects(zoneRect, obstacles))
                    AssertOutlineOff(StormdrainKit.BuildPanelJoint(host, seg));

                float floorLuma = MeanAlbedoLuma(MaterialLibrary.Tinted(SurfaceKind.Stone, StormdrainKit.GroundBase));

                List<StormdrainDressing.Stain> silt = StormdrainDressing.SiltRects(zoneRect, zone.id, obstacles);
                Assert.IsNotEmpty(silt, $"zone '{zone.id}' placed no silt drifts at all");
                foreach (StormdrainDressing.Stain s in silt)
                {
                    GameObject go = StormdrainKit.BuildSiltStain(host,
                        new Vector3(s.Center.x, 0f, s.Center.y), s.CoreRadius, s.Seed);
                    AssertStainLayersOk(go, floorLuma, isWater: false);
                }

                List<StormdrainDressing.Stain> water = StormdrainDressing.WaterRects(zoneRect, zone.id, obstacles);
                Assert.IsNotEmpty(water, $"zone '{zone.id}' placed no standing water at all");
                foreach (StormdrainDressing.Stain s in water)
                {
                    GameObject go = StormdrainKit.BuildWaterStain(host,
                        new Vector3(s.Center.x, 0f, s.Center.y), s.CoreRadius, s.Seed);
                    AssertStainLayersOk(go, floorLuma, isWater: true);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host.gameObject);
                MaterialLibrary.Palette = previous;
                MaterialLibrary.Clear();
            }
        }

        private static MapZone LargestFloorZone(MapData map)
        {
            MapZone best = null;
            float bestArea = -1f;
            foreach (MapZone zone in map.zones)
            {
                if (zone == null || zone.level > 0) continue;
                Rect r = zone.Footprint;
                float area = r.width * r.height;
                if (area > bestArea) { bestArea = area; best = zone; }
            }
            return best;
        }

        private static List<Rect> FloorObstacles(MapData map)
        {
            var rects = new List<Rect>();
            foreach (MapEntity e in map.entities)
            {
                if (e == null) continue;
                if (e.Kind != EntityKind.Grate && e.Kind != EntityKind.Deck && e.Kind != EntityKind.Ramp &&
                    e.Kind != EntityKind.Hatch && e.Kind != EntityKind.Sludge) continue;

                Rect r = e.Kind == EntityKind.Grate
                    ? new Rect(e.x, e.z, e.width, e.depth)
                    : new Rect(e.x - e.width * 0.5f, e.z - e.depth * 0.5f, e.width, e.depth);
                rects.Add(r);
            }
            return rects;
        }

        private static void AssertDeterminism(Rect zoneRect, string zoneId, List<Rect> obstacles)
        {
            List<StormdrainDressing.Bay> bays1 = StormdrainDressing.BayRects(zoneRect, zoneId)
                .OrderBy(b => b.Rect.x).ThenBy(b => b.Rect.y).ToList();
            List<StormdrainDressing.Bay> bays2 = StormdrainDressing.BayRects(zoneRect, zoneId)
                .OrderBy(b => b.Rect.x).ThenBy(b => b.Rect.y).ToList();
            Assert.AreEqual(bays1.Count, bays2.Count, "bay count is not deterministic");
            for (int i = 0; i < bays1.Count; i++)
            {
                Assert.AreEqual(bays1[i].Rect, bays2[i].Rect, $"bay {i} position is not deterministic");
                Assert.AreEqual(bays1[i].Tone, bays2[i].Tone, $"bay {i} tone is not deterministic");
                Assert.AreEqual(bays1[i].HasCrack, bays2[i].HasCrack, $"bay {i} crack is not deterministic");
            }

            List<Rect> joints1 = StormdrainDressing.JointRects(zoneRect, obstacles)
                .OrderBy(r => r.x).ThenBy(r => r.y).ToList();
            List<Rect> joints2 = StormdrainDressing.JointRects(zoneRect, obstacles)
                .OrderBy(r => r.x).ThenBy(r => r.y).ToList();
            Assert.AreEqual(joints1.Count, joints2.Count, "joint count is not deterministic");
            for (int i = 0; i < joints1.Count; i++)
                Assert.AreEqual(joints1[i], joints2[i], $"joint {i} is not deterministic");

            AssertStainDeterminism(StormdrainDressing.SiltRects(zoneRect, zoneId, obstacles),
                StormdrainDressing.SiltRects(zoneRect, zoneId, obstacles), "silt");
            AssertStainDeterminism(StormdrainDressing.WaterRects(zoneRect, zoneId, obstacles),
                StormdrainDressing.WaterRects(zoneRect, zoneId, obstacles), "water");
        }

        private static void AssertStainDeterminism(List<StormdrainDressing.Stain> a, List<StormdrainDressing.Stain> b,
                                                    string label)
        {
            List<StormdrainDressing.Stain> sa = a.OrderBy(s => s.Center.x).ThenBy(s => s.Center.y).ToList();
            List<StormdrainDressing.Stain> sb = b.OrderBy(s => s.Center.x).ThenBy(s => s.Center.y).ToList();
            Assert.AreEqual(sa.Count, sb.Count, $"{label} count is not deterministic");
            for (int i = 0; i < sa.Count; i++)
            {
                Assert.AreEqual(sa[i].Center, sb[i].Center, $"{label} {i} position is not deterministic");
                Assert.AreEqual(sa[i].CoreRadius, sb[i].CoreRadius, $"{label} {i} radius is not deterministic");
            }
        }

        private static void AssertStainLayersOk(GameObject stain, float floorLuma, bool isWater)
        {
            Transform[] layers = stain.GetComponentsInChildren<Transform>(true)
                .Where(t => t.GetComponent<MeshFilter>() != null).ToArray();
            Assert.AreEqual(2, layers.Length,
                $"'{stain.name}' must be built from exactly two stacked blob layers, found {layers.Length}");

            foreach (Transform layer in layers)
            {
                Mesh mesh = layer.GetComponent<MeshFilter>().sharedMesh;
                int perimeterVertices = mesh.vertexCount - 1; // one shared centre vertex per blob
                Assert.GreaterOrEqual(perimeterVertices, 9,
                    $"'{stain.name}/{layer.name}' mesh has only {perimeterVertices} perimeter vertices");
                AssertOutlineOff(layer.gameObject);
            }

            // The denser layer (named "Core"/"Water" by its own builder) is the one the ticket's colour
            // rule is actually about — the wider halo/meniscus underneath is a blend/brightened tone,
            // not the pinned Silt/StandingWater constant, so only the core's luma is checked against
            // the floor here. Found by name, not by mesh bounds: a halo/meniscus radius is only a
            // little wider than its own core's, well inside the per-segment jitter's own spread, so
            // bounds size cannot reliably tell the two layers apart.
            string coreName = isWater ? "Water" : "Core";
            Transform core = layers.FirstOrDefault(t => t.name == coreName);
            Assert.IsNotNull(core, $"'{stain.name}' has no '{coreName}' layer");
            float coreLuma = MeanAlbedoLuma(RequireMaterial(core.gameObject));
            if (isWater)
                Assert.Less(coreLuma, floorLuma,
                    $"standing-water core ({coreLuma:F1} luma) must resolve darker than the floor ({floorLuma:F1} luma)");
            else
                Assert.Greater(coreLuma, floorLuma,
                    $"silt core ({coreLuma:F1} luma) must resolve brighter than the floor ({floorLuma:F1} luma)");
        }

        private static Material RequireMaterial(GameObject go)
        {
            Material m = go.GetComponent<Renderer>()?.sharedMaterial;
            Assert.IsNotNull(m, $"'{go.name}' has no material to resolve");
            return m;
        }

        private static void AssertOutlineOff(GameObject go)
        {
            Material m = RequireMaterial(go);
            float outline = m.HasProperty("_OutlineOn") ? m.GetFloat("_OutlineOn") : 0f;
            Assert.AreEqual(0f, outline, $"'{go.name}' must resolve _OutlineOn to 0 at floor level");
        }

        /// <summary>Mean luma (0-255) of a material's own baked albedo texture — the same "read the
        /// resolved texture back" idiom <c>MV781FloorCompositionTests</c> already uses.</summary>
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
