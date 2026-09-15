using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-799: World 2's floor was lit uniformly — the "fittings" only ever dropped a +13% additive
    /// wash over a 2 m circle on a floor that was already one flat tone everywhere, so the room read as
    /// having no light variation even after MV-787 shipped. This fails on base commit f89c4e7 (today
    /// every bay resolves to one of three fixed tones regardless of distance to any fitting — the
    /// darkest/brightest ratio is ~0.68, not <= 0.50, and neither distribution clause below holds).
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values only (Rule 2,
    /// Tier 2): every cast bay's ACTUAL BUILT MATERIAL resolves a luminance driven by its distance to
    /// the nearest fitting, not by its authored tone constant.
    /// </summary>
    public sealed class MV799LitGroundTests
    {
        private const float NearFittingRadius = 2.0f;
        private const float FarFittingRadius = 7.0f;
        private const float MaxDarkToLightRatio = 0.50f;

        [Test]
        public void FloorLuminance_TracksDistanceToNearestFitting()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV799 host").transform;
            BiomePalette previousPalette = MaterialLibrary.Palette;
            try
            {
                MaterialLibrary.Palette = BiomePalette.Stormdrain;
                MaterialLibrary.Clear();

                MapBuild build = MapRuntime.Build(map, host);
                StormdrainDressing.Dress(host, map, build.Cover);

                Transform dressingHost = host.Find("Stormdrain Dressing");
                Assert.IsNotNull(dressingHost, "the dressing host was never built");

                List<Vector2> fittingPositions = FindFittingPositions(dressingHost);
                Assert.GreaterOrEqual(fittingPositions.Count, 3,
                    "World 2 must build at least three wall fittings for this test to mean anything");

                Transform floorComposition = dressingHost.Find("Floor Composition");
                Assert.IsNotNull(floorComposition, "the floor composition host was never built");

                // Scoped to ONE room (the largest floor zone, same helper MV784FloorStructureTests
                // already uses) rather than the whole multi-room map: a connector stub half a map away
                // from every fitting is a real "far" bay by Euclidean distance but tells this test
                // nothing about whether THIS room's own light/dark structure tracks its own fittings.
                Rect zoneRect = LargestFloorZone(map).Footprint;
                var bays = floorComposition.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name == "Bay" && zoneRect.Contains(new Vector2(t.position.x, t.position.z)))
                    .Select(t => (pos: new Vector2(t.position.x, t.position.z),
                                  luma: MeanAlbedoLuma(RequireMaterial(t.gameObject))))
                    .ToList();
                Assert.GreaterOrEqual(bays.Count, 3,
                    "World 2's largest floor zone must cast at least three bay slabs for this test to mean anything");

                float darkest = bays.Min(b => b.luma);
                float brightest = bays.Max(b => b.luma);
                Assert.Greater(brightest, 0f, "brightest bay resolved to zero luminance");

                Assert.LessOrEqual(darkest / brightest, MaxDarkToLightRatio,
                    $"darkest bay ({darkest:F1}) / brightest bay ({brightest:F1}) = " +
                    $"{darkest / brightest:F3}, must be <= {MaxDarkToLightRatio:F2}");

                // "Sits in the top/bottom third" is asserted as a POPULATION SEPARATION: every bay
                // within NearFittingRadius of a fitting must out-resolve every bay beyond
                // FarFittingRadius of every fitting. A three-way split of either the numeric min-max
                // span or the population's own rank isn't reachable here regardless of correctness:
                // World 2's three authored bay tones already span a real luminance ratio on their own
                // (this test's own darkest-observed/brightest-observed check above), so either kind of
                // three-way split can demand a bay wearing the darkest authored tone out-resolve one
                // wearing the brightest even at this multiplier's own maximum — never achievable, tone
                // aside. The separation below IS reachable by construction: at NearFittingRadius a
                // single fitting alone already yields m >= 0.38 + (1 - 2/4.6)^1.35 * 0.62 ~= 0.67, so
                // even the darkest authored tone (GroundDry) resolves brighter than the brightest
                // authored tone (GroundAccent) at FarFittingRadius's m == the unlit floor of 0.38.
                float dimmestNear = float.PositiveInfinity;
                float brightestFar = float.NegativeInfinity;
                int nearChecked = 0, farChecked = 0;
                foreach (var (pos, luma) in bays)
                {
                    float distToNearest = fittingPositions.Min(f => Vector2.Distance(pos, f));

                    if (distToNearest <= NearFittingRadius)
                    {
                        nearChecked++;
                        dimmestNear = Mathf.Min(dimmestNear, luma);
                    }
                    else if (distToNearest > FarFittingRadius)
                    {
                        farChecked++;
                        brightestFar = Mathf.Max(brightestFar, luma);
                    }
                }

                Assert.Greater(nearChecked, 0,
                    "no bay sits within 2.0 m of a fitting — this test would pass on garbage");
                Assert.Greater(farChecked, 0,
                    "no bay sits more than 7.0 m from every fitting — this test would pass on garbage");

                Assert.Greater(dimmestNear, brightestFar,
                    $"the dimmest bay within {NearFittingRadius} m of a fitting ({dimmestNear:F1}) does not " +
                    $"out-resolve the brightest bay more than {FarFittingRadius} m from every fitting " +
                    $"({brightestFar:F1}) — distance to a fitting is not actually separating lit floor from " +
                    "dark floor");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
                MaterialLibrary.Palette = previousPalette;
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

        /// <summary>Every fitting the lit-ground post-pass actually reads (MV-799, change 1): bulkhead
        /// lamps (steady and hazard) and every individual kerb-strip segment — the same set
        /// <c>StormdrainDressing.Dress</c> collects, found here by the same names its own dressing pass
        /// builds them under rather than re-derived.</summary>
        private static List<Vector2> FindFittingPositions(Transform dressingHost)
        {
            var result = new List<Vector2>();
            foreach (Transform t in dressingHost.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Bulkhead Lamp" || t.name == "Hazard Bulkhead")
                    result.Add(new Vector2(t.position.x, t.position.z));
                else if (t.name == "Guide Rail")
                    foreach (Transform seg in t)
                        result.Add(new Vector2(seg.position.x, seg.position.z));
            }
            return result;
        }

        private static Material RequireMaterial(GameObject go)
        {
            Material m = go.GetComponent<Renderer>()?.sharedMaterial;
            Assert.IsNotNull(m, $"'{go.name}' has no material to resolve");
            return m;
        }

        /// <summary>Mean luma (0-255) of a material's own baked albedo texture — the same "read the
        /// resolved texture back" idiom <c>MV784FloorStructureTests</c> already uses.</summary>
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
