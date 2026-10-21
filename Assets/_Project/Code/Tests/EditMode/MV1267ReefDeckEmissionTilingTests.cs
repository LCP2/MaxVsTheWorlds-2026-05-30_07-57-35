using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1267: the Reef deck's a1 spawn frame read mauve on WebGL/iOS while the same commit/camera/
    /// tier read navy on Windows standalone. Root cause (code, not a re-bisect — the Jira ticket's own
    /// bisect table ruled out every <c>BackyardLook.Reef</c> post-processing field): <see cref="ReefKit.
    /// ApplyDeckTiling"/> called <c>SetTextureScale("_BaseMap", tiling)</c> to repeat the baked
    /// <see cref="WorldMaterials.ReefDeckGridCells"/>-square supertile once per
    /// <see cref="WorldMaterials.ReefDeckPlateSizeMetres"/>-scaled plate group, but never set a matching
    /// scale on <c>_EmissionMap</c> — a URP Lit property with its own independent <c>_ST</c>, not one
    /// inherited from <c>_BaseMap</c>'s. Left at Unity's default scale (1,1), the circuit-inlay mask
    /// stretches as a single copy across the floor's entire UV span instead of repeating, so its mostly-
    /// black image with a handful of bright cyan/violet/magenta accent texels gets minified back down by
    /// the camera's real viewing distance into a uniform colour wash — the reported mauve, and the same
    /// binding gap behind MV-1256/MV-1265's "circuit inlay doesn't render".
    ///
    /// Fails before the fix: <c>_EmissionMap</c>'s resolved texture scale is Unity's untouched default
    /// (1,1) — equal on both axes and un-related to the floor's own resolved footprint — while
    /// <c>_BaseMap</c>'s is the real per-floor tiling count, so the two diverge. Loads the REAL shipped
    /// World 3 config through the production path (<see cref="WorldLibrary.Load"/> -&gt;
    /// <see cref="WorldMapLoader.TryLoad"/> -&gt; <see cref="MapRuntime.Build"/> -&gt;
    /// <see cref="ReefKit.DressHull"/>, same as <c>MV1256ReefDeckDetailTests</c>), then asserts the
    /// RESOLVED <see cref="Renderer.sharedMaterial"/>'s own <c>GetTextureScale</c> — never a constant
    /// re-read from source.
    /// </summary>
    public sealed class MV1267ReefDeckEmissionTilingTests
    {
        [Test]
        public void World3Floor_EmissionMapTilingMatchesBaseMapTiling()
        {
            WorldConfig cfg3 = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg3, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg3, out MapData map3, out string reason3), reason3);

            var root3 = new GameObject("MV1267 World3 Probe Root");
            try
            {
                MapRuntime.Build(map3, root3.transform);
                var wm = new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
                wm.Apply(WorldCatalog.Get(2).Palette);
                ReefKit.DressHull(root3.transform);

                Renderer floor = FindNamed(root3.transform, "Map Floor");
                Assert.IsNotNull(floor, "expected World 3's built map to include its floor slab");

                Material mat = floor.sharedMaterial;
                Vector2 baseScale = mat.GetTextureScale("_BaseMap");
                Vector2 emissionScale = mat.GetTextureScale("_EmissionMap");

                Assert.AreNotEqual(Vector2.one, baseScale,
                    "expected the floor's own real footprint to resolve a non-trivial _BaseMap tiling " +
                    "(otherwise this test can't tell a correct binding from two defaults happening to match)");
                Assert.AreEqual(baseScale, emissionScale,
                    $"resolved _EmissionMap tiling {emissionScale} must match the resolved _BaseMap " +
                    $"tiling {baseScale} the floor's own footprint produced — otherwise the circuit-inlay " +
                    "mask stretches as one copy across the whole floor instead of repeating per supertile");
            }
            finally
            {
                Object.DestroyImmediate(root3);
            }
        }

        private static Renderer FindNamed(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.GetComponent<Renderer>();
            return null;
        }
    }
}
