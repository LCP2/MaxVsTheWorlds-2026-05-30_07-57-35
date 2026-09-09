using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-742: on the live build (d63d576-0908-2256), World 2's ground read as a near-black void with
    /// no colour separation from the walls or cover — Stormdrain's wet-concrete palette (MV-690) never
    /// reached the running arena.
    ///
    /// Reproduces exactly what <c>BackyardPath.Awake</c> does, in the order it does it, against World
    /// 2's real shipped config: build the map's geometry via <see cref="MapRuntime.Build"/>, then assert
    /// the world's real palette via <see cref="WorldMaterials.Apply"/>. Asserts RESOLVED state only —
    /// the palette <see cref="MaterialLibrary"/> actually ends up holding, and the baked albedo colour
    /// read back off the floor and wall renderers' own materials, never an authored constant.
    /// </summary>
    public sealed class MV742StormdrainPaletteTests
    {
        [Test]
        public void World2Build_ResolvesStormdrainPalette_WithFloorWallSeparation()
        {
            MaterialLibrary.Palette = BiomePalette.Backyard;
            MaterialLibrary.Clear();

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject("MV742 Stormdrain Palette Probe Root");
            try
            {
                // Exactly BackyardPath.Awake's own order: geometry first, then the world's real palette.
                MapRuntime.Build(map, root.transform);

                var wm = new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
                wm.Apply(BiomePalette.ForWorld(1));   // World 2 — a real palette change away from Backyard

                Assert.AreEqual(BiomePalette.Stormdrain, MaterialLibrary.Palette,
                    "World 2 must resolve the Stormdrain biome palette, not whatever was active before it.");
                Assert.AreNotEqual(BiomePalette.Backyard, MaterialLibrary.Palette,
                    "World 2 must not still be wearing World 1's Backyard palette.");

                Renderer floor = FindNamed(root.transform, "Map Floor");
                Assert.IsNotNull(floor, "expected World 2's built map to include its floor slab");
                Material floorMat = floor.sharedMaterial;
                Assert.IsNotNull(floorMat, "the floor's material was lost by the palette switch");

                Renderer wall = FindFirstWall(root.transform);
                Assert.IsNotNull(wall, "expected at least one built wall in World 2");
                Material wallMat = wall.sharedMaterial;
                Assert.IsNotNull(wallMat, "the wall's material was lost by the palette switch");

                Color floorColor = AverageAlbedo(floorMat);
                Color wallColor = AverageAlbedo(wallMat);

                float separation = ColorDistance(floorColor, wallColor);
                Assert.Greater(separation, 0.08f,
                    $"floor ({floorColor}) and wall ({wallColor}) must be separable by eye on a 6-inch " +
                    $"screen; resolved distance was only {separation:F3}.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Renderer FindNamed(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.GetComponent<Renderer>();
            return null;
        }

        private static Renderer FindFirstWall(Transform root)
        {
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
                if (WorldMaterials.IsWorldSurface(r) && WorldMaterials.KindOf(r) == SurfaceKind.Wall)
                    return r;
            return null;
        }

        private static Color AverageAlbedo(Material m)
        {
            Texture2D tex = (m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null) as Texture2D
                          ?? m.mainTexture as Texture2D;
            Assert.IsNotNull(tex, $"material '{m.name}' has no readable albedo texture to sample");

            Color32[] px = tex.GetPixels32();
            long r = 0, g = 0, b = 0;
            foreach (Color32 c in px) { r += c.r; g += c.g; b += c.b; }
            int n = px.Length;
            return new Color(r / (255f * n), g / (255f * n), b / (255f * n));
        }

        private static float ColorDistance(Color a, Color b)
        {
            float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }
    }
}
