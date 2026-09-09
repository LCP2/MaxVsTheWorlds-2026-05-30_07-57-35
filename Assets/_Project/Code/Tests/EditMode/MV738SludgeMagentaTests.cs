using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-738: World 2's sludge rendered flat magenta on the live build (d63d576-0908-2256) — Unity's
    /// error-shader colour, meaning a destroyed-but-still-referenced material, not a wrong one.
    ///
    /// Root cause: <c>BackyardPath.Awake</c> calls <c>MapRuntime.Build</c> — which bakes the sludge's
    /// material through <see cref="MaterialLibrary.Tinted"/> and marks its renderer
    /// <see cref="KeepsOwnMaterial"/>, specifically so <see cref="WorldMaterials.Apply"/> never
    /// re-touches it — and only THEN asserts the world's real palette via that same
    /// <c>WorldMaterials.Apply</c> call. World 2's palette differs from whatever was active a moment
    /// earlier, so that second call used to run <c>MaterialLibrary.Clear()</c>, which destroyed every
    /// cached material, including the sludge tile's — even though a <c>Tinted</c> material's colour
    /// never depended on the palette to begin with. Nothing was ever going to hand the orphaned
    /// <c>KeepsOwnMaterial</c> renderer a replacement, so it kept pointing at a dead <c>Material</c>:
    /// Unity's magenta error shader.
    ///
    /// This reproduces the exact two calls <c>BackyardPath.Awake</c> makes, in the exact order it makes
    /// them, against World 2's real shipped config — the config behind the live bug. Asserts the
    /// RESOLVED state of the built renderer, never an authored constant. FAILS on d63d576 with:
    ///   "the sludge renderer's material was destroyed by the palette change that followed
    ///   MapRuntime.Build — every World 2 sludge tile renders Unity's magenta error shader.
    ///   Expected: True
    ///   But was:  False"
    /// </summary>
    public sealed class MV738SludgeMagentaTests
    {
        [Test]
        public void SludgeMaterial_SurvivesTheWorldPaletteChangeThatFollowsMapRuntimeBuild()
        {
            MaterialLibrary.Palette = BiomePalette.Backyard;
            MaterialLibrary.Clear();

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject("MV738 Sludge Probe Root");
            try
            {
                // Exactly BackyardPath.Awake's own order: build the map's geometry — including the
                // KeepsOwnMaterial-tinted sludge — BEFORE the world's real palette is asserted.
                MapRuntime.Build(map, root.transform);

                Renderer sludgeRenderer = FindAnySludgeRenderer(root.transform);
                Assert.IsNotNull(sludgeRenderer, "expected at least one built sludge tile in World 2");

                var wm = new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
                wm.Apply(BiomePalette.ForWorld(1));   // World 2 — a real palette change away from Backyard

                Material mat = sludgeRenderer.sharedMaterial;
                Assert.IsTrue(mat != null,
                    "the sludge renderer's material was destroyed by the palette change that followed " +
                    "MapRuntime.Build — every World 2 sludge tile renders Unity's magenta error shader.");
                Assert.IsNotNull(mat.shader,
                    "the sludge material survived but lost its shader — still the magenta symptom.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Renderer FindAnySludgeRenderer(Transform root)
        {
            foreach (SludgeFlow flow in root.GetComponentsInChildren<SludgeFlow>(true))
            {
                var r = flow.GetComponent<Renderer>();
                if (r != null) return r;
            }
            return null;
        }
    }
}
