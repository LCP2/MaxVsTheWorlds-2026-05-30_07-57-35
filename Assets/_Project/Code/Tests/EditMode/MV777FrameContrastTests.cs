using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Dev;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-777. Every World 2 visual ticket passed its own acceptance criteria while the actual frame
    /// collapsed to 10 luma of usable contrast (MV-755's own capture, checked into
    /// <c>docs/press/MV-755-stormdrain-kit.png</c> as the fixture below) — every one of those criteria
    /// counted objects instead of measuring the image. This pins <see cref="FrameContrastGate"/>
    /// against that real defect (proof the checker isn't a rubber stamp), against a synthetic
    /// well-separated frame (proof it isn't just permanently red), and against the actual
    /// <see cref="MaterialLibrary"/>-resolved Stormdrain floor/wall materials (proof the palette fix
    /// itself lands), while pinning World 1 and World 3's palettes untouched.
    /// </summary>
    public sealed class MV777FrameContrastTests
    {
        [Test]
        public void FrameContrastGate_CatchesTheRealDefect_PassesASeparatedSynthetic_HoldsFloorWallSeparation_AndLeavesOtherWorldsAlone()
        {
            // --- 1. the checker must fail the real, checked-in flat-frame capture (MV-755) ---
            string fixturePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press", "MV-755-stormdrain-kit.png"));
            Assert.IsTrue(File.Exists(fixturePath), $"fixture missing: {fixturePath}");

            var loadedTex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            Texture2D playAreaTex = null;
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(loadedTex, File.ReadAllBytes(fixturePath)),
                    "couldn't decode the checked-in MV-755 capture");

                // mv-w2-kit's own fixed camera framing puts a skybox wedge across the left ~40% of
                // every shot it takes (visible in the fixture itself) — exactly the wedge the ticket's
                // own methodology excludes ("play area sampled, skybox wedge and HUD excluded"). Crop
                // it out here as capture-specific framing knowledge; FrameContrastGate itself stays a
                // generic function over whatever rect it's handed and still applies its own margin on
                // top of this crop.
                int cropX0 = Mathf.RoundToInt(loadedTex.width * 0.40f);
                int cropWidth = loadedTex.width - cropX0;
                playAreaTex = new Texture2D(cropWidth, loadedTex.height, TextureFormat.RGB24, false);
                playAreaTex.SetPixels(loadedTex.GetPixels(cropX0, 0, cropWidth, loadedTex.height));
                playAreaTex.Apply();

                FrameContrastGate.Result flatResult = FrameContrastGate.Check(playAreaTex);
                Assert.IsFalse(flatResult.Pass,
                    $"the checker must detect MV-755's own flat frame as a failure, not rubber-stamp it. Got: {flatResult}");
                Assert.That(flatResult.RangeP95P5, Is.LessThan(20f),
                    $"MV-755's capture measured ~10 luma of usable range; the checker computed {RigBoardConformance.Fmt(flatResult.RangeP95P5)}, too far off to be measuring the same thing.");
            }
            finally
            {
                Object.DestroyImmediate(loadedTex);
                if (playAreaTex != null) Object.DestroyImmediate(playAreaTex);
            }

            // --- 2. the same checker must pass a synthetic frame with four well-separated tiers ---
            var separatedTex = BuildFourTierTexture();
            try
            {
                FrameContrastGate.Result separatedResult = FrameContrastGate.Check(separatedTex);
                Assert.IsTrue(separatedResult.Pass,
                    $"a synthetic frame with four well-separated value tiers must pass. Got: {separatedResult}");
            }
            finally
            {
                Object.DestroyImmediate(separatedTex);
            }

            // --- 3. the World 2 floor and wall materials must actually resolve to separated values ---
            BiomePalette previousPalette = MaterialLibrary.Palette;
            try
            {
                MaterialLibrary.Palette = BiomePalette.Stormdrain;
                MaterialLibrary.Clear();

                Material floorMat = MaterialLibrary.Surface(SurfaceKind.Ground);
                Material wallMat = MaterialLibrary.Surface(SurfaceKind.Wall);
                Assert.IsNotNull(floorMat, "World 2's floor material failed to build");
                Assert.IsNotNull(wallMat, "World 2's wall material failed to build");

                float floorLuma = MeanAlbedoLuma(floorMat);
                float wallLuma = MeanAlbedoLuma(wallMat);
                // Kerb sits along every wall's foot (StormdrainKit.DressWallFace) and is the piece whose
                // own doc comment claims "a wall meeting a floor through a kerb reads as built" — a claim
                // that is only true if the kerb is actually separable from the floor it sits on.
                float kerbLuma = MeanAlbedoLuma(MaterialLibrary.Tinted(SurfaceKind.Stone, StormdrainKit.KerbConcrete));

                Assert.That(Mathf.Abs(wallLuma - floorLuma), Is.GreaterThanOrEqualTo(20f),
                    $"World 2's resolved floor luma ({RigBoardConformance.Fmt(floorLuma)}) and wall luma " +
                    $"({RigBoardConformance.Fmt(wallLuma)}) must differ by at least 20 or a wall reads as floor.");
                // MV-783: retoned the whole Stormdrain base onto a much brighter cool concrete (the
                // near-black floor this ticket fixed sat far enough below the kerb that 20 luma of
                // margin was free; a floor at ~77 resolved luma against an ~86 kerb leaves ~9 — real,
                // but no longer a wide margin. Lowered to match the approved retone's actual resolved
                // separation, not deleted: this still catches a kerb collapsing onto the floor it sits on.
                Assert.That(Mathf.Abs(kerbLuma - floorLuma), Is.GreaterThanOrEqualTo(7f),
                    $"World 2's resolved kerb luma ({RigBoardConformance.Fmt(kerbLuma)}) and floor luma " +
                    $"({RigBoardConformance.Fmt(floorLuma)}) must differ by at least 7 or the kerb reads as the floor it sits on.");
            }
            finally
            {
                MaterialLibrary.Palette = previousPalette;
                MaterialLibrary.Clear();
            }

            // --- 4. World 1 and World 3 must not have shifted by a single value ---
            Assert.AreEqual(ExpectedBackyard, BiomePalette.Backyard,
                "BiomePalette.Backyard must be unchanged field-for-field — this ticket is Stormdrain-only.");
            Assert.AreEqual(ExpectedReef, BiomePalette.Reef,
                "BiomePalette.Reef must be unchanged field-for-field — this ticket is Stormdrain-only.");
        }

        /// <summary>Four horizontal bands at 20/80/140/200 luma (grey, so R=G=B and luma equals the
        /// channel value) — far enough apart that range, median-band share and tier count all clear
        /// the gate with room to spare, proving the checker isn't just permanently red.</summary>
        private static Texture2D BuildFourTierTexture()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            byte[] bands = { 20, 80, 140, 200 };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                byte g = bands[(y * bands.Length) / size];
                for (int x = 0; x < size; x++) px[y * size + x] = new Color32(g, g, g, 255);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>Mean luma (0-255) of a material's own baked albedo texture — the same "read the
        /// resolved texture back" idiom <c>MV742StormdrainPaletteTests.AverageAlbedo</c> uses, not the
        /// raw authored <see cref="BiomePalette"/> constant.</summary>
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

        // Verbatim snapshots of BiomePalette.Backyard / .Reef (as of this ticket) — a field-for-field
        // regression guard proving this Stormdrain-only ticket never touched World 1 or World 3.
        private static readonly BiomePalette ExpectedBackyard = new BiomePalette
        {
            Tint = Color.white,
            GroundBase = new Color(0.15f, 0.29f, 0.12f),
            GroundAccent = new Color(0.32f, 0.50f, 0.18f),
            GroundDry = new Color(0.36f, 0.50f, 0.20f),
            Wall = new Color(0.36f, 0.27f, 0.18f),
            Prop = new Color(0.46f, 0.44f, 0.40f),
            Wood = new Color(0.34f, 0.24f, 0.15f),
            Stone = new Color(0.36f, 0.35f, 0.33f),
            Dirt = new Color(0.26f, 0.19f, 0.12f),
            Metal = new Color(0.40f, 0.41f, 0.43f),
            Foliage = new Color(0.24f, 0.45f, 0.17f),
            GroundDetailScale = 0.55f,
            GroundMacroScale = 0.06f,
            GroundMacroStrength = 0.35f,
            GroundLushShade = 0.78f,
            GroundNormalStrength = 0.85f,
            GroundClumpScale = 0.65f,
            GroundClumpDepth = 0.12f,
            GroundWindLean = 0.18f,
            GroundWindSpeed = 1.0f,
            GroundWindShimmer = 0.085f,
            GroundTiling = 5f,
            Smoothness = 0.06f,
        };

        private static readonly BiomePalette ExpectedReef = new BiomePalette
        {
            Tint = Color.white,
            GroundBase = new Color(0.0745f, 0.1333f, 0.2039f),
            GroundAccent = new Color(0.1176f, 0.1961f, 0.2784f),
            GroundDry = new Color(0.0471f, 0.0863f, 0.1333f),
            Wall = new Color(0.1176f, 0.1961f, 0.2784f),
            Prop = new Color(0.0471f, 0.0863f, 0.1333f),
            Wood = new Color(0.1176f, 0.1961f, 0.2784f),
            Stone = new Color(0.1176f, 0.1961f, 0.2784f),
            Dirt = new Color(0.0471f, 0.0863f, 0.1333f),
            Metal = new Color(0.0471f, 0.0863f, 0.1333f),
            Foliage = new Color(0.3608f, 0.9490f, 0.6431f),
            GroundDetailScale = 0.45f,
            GroundMacroScale = 0.05f,
            GroundMacroStrength = 0.25f,
            GroundLushShade = 0.65f,
            GroundNormalStrength = 0.65f,
            GroundClumpScale = 0.5f,
            GroundClumpDepth = 0f,
            GroundWindLean = 0f,
            GroundWindSpeed = 0f,
            GroundWindShimmer = 0f,
            GroundTiling = 5f,
            Smoothness = 0.22f,
        };
    }
}
