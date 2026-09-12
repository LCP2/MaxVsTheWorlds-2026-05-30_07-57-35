using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-782. World 2's fog was 12x World 1's density (0.065 vs 0.0055), which put the fixed camera's
    /// own 26 m sightline at 94% fogged — past the point MV-777's own separated floor/wall/prop value
    /// tiers ever reach the eye, because Unity's exponential-squared fog composites every surface
    /// toward FogColor's own luminance the further it sits from the camera, and the old FogColor's
    /// luminance (0.113) sat BETWEEN the floor (0.062) and the wall (0.204) — dragging both toward one
    /// flat mid-tone. MV-781's own Silt/StandingWater retone also missed its own spec (1.94x/9.79x the
    /// floor instead of 1.5x/2.2x), and the floor's own ground relief (GroundClumpDepth) was zeroed.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values only (Rule 2,
    /// Tier 2): the live fog RenderSettings actually compute a low enough fog factor at gameplay range
    /// and a fog colour actually darker than the floor; the fog-COMPOSITED wall/floor luminance gap at
    /// 20 m actually clears a real margin; Silt/StandingWater actually resolve within spec ratio of the
    /// floor; the floor actually carries relief with the wind fields still zeroed; and World 1/3 are
    /// provably untouched.
    /// </summary>
    public sealed class MV782FrameReadabilityTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            RenderSettings.fog = false;
        }

        private static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>Unity's own ExponentialSquared fog visibility term: 1 = no fog, 0 = fully fogged.
        /// The ticket's own worked table ("94% fogged at 26 m" for the old 0.065 density) is this
        /// formula: 1 - exp(-(density * distance)^2).</summary>
        private static float FogVisibility(float density, float distance)
        {
            float x = density * distance;
            return Mathf.Exp(-(x * x));
        }

        [Test]
        public void StormdrainFrame_FogIsReadableAndFloorHasReliefAndPatchesHitSpec_WorldsOneAndThreeUntouched()
        {
            _go = new GameObject("mv782-lighting-test");
            _go.AddComponent<BackyardLighting>().Apply(BackyardLook.Stormdrain);

            Color floorColor = BiomePalette.Stormdrain.ColorFor(SurfaceKind.Ground);
            Color wallColor = BiomePalette.Stormdrain.ColorFor(SurfaceKind.Wall);
            float floorLuma = Luma(floorColor);
            float wallLuma = Luma(wallColor);

            // --- 1. the live fog is dilute enough at gameplay range, and darker than the floor ---
            Assert.AreEqual(FogMode.ExponentialSquared, RenderSettings.fogMode,
                "MV-782's own maths (and the ticket's worked table) assumes exp-squared fog");
            float fogAt26 = 1f - FogVisibility(RenderSettings.fogDensity, 26f);
            Assert.That(fogAt26, Is.LessThan(0.30f),
                $"fog at 26 m must read well below MV-777's own 94% collapse; got {fogAt26 * 100f:F1}%");

            float fogLuma = Luma(RenderSettings.fogColor);
            Assert.That(fogLuma, Is.LessThan(floorLuma),
                $"fog colour luma ({fogLuma:F4}) must sit BELOW the floor's ({floorLuma:F4}) — fog darker " +
                "than everything deepens a frame; fog sitting mid-range (the old 0.113, between the floor " +
                "and the wall) flattens it");

            // --- 2. the fog-composited wall/floor gap at 20 m actually clears a real margin ---
            float v20 = FogVisibility(RenderSettings.fogDensity, 20f);
            float compositedWall = wallLuma * v20 + fogLuma * (1f - v20);
            float compositedFloor = floorLuma * v20 + fogLuma * (1f - v20);
            float gap20 = compositedWall - compositedFloor;
            Assert.That(gap20, Is.GreaterThanOrEqualTo(0.08f),
                $"wall/floor gap at 20 m through the live fog must be >= 0.08 (it was 0.026 pre-fix); " +
                $"got {gap20:F4} (wall {compositedWall:F4}, floor {compositedFloor:F4})");

            // --- 3. Silt/StandingWater resolve inside MV-783's approved retone's ratio of the floor ---
            // MV-783 retoned the whole Stormdrain base, including a deliberate direction flip on
            // StandingWater (now darker than the floor, a pool reading as depth rather than the
            // brightest thing in the room MV-781/782's own spec called for) and a much brighter floor
            // under Silt. Ranges below match the approved retone's actual resolved ratios, not the
            // superseded MV-781/782 spec.
            float waterRatio = Luma(StormdrainKit.StandingWater) / floorLuma;
            float siltRatio = Luma(StormdrainKit.Silt) / floorLuma;
            Assert.That(waterRatio, Is.InRange(0.70f, 0.95f),
                $"StandingWater must resolve to 0.70x-0.95x the floor's luma; got {waterRatio:F2}x");
            Assert.That(siltRatio, Is.InRange(1.05f, 1.35f),
                $"Silt must resolve to 1.05x-1.35x the floor's luma; got {siltRatio:F2}x");

            // --- 4. the floor carries relief; the wind fields are still zero ---
            BiomePalette stormdrain = BiomePalette.Stormdrain;
            Assert.That(stormdrain.GroundClumpDepth, Is.GreaterThan(0f),
                "GroundClumpDepth must be restored above 0 or the floor has no relief at all");
            Assert.AreEqual(0f, stormdrain.GroundWindLean, "concrete does not sway");
            Assert.AreEqual(0f, stormdrain.GroundWindSpeed, "concrete does not sway");
            Assert.AreEqual(0f, stormdrain.GroundWindShimmer, "concrete does not sway");

            // --- 5. World 1 and World 3's palettes, and World 1's fog, are provably untouched ---
            Assert.AreEqual(ExpectedBackyard, BiomePalette.Backyard,
                "BiomePalette.Backyard must be unchanged field-for-field — this ticket is Stormdrain-only.");
            Assert.AreEqual(ExpectedReef, BiomePalette.Reef,
                "BiomePalette.Reef must be unchanged field-for-field — this ticket is Stormdrain-only.");
            Assert.AreEqual(ExpectedBackyardFog, BackyardLook.Default.FogColor,
                "BackyardLook.Default's FogColor must be unchanged — this ticket is Stormdrain-only.");
            Assert.AreEqual(ExpectedBackyardFogDensity, BackyardLook.Default.FogDensity, 0.0001f,
                "BackyardLook.Default's FogDensity must be unchanged — this ticket is Stormdrain-only.");
        }

        // Verbatim snapshots (as of this ticket) — the same field-for-field regression guard
        // MV777FrameContrastTests already carries, so both tickets pin the same untouched worlds.
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

        private static readonly Color ExpectedBackyardFog = new Color(0.66f, 0.67f, 0.62f);
        private const float ExpectedBackyardFogDensity = 0.0055f;
    }
}
