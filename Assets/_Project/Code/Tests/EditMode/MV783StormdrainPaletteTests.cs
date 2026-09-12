using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Dev;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-783. On the named base commit (927ea76), World 2's <see cref="BiomePalette.Stormdrain"/> has
    /// chroma in exactly two places — <c>Metal</c> (rust, saturation 0.75) and <c>StormdrainKit.Sludge</c>
    /// — while every neutral surface sits so dark in linear terms it contributes no colour at all, so the
    /// whole frame reads as two colours and black. This pins the approved fix (Lee, 2026-09-12, "Stormdrain
    /// Surface Kit" review): a cool wet-concrete base with real value separation between floor/wall/cover,
    /// rust desaturated to an accent, and the floor's relief left in place.
    ///
    /// Luminance is RESOLVED via Unity's own <see cref="Color.linear"/> — the actual sRGB-to-linear
    /// decode the GPU performs on every material Color property in this project's linear colour space
    /// (see the ticket's own Observation section) — not the raw authored sRGB triple, and not the
    /// procedurally noise-blended albedo texture <see cref="MV742StormdrainPaletteTests"/> and
    /// <see cref="MV777FrameContrastTests"/> read back (that blend mixes in <c>GroundAccent</c>, which
    /// the ticket's own numeric thresholds were never computed against). Plus one computed derived
    /// property (Metal's saturation) and one field-for-field regression guard on the untouched worlds —
    /// never a bare authored-constant equality.
    /// </summary>
    public sealed class MV783StormdrainPaletteTests
    {
        [Test]
        public void StormdrainPalette_ResolvesCoolSeparatedValueTiers_DesaturatesRust_KeepsFloorRelief_AndLeavesOtherWorldsAlone()
        {
            // --- 1. the resolved floor luminance must be at least 4x the base commit's ---
            float floorLumaOld = ResolvedLuminance(BaseCommitStormdrain.GroundBase);
            float floorLumaNew = ResolvedLuminance(BiomePalette.Stormdrain.GroundBase);

            Assert.That(floorLumaNew, Is.GreaterThanOrEqualTo(floorLumaOld * 4f),
                $"resolved floor luminance must be at least 4x the base commit's (old {RigBoardConformance.Fmt(floorLumaOld)}, " +
                $"new {RigBoardConformance.Fmt(floorLumaNew)}) — the near-black void is the defect this ticket fixes.");

            // --- 2. Metal's authored saturation must drop below the approved ceiling ---
            float metalSat = Saturation(BiomePalette.Stormdrain.Metal);
            Assert.That(metalSat, Is.LessThan(0.62f),
                $"Metal's saturation ({metalSat:F3}) must drop below 0.62 — rust is an accent now, not one of the world's only two colours.");

            // --- 3. floor < wall < cover must form a strictly increasing, well-separated sequence ---
            float wallLumaNew = ResolvedLuminance(BiomePalette.Stormdrain.Wall);
            float coverLumaNew = ResolvedLuminance(BiomePalette.Stormdrain.Prop);
            const float minStep = 0.04f;

            Assert.That(wallLumaNew - floorLumaNew, Is.GreaterThanOrEqualTo(minStep),
                $"wall luminance ({RigBoardConformance.Fmt(wallLumaNew)}) must sit at least 0.04 above floor luminance ({RigBoardConformance.Fmt(floorLumaNew)}).");
            Assert.That(coverLumaNew - wallLumaNew, Is.GreaterThanOrEqualTo(minStep),
                $"cover luminance ({RigBoardConformance.Fmt(coverLumaNew)}) must sit at least 0.04 above wall luminance ({RigBoardConformance.Fmt(wallLumaNew)}).");

            // --- 4. the floor's relief must be restored, with every wind field still exactly zero ---
            Assert.That(BiomePalette.Stormdrain.GroundClumpDepth, Is.GreaterThan(0f),
                "GroundClumpDepth must be restored above 0 — concrete relief, not a flat fill.");
            Assert.That(BiomePalette.Stormdrain.GroundWindLean, Is.EqualTo(0f),
                "GroundWindLean must stay exactly 0 — concrete does not sway.");
            Assert.That(BiomePalette.Stormdrain.GroundWindSpeed, Is.EqualTo(0f),
                "GroundWindSpeed must stay exactly 0 — concrete does not sway.");
            Assert.That(BiomePalette.Stormdrain.GroundWindShimmer, Is.EqualTo(0f),
                "GroundWindShimmer must stay exactly 0 — concrete does not sway.");

            // --- 5. World 1 and World 3 must not have shifted by a single field ---
            Assert.AreEqual(ExpectedBackyard, BiomePalette.Backyard,
                "BiomePalette.Backyard must be unchanged field-for-field — this ticket is Stormdrain-only.");
            Assert.AreEqual(ExpectedReef, BiomePalette.Reef,
                "BiomePalette.Reef must be unchanged field-for-field — this ticket is Stormdrain-only.");
        }

        /// <summary>Rec709 luma over the engine-resolved linear channels (<see cref="Color.linear"/> —
        /// Unity's own sRGB decode, not a hand-rolled approximation), the same weighting
        /// <c>MV777FrameContrastTests.MeanAlbedoLuma</c> uses on a baked texture. Applied directly to an
        /// authored <see cref="BiomePalette"/> tone rather than a texture readback, per this ticket's
        /// own Observation section.</summary>
        private static float ResolvedLuminance(Color authoredSRgb)
        {
            Color lin = authoredSRgb.linear;
            return 0.2126f * lin.r + 0.7152f * lin.g + 0.0722f * lin.b;
        }

        /// <summary>(max-min)/max on the authored sRGB triple — the ticket's own definition.</summary>
        private static float Saturation(Color c)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            return max <= 0f ? 0f : (max - min) / max;
        }

        // Verbatim snapshot of BiomePalette.Stormdrain as of the named base commit (927ea76) — the
        // "two colours and black" defect this ticket fixes, kept here (not read from the live struct)
        // so the fix can never quietly make this comparison pass by accident.
        private static readonly BiomePalette BaseCommitStormdrain = new BiomePalette
        {
            Tint = Color.white,
            GroundBase = new Color(0.055f, 0.065f, 0.057f),
            GroundAccent = new Color(0.085f, 0.10f, 0.085f),
            GroundDry = new Color(0.11f, 0.115f, 0.10f),
            Wall = new Color(0.19f, 0.21f, 0.185f),
            Prop = new Color(0.29f, 0.32f, 0.29f),
            Wood = new Color(0.28f, 0.22f, 0.16f),
            Stone = new Color(0.19f, 0.21f, 0.185f),
            Dirt = new Color(0.20f, 0.15f, 0.10f),
            Metal = new Color(0.685f, 0.355f, 0.17f),
            Foliage = new Color(0.43f, 0.56f, 0.20f),
            GroundDetailScale = 0.45f,
            GroundMacroScale = 0.05f,
            GroundMacroStrength = 0.30f,
            GroundLushShade = 0.70f,
            GroundNormalStrength = 0.9f,
            GroundClumpScale = 0.5f,
            GroundClumpDepth = 0.07f,
            GroundWindLean = 0f,
            GroundWindSpeed = 0f,
            GroundWindShimmer = 0f,
            GroundTiling = 5f,
            Smoothness = 0.14f,
        };

        // Verbatim snapshots of BiomePalette.Backyard / .Reef (as of this ticket) — same field-for-field
        // regression guard MV777FrameContrastTests already carries for the same reason.
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
