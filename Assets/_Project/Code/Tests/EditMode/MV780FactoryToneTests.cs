using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Dev;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-780 — <see cref="FactoryBodies"/>' <c>EnsureMaterials()</c> hard-codes three colours
    /// outside <see cref="BiomePalette"/>, so MV-777's World 2 value-tier separation never reached
    /// them. The replicator hull (<c>Factory_Dark</c>) resolved to ~15 luma — darker than the
    /// 18-26 luma floor tier MV-777 established, so the most important object in World 2 had no
    /// silhouette against its own floor. This pins the hull's resolved material at least 40 luma
    /// apart from the resolved World 2 floor material, and pins <c>Factory_Hazard</c>/
    /// <c>Factory_Rust</c> unchanged. Tier 2 (resolved values): both sides are read off materials
    /// actually built by <see cref="FactoryBodies"/> and <see cref="MaterialLibrary"/>, never an
    /// authored constant asserted back at itself.
    /// </summary>
    public sealed class MV780FactoryToneTests
    {
        // Same "distinctive far-off origin" idiom other EditMode tests in this suite use.
        private static readonly Vector3 RigOrigin = new Vector3(71553f, 0f, -30442f);

        private GameObject _boxGo;

        [TearDown]
        public void TearDown()
        {
            if (_boxGo != null) Object.DestroyImmediate(_boxGo);
        }

        [Test]
        public void ReplicatorHull_ResolvesAboveFloorTier_AndHazardRustUnchanged()
        {
            _boxGo = new GameObject("ReplicatorBox");
            _boxGo.transform.position = RigOrigin;
            _boxGo.transform.localScale = new Vector3(2f, 2f, 1.5f);
            Transform bodyRoot = ParentScale.MakeMetreSpace(new GameObject("Body").transform, _boxGo.transform);

            FactoryBodies.BuildReplicator(bodyRoot, _boxGo.transform.lossyScale);

            Transform hull = bodyRoot.Find("Hull");
            Assert.IsNotNull(hull, "BuildReplicator must generate a Hull part under the metre-space body root");
            Material hullMat = hull.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.IsNotNull(hullMat, "the Hull part must carry a resolved material");

            float hullLuma = ResolvedLuma(hullMat);

            BiomePalette previousPalette = MaterialLibrary.Palette;
            float floorLuma;
            try
            {
                MaterialLibrary.Palette = BiomePalette.Stormdrain;
                MaterialLibrary.Clear();
                Material floorMat = MaterialLibrary.Surface(SurfaceKind.Ground);
                Assert.IsNotNull(floorMat, "World 2's floor material failed to build");
                floorLuma = MeanAlbedoLuma(floorMat);
            }
            finally
            {
                MaterialLibrary.Palette = previousPalette;
                MaterialLibrary.Clear();
            }

            Assert.That(Mathf.Abs(hullLuma - floorLuma), Is.GreaterThanOrEqualTo(40f),
                $"the replicator hull's resolved luma ({RigBoardConformance.Fmt(hullLuma)}) and World 2's " +
                $"resolved floor luma ({RigBoardConformance.Fmt(floorLuma)}) must differ by at least 40 or " +
                "the hull has no silhouette against its own floor.");

            // Factory_Rust / Factory_Hazard are MV-780's "do not touch" list — field-for-field.
            Transform hazardBand = bodyRoot.Find("HazardBand");
            Transform hatch = bodyRoot.Find("Hatch");
            Assert.IsNotNull(hazardBand);
            Assert.IsNotNull(hatch);
            Material hazardMat = hazardBand.GetComponent<MeshRenderer>().sharedMaterial;
            Material rustMat = hatch.GetComponent<MeshRenderer>().sharedMaterial;
            AssertColorEqual(new Color(0.95f, 0.78f, 0.08f), ResolvedColor(hazardMat), "Factory_Hazard");
            AssertColorEqual(new Color(0.55f, 0.27f, 0.11f), ResolvedColor(rustMat), "Factory_Rust");
        }

        private static void AssertColorEqual(Color expected, Color actual, string label)
        {
            Assert.AreEqual(expected.r, actual.r, 0.001f, $"{label}.r must be unchanged");
            Assert.AreEqual(expected.g, actual.g, 0.001f, $"{label}.g must be unchanged");
            Assert.AreEqual(expected.b, actual.b, 0.001f, $"{label}.b must be unchanged");
        }

        private static Color ResolvedColor(Material m) =>
            m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.GetColor("_Color");

        /// <summary>Resolved luma (0-255) of a solid, texture-less material's own colour — the same
        /// direct-from-Color idiom <c>StormdrainKit.Rust</c>'s own doc comment ("~95-115 resolved
        /// luma") uses, for materials that carry no baked albedo texture to average.</summary>
        private static float ResolvedLuma(Material m) => ElementPalette.Luminance(ResolvedColor(m)) * 255f;

        /// <summary>Mean luma (0-255) of a material's own baked albedo texture — the same idiom
        /// <c>MV777FrameContrastTests.MeanAlbedoLuma</c> uses for the ground/wall shaders, which DO
        /// bake a texture.</summary>
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
