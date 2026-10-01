using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1053: World 3's floor wore a flat tint with no plate detail and no emissive circuitry —
    /// <see cref="WorldMaterials.M_ShipFloor"/> carried only a colour (via <c>ReefMaterial</c>), never
    /// a base map or an emission map, and the triplanar shader behind it has no mesh-UV tiling to
    /// measure a world-space plate size from in the first place (it samples from world position
    /// instead — see StylizedSurface.shader's own header).
    ///
    /// Fails before this fix: <c>floor.sharedMaterial.mainTextureScale</c> is the shader default
    /// (1, 1), so the computed plate period is the whole floor's own world size — tens of metres,
    /// nowhere near 2 m — and <c>GetTexture("_EmissionMap")</c> comes back null (never assigned).
    ///
    /// Asserts RESOLVED state only, through the same real load path MV745World3HullDressingTests and
    /// MV1019ReefFloorContrastTests already prove out (<see cref="MapRuntime.Build"/> -&gt;
    /// <see cref="WorldMaterials.Apply"/> -&gt; <see cref="ReefKit.DressHull"/>): the floor renderer's
    /// own resolved world size divided by its own resolved material tiling, and the material's own
    /// resolved emission map — never an authored constant compared to itself.
    /// </summary>
    public sealed class MV1053ReefDeckFloorTests
    {
        [Test]
        public void World3Floor_TilesTwoMetrePlates_AndCarriesAnEmissiveCircuitMask()
        {
            WorldConfig cfg3 = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg3, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg3, out MapData map3, out string reason3), reason3);

            var root3 = new GameObject("MV1053 World3 Probe Root");
            try
            {
                // Exactly BackyardPath.Awake's own order (mirrors MV745World3HullDressingTests):
                // geometry, the world's palette sweep, then the Reef-only hull pass this ticket
                // changes the floor branch of.
                MapRuntime.Build(map3, root3.transform);
                var wm = new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
                wm.Apply(BiomePalette.ForWorld(2));
                ReefKit.DressHull(root3.transform);

                Renderer floor = FindNamed(root3.transform, "Map Floor");
                Assert.IsNotNull(floor, "expected World 3's built map to include its floor slab");

                Material mat = floor.sharedMaterial;
                // GetTexture("_BaseMap"), not the mainTexture shortcut: that shortcut only follows a
                // shader's [MainTexture]-tagged property (or _MainTex), and URP/Lit's own _BaseMap
                // carries neither, so mainTexture reads back null here regardless of what is set.
                Assert.IsNotNull(mat.GetTexture("_BaseMap"), "the floor must wear a real base map, not the shader's default white");

                // The world-space period of one plate: the floor's own resolved world size divided
                // by the material's own resolved mesh-UV tiling count (ReefKit.DressHull sets this
                // from the floor's real bounds — never a hardcoded guess).
                Vector3 worldSize = floor.bounds.size;
                Vector2 tiling = mat.GetTextureScale("_BaseMap");
                Assert.Greater(tiling.x, 0f, "precondition: the floor's tiling must have been set at all");
                Assert.Greater(tiling.y, 0f, "precondition: the floor's tiling must have been set at all");

                float periodX = worldSize.x / tiling.x;
                float periodZ = worldSize.z / tiling.y;
                Assert.That(periodX, Is.EqualTo(WorldMaterials.ReefDeckPlateSizeMetres).Within(0.05f),
                    $"the deck's X-axis plate period ({periodX:F3} m) must be 2 m +/- 0.05, from the floor's " +
                    $"own world size ({worldSize.x:F2} m) against its own resolved tiling ({tiling.x:F2})");
                Assert.That(periodZ, Is.EqualTo(WorldMaterials.ReefDeckPlateSizeMetres).Within(0.05f),
                    $"the deck's Z-axis plate period ({periodZ:F3} m) must be 2 m +/- 0.05, from the floor's " +
                    $"own world size ({worldSize.z:F2} m) against its own resolved tiling ({tiling.y:F2})");

                Texture emission = mat.GetTexture("_EmissionMap");
                Assert.IsNotNull(emission, "the floor's material must carry a non-null emission map (the circuit mask)");
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
