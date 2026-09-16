using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-824: World 2's unlit floor was unreadable -- MV-799's own <c>LitGroundBaseMultiplier</c> of
    /// 0.38 left the floor's ~0.049 linear base at ~0.019 linear, effectively black, so a bay's own
    /// cast joints, cracks and silt drifts could never be seen far from a fitting (Lee, build 302e10e).
    /// Fails on base commit e6c2c74 -- the multiplier is still 0.38 there, so a3's farthest-from-any-
    /// fitting bay resolves at roughly 0.38x its unmodulated tone, well under the 0.72x floor this test
    /// demands -- see the fix comment for the captured failure output.
    ///
    /// One EditMode test (testing policy MV-465, Rule 1) over a real World 2 area (a3), asserting a
    /// RESOLVED value (Rule 2, Tier 2): the actual built floor bay farthest from any fitting must
    /// resolve at least 0.72x its own unmodulated tone's baked albedo, and the bay nearest a fitting
    /// must never resolve past 1.30x -- MV-799's own lit peak, unchanged by this ticket.
    /// </summary>
    public sealed class MV824LitGroundReadabilityTests
    {
        private const float LitGroundBaseMultiplier = 0.72f;
        private const float LitGroundMaxMultiplier = 1.30f;
        private const float BaseMultiplierTolerance = 0.01f;

        [Test]
        public void FarthestBayInA3_ReadsAtLeastTheLitGroundBase_NearestBay_NeverExceedsThePeak()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV824 host").transform;
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

                // World 2's JSON authors this area as "a3" (index 3), but WorldMapLoader renames every
                // combat area 1..dials.areaCount to the old engine's "area<N>" convention before it ever
                // becomes a MapZone (WorldMapLoader.cs ~line 106) -- entity ids under it (a3_cover1,
                // a3_sludge1, ...) keep the authored prefix, but the zone itself resolves as "area3".
                MapZone a3 = map.zones.FirstOrDefault(z => z != null && z.id == "area3" && z.level == 0);
                Assert.IsNotNull(a3, "World 2's own shipped map must carry a level-0 zone 'area3' (authored area id 'a3')");
                Rect a3Rect = a3.Footprint;

                List<Transform> a3Bays = floorComposition.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name == "Bay" && a3Rect.Contains(new Vector2(t.position.x, t.position.z)))
                    .ToList();
                Assert.GreaterOrEqual(a3Bays.Count, 2,
                    "a3 must carry at least two built floor bays for this test to mean anything");

                // The SAME deterministic tone-per-bay draw StormdrainDressing.Dress already used to
                // build these -- BayRects is a pure function of the zone rect + id, so recomputing it
                // with no channel filter still yields the exact Rect+Tone pair for every bay that
                // actually exists (a channel-eligible bay production skipped just never finds a match
                // below), never a re-derivation of what tone a built bay actually carries.
                List<StormdrainDressing.Bay> a3BayDefs = StormdrainDressing.BayRects(a3Rect, a3.id);

                Transform farthest = null, nearest = null;
                float farthestDist = float.NegativeInfinity, nearestDist = float.PositiveInfinity;
                foreach (Transform bay in a3Bays)
                {
                    var pos = new Vector2(bay.position.x, bay.position.z);
                    float d = fittingPositions.Min(f => Vector2.Distance(pos, f));
                    if (d > farthestDist) { farthestDist = d; farthest = bay; }
                    if (d < nearestDist) { nearestDist = d; nearest = bay; }
                }
                Assert.IsNotNull(farthest, "no farthest bay resolved -- a3 built no bays");
                Assert.IsNotNull(nearest, "no nearest bay resolved -- a3 built no bays");

                float farRatio = ResolvedRatio(farthest, a3BayDefs);
                float nearRatio = ResolvedRatio(nearest, a3BayDefs);

                Assert.GreaterOrEqual(farRatio, LitGroundBaseMultiplier - BaseMultiplierTolerance,
                    $"a3's farthest-from-any-fitting bay ({farthest.name}, {farthestDist:F1} m) resolves at " +
                    $"{farRatio:F3}x its unmodulated tone, under the {LitGroundBaseMultiplier:F2}x unlit floor " +
                    "-- it is too dark to read");
                Assert.LessOrEqual(nearRatio, LitGroundMaxMultiplier + BaseMultiplierTolerance,
                    $"a3's nearest-to-a-fitting bay ({nearest.name}, {nearestDist:F1} m) resolves at " +
                    $"{nearRatio:F3}x its unmodulated tone, over the {LitGroundMaxMultiplier:F2}x lit peak");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
                MaterialLibrary.Palette = previousPalette;
                MaterialLibrary.Clear();
            }
        }

        /// <summary>Actual resolved albedo (the built bay's own baked material) over the SAME bay's
        /// unmodulated tone baked the same way -- both read back with <see cref="MeanAlbedoLuma"/>, so
        /// whatever grain <see cref="MaterialLibrary.Tinted"/> spreads around a tone cancels out of the
        /// ratio rather than needing to be modelled.</summary>
        private static float ResolvedRatio(Transform bay, List<StormdrainDressing.Bay> defs)
        {
            var pos = new Vector2(bay.position.x, bay.position.z);
            StormdrainDressing.Bay def = defs.First(b => Vector2.Distance(b.Rect.center, pos) < 0.01f);

            Material actual = RequireMaterial(bay.gameObject);
            Material unmodulated = MaterialLibrary.Tinted(SurfaceKind.Stone, def.Tone);
            Assert.IsNotNull(unmodulated, $"could not bake a reference material for {bay.name}'s own unmodulated tone");

            float actualLuma = MeanAlbedoLuma(actual);
            float unmodulatedLuma = MeanAlbedoLuma(unmodulated);
            Assert.Greater(unmodulatedLuma, 0f, $"{bay.name}'s unmodulated reference resolved to zero luminance");

            return actualLuma / unmodulatedLuma;
        }

        /// <summary>Every fitting the lit-ground post-pass actually reads (MV-799, change 1): bulkhead
        /// lamps (steady and hazard) and every individual kerb-strip segment -- the same set
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

        /// <summary>Mean luma (0-255) of a material's own baked albedo texture -- the same "read the
        /// resolved texture back" idiom <c>MV799LitGroundTests</c> and <c>MV784FloorStructureTests</c>
        /// already use.</summary>
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
