using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Dev;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-790. MV-777's <see cref="FrameContrastGate"/> made p95-p5 luma range its primary signal —
    /// exactly the metric a two-extreme neon-on-near-black frame maximises (measured 127 on a real
    /// World 2 capture against the approved design's 50), while a single quantised colour covered 51%
    /// of the play area and nothing checked for that at all. This pins the fix: distinct value tiers and
    /// colour dominance are now the primary signals; range only guards a genuinely flat frame (the
    /// MinRange floor drops 90 -> 28); the median-band ceiling rises to the value the approved design
    /// actually reaches (0.60 -> 0.70).
    ///
    /// One consolidated test (testing policy MV-465 Rule 1), all assertions on the gate's own RESOLVED
    /// <see cref="FrameContrastGate.Result"/> (Rule 2, Tier 2): a two-colour frame that already fails on
    /// tier count must still fail once colour dominance is added (AC1); a synthetic frame with five
    /// well-separated tiers and a range of exactly 60 flips from FAIL (old 90 floor) to PASS (new 28
    /// floor) with no colour above 30% (AC2); a synthetic frame whose single dominant colour occupies
    /// exactly 51% of the frame — high range, four real tiers, comfortably under even the old
    /// median-band ceiling — flips from PASS (the exact defect this ticket fixes: nothing checked colour
    /// dominance) to FAIL (AC3); the checked-in MV-755 flat-frame fixture still fails (AC4); and the live
    /// fog RenderSettings the capture preset relies on actually resolves a positive density once
    /// <see cref="BackyardLighting"/> has applied World 2's look — confirming the preset already renders
    /// with the live fog stack, so Change 2 needed no preset edit (AC5).
    /// </summary>
    public sealed class MV790GateMetricTests
    {
        [Test]
        public void GateMetric_FlipsOnRangeFloorAndColourDominance_KeepsFailingTheRealDefect_AndFogIsLive()
        {
            // --- AC1: a two-colour frame (60% black, 40% neon green) must still fail — it already
            // fails on tier count alone (only 2 bands), and now also fails on colour dominance. High
            // range must not save it. ---
            var blackGreen = BuildBandedTexture(4, 2000, new (float share, Color32 color)[]
            {
                (0.60f, new Color32(0, 0, 0, 255)),
                (0.40f, new Color32(57, 255, 20, 255)),
            });
            try
            {
                FrameContrastGate.Result r1 = FrameContrastGate.Check(blackGreen, marginFrac: 0f, step: 1);
                Assert.IsFalse(r1.Pass, $"a 60/40 black/neon-green frame must fail. Got: {r1}");
            }
            finally { Object.DestroyImmediate(blackGreen); }

            // --- AC2: five well-separated tiers, no colour above 30%, range of exactly 60 — must PASS
            // now that range is a 28 floor, not a 90 gate (it fails on the old floor alone). ---
            var fiveTiers = BuildBandedTexture(4, 2000, new (float share, Color32 color)[]
            {
                (0.045f, Gray(8)),
                (0.20f,  Gray(28)),
                (0.30f,  Gray(48)),
                (0.30f,  Gray(68)),
                (0.155f, Gray(88)),
            });
            try
            {
                FrameContrastGate.Result r2 = FrameContrastGate.Check(fiveTiers, marginFrac: 0f, step: 1);
                Assert.That(r2.DistinctTiers, Is.EqualTo(5), $"expected five distinct tiers. Got: {r2}");
                Assert.That(r2.RangeP95P5, Is.EqualTo(60f).Within(0.5f), $"expected a range of 60. Got: {r2}");
                Assert.IsTrue(r2.Pass,
                    $"five well-separated tiers with no colour above 30% must pass under the new 28 floor. Got: {r2}");
            }
            finally { Object.DestroyImmediate(fiveTiers); }

            // --- AC3: the actual defect this ticket fixes — a single quantised colour occupying 51% of
            // the frame, spread across four real tiers with a wide range, must now FAIL. This is the
            // exact "neon-on-near-black" case MV-777's gate rewarded. ---
            var dominantColor = BuildBandedTexture(4, 2000, new (float share, Color32 color)[]
            {
                (0.51f, Gray(10)),
                (0.16f, Gray(90)),
                (0.16f, Gray(150)),
                (0.17f, Gray(230)),
            });
            try
            {
                FrameContrastGate.Result r3 = FrameContrastGate.Check(dominantColor, marginFrac: 0f, step: 1);
                Assert.That(r3.DistinctTiers, Is.GreaterThanOrEqualTo(4), $"expected at least four tiers. Got: {r3}");
                Assert.IsFalse(r3.Pass,
                    $"a single colour occupying 51% of the frame must fail regardless of range or tier count. Got: {r3}");
            }
            finally { Object.DestroyImmediate(dominantColor); }

            // --- AC4: the checker must still fail MV-755's own checked-in flat-frame capture (same
            // 40%-width skybox-wedge crop MV777FrameContrastTests already applies to this preset). ---
            string fixturePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press", "MV-755-stormdrain-kit.png"));
            Assert.IsTrue(File.Exists(fixturePath), $"fixture missing: {fixturePath}");

            var loadedTex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            Texture2D playAreaTex = null;
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(loadedTex, File.ReadAllBytes(fixturePath)),
                    "couldn't decode the checked-in MV-755 capture");

                int cropX0 = Mathf.RoundToInt(loadedTex.width * 0.40f);
                int cropWidth = loadedTex.width - cropX0;
                playAreaTex = new Texture2D(cropWidth, loadedTex.height, TextureFormat.RGB24, false);
                playAreaTex.SetPixels(loadedTex.GetPixels(cropX0, 0, cropWidth, loadedTex.height));
                playAreaTex.Apply();

                FrameContrastGate.Result r4 = FrameContrastGate.Check(playAreaTex);
                Assert.IsFalse(r4.Pass, $"MV-755's own flat frame must still fail under the new thresholds. Got: {r4}");
            }
            finally
            {
                Object.DestroyImmediate(loadedTex);
                if (playAreaTex != null) Object.DestroyImmediate(playAreaTex);
            }

            // --- AC5 / Change 2: the capture preset that produces play frames must render with the live
            // fog stack — confirm BackyardLighting's own Apply() resolves a positive fog density (the
            // exact call the preset's normal World 2 boot path drives via its AfterSceneLoad
            // self-install; CaptureDirector renders Camera.main, and BackyardLighting keeps retrying
            // onto Camera.main until it exists — see BackyardLighting.Update). ---
            var fogGo = new GameObject("mv790-fog-check");
            try
            {
                fogGo.AddComponent<BackyardLighting>().Apply(BackyardLook.Stormdrain);
                Assert.That(RenderSettings.fogDensity, Is.GreaterThan(0f),
                    "the live fog stack must resolve a positive density at capture time, or play frames are measured without the fog the player actually sees");
            }
            finally
            {
                Object.DestroyImmediate(fogGo);
                RenderSettings.fog = false;
            }
        }

        private static Color32 Gray(byte v) => new Color32(v, v, v, 255);

        /// <summary>Builds a texture whose rows are assigned to <paramref name="bands"/> in order, each
        /// band getting a share of the total row count proportional to <c>share</c> — the same
        /// contiguous-band idiom <see cref="MV777FrameContrastTests"/>'s BuildFourTierTexture uses, just
        /// generalised to arbitrary shares and colours (not just four equal grey bands).</summary>
        private static Texture2D BuildBandedTexture(int width, int height, (float share, Color32 color)[] bands)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            var px = new Color32[width * height];
            int row = 0;
            foreach (var band in bands)
            {
                int rowCount = Mathf.RoundToInt(height * band.share);
                for (int i = 0; i < rowCount && row < height; i++, row++)
                    for (int x = 0; x < width; x++)
                        px[row * width + x] = band.color;
            }
            Color32 last = bands[bands.Length - 1].color;
            while (row < height)
            {
                for (int x = 0; x < width; x++) px[row * width + x] = last;
                row++;
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
