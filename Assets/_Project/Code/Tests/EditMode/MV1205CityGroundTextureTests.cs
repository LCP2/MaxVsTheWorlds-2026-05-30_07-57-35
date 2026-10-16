using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1205: Lee, 17 Oct — "the floor on World 3 is nondescript. It doesn't feel like I'm standing
    /// on anything." Before this fix, every City floor renderer carried the generic per-world sweep's
    /// grass-shaded Ground material (<see cref="WorldMaterials.Apply"/> always bakes
    /// <see cref="StylizedTextures.Ground"/>'s lawn height field for <c>SurfaceKind.Ground</c>,
    /// regardless of biome) tinted a flat, near-uniform dark blue-grey — a base map with almost no
    /// luminance spread. <see cref="CityDressing.Dress"/> now re-skins every City floor renderer onto
    /// a dedicated <c>SurfaceKind.Asphalt</c> material (<see cref="StylizedTextures.AsphaltHeight"/>),
    /// and <see cref="CityKit.BuildPavement"/> wears a dedicated <c>SurfaceKind.Pavement</c> material
    /// (<see cref="StylizedTextures.PavingSlabHeight"/>) instead of <c>SurfaceKind.Stone</c>'s generic
    /// slab noise plus hand-painted joint lines.
    ///
    /// Built for real through <see cref="WorldMapLoader.TryLoad"/>, <see cref="MapRuntime.Build"/>,
    /// <see cref="WorldMaterials.Apply"/> and <see cref="CityDressing.Dress"/> — same load path
    /// <see cref="MarketStreetBuildTests"/> already proves out — and asserts RESOLVED values only
    /// (Rule 2, Tier 2): a real baked texture's own pixels, never an authored constant compared to
    /// itself, never a rendered pixel on screen.
    ///
    /// Fails on the base commit (183f27f): the floor's material is the grass-shaded Ground material,
    /// whose baked albedo over <c>BiomePalette.City</c>'s GroundBase/GroundAccent (both near-identical
    /// dark blue-greys, delta &lt; 0.04 per channel) carries a luminance standard deviation far below
    /// 0.02; and <c>CityKit.BuildPavement</c>'s slab wears <c>SurfaceKind.Stone</c>'s own material,
    /// whose baked texture has no joint pattern tied to a resolved "joint vs slab centre" measurement
    /// this test can make (the joints are separate painted boxes, not part of the slab's own texture).
    /// </summary>
    public sealed class MV1205CityGroundTextureTests
    {
        [Test]
        public void CityRoadIsAsphaltAndPavementJointsReadDarkerThanSlabs()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(cfg, "the City's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV1205 City Ground Probe Root").transform;
            try
            {
                MapBuild build = MapRuntime.Build(map, host);
                host.gameObject.AddComponent<WorldMaterials>().Apply(BiomePalette.City);
                CityDressing.Dress(host, map, cfg, build.Cover);

                // ---- AC1a: the road ground reads as asphalt, not flat grass-shaded tint ----
                Renderer floor = FindNamed(host, "Map Floor");
                Assert.IsNotNull(floor, "expected the City's built map to include its floor slab");

                Texture2D albedo = floor.sharedMaterial.GetTexture("_BaseMap") as Texture2D;
                Assert.IsNotNull(albedo, "the City floor must wear a real base map, not the shader's default white");
                Assert.That(albedo.width, Is.GreaterThanOrEqualTo(256),
                    $"the floor's base map ({albedo.width}px) must be at least 256 px");

                float stdDev = LuminanceStdDev(albedo);
                Assert.That(stdDev, Is.GreaterThanOrEqualTo(0.02f),
                    $"the floor's base map luminance std dev ({stdDev:F4}) must be >= 0.02 — a flat grass " +
                    "tint (the pre-fix defect) reads far below this");

                // ---- AC1b: the pavement's joints read darker than its slab centres ----
                Transform pavement = FindDeep(host, "Pavement");
                Assert.IsNotNull(pavement, "CityDressing must still build a1's authored pavement surfaces");
                Transform slab = FindDeep(pavement, "Slab");
                Assert.IsNotNull(slab, "the Pavement builder must still build its own Slab child");

                Texture2D pavingTex = slab.GetComponent<Renderer>().sharedMaterial.GetTexture("_BaseMap") as Texture2D;
                Assert.IsNotNull(pavingTex, "the pavement slab must wear a real base map");

                (float darkest, float brightest) = LuminanceRange(pavingTex);
                Assert.That(darkest, Is.LessThan(brightest - 0.05f),
                    $"the pavement's joint pixels (darkest {darkest:F3}) must read measurably darker than " +
                    $"its slab-centre pixels (brightest {brightest:F3})");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        private static float Luminance(Color32 p) => (p.r + p.g + p.b) / (3f * 255f);

        private static float LuminanceStdDev(Texture2D tex)
        {
            Color32[] px = tex.GetPixels32();
            float mean = 0f;
            foreach (Color32 p in px) mean += Luminance(p);
            mean /= px.Length;

            float variance = 0f;
            foreach (Color32 p in px)
            {
                float d = Luminance(p) - mean;
                variance += d * d;
            }
            variance /= px.Length;
            return Mathf.Sqrt(variance);
        }

        private static (float darkest, float brightest) LuminanceRange(Texture2D tex)
        {
            Color32[] px = tex.GetPixels32();
            float min = float.MaxValue, max = float.MinValue;
            foreach (Color32 p in px)
            {
                float l = Luminance(p);
                if (l < min) min = l;
                if (l > max) max = l;
            }
            return (min, max);
        }

        private static Renderer FindNamed(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.GetComponent<Renderer>();
            return null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
