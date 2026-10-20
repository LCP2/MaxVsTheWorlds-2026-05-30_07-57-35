using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1265: the Reef deck's circuit-inlay traces baked at 5 texels wide - about 7.5 screen px at
    /// play distance, under DECISIONS.md section 3's ~10 px legibility floor (and mipmapping crushed it
    /// further) - so the inlay never actually read in a built player, on either render tier. Lee's
    /// decision (option b): widen <see cref="StylizedTextures.ReefDeckEmission"/>'s trace stroke to
    /// >=12 texels; this is also the diagnostic - if the widened inlay still doesn't render, the cause
    /// is binding, not size.
    ///
    /// Measures a RESOLVED value: the actual stroke thickness baked into the real texture's pixels,
    /// found by locating a run that spans nearly a whole cell (a straight trace, never a node - every
    /// node's diameter is well under this texture's cell size) and measuring its thickness in the
    /// orthogonal direction. Never a restated copy of the authored fraction constant.
    ///
    /// Fails on the base commit, where the baked trace measured 5 texels wide.
    /// </summary>
    public sealed class MV1265ReefDeckEmissionTraceWidthTests
    {
        [Test]
        public void BakedEmission_TraceStrokeIsAtLeastTwelveTexelsWide()
        {
            Texture2D tex = StylizedTextures.ReefDeckEmission();
            int size = tex.width;
            Color32[] px = tex.GetPixels32();

            int minThickness = int.MaxValue;
            bool foundAny = false;

            // Horizontal traces: a near-full-width run along a row; thickness is the vertical extent.
            for (int y = 0; y < size; y++)
            {
                int runStart = -1;
                for (int x = 0; x <= size; x++)
                {
                    bool nonBlack = x < size && !IsBlack(px[y * size + x]);
                    if (nonBlack)
                    {
                        if (runStart < 0) runStart = x;
                        continue;
                    }
                    if (runStart < 0) continue;

                    int runLen = x - runStart;
                    if (runLen >= size / 8)
                    {
                        int midX = runStart + runLen / 2;
                        minThickness = Mathf.Min(minThickness, VerticalRunThrough(px, size, midX, y));
                        foundAny = true;
                    }
                    runStart = -1;
                }
            }

            // Vertical traces: a near-full-height run along a column; thickness is the horizontal extent.
            for (int x = 0; x < size; x++)
            {
                int runStart = -1;
                for (int y = 0; y <= size; y++)
                {
                    bool nonBlack = y < size && !IsBlack(px[y * size + x]);
                    if (nonBlack)
                    {
                        if (runStart < 0) runStart = y;
                        continue;
                    }
                    if (runStart < 0) continue;

                    int runLen = y - runStart;
                    if (runLen >= size / 8)
                    {
                        int midY = runStart + runLen / 2;
                        minThickness = Mathf.Min(minThickness, HorizontalRunThrough(px, size, x, midY));
                        foundAny = true;
                    }
                    runStart = -1;
                }
            }

            Assert.IsTrue(foundAny,
                "expected at least one near-full-span trace run in the baked ReefDeckEmission texture");
            Assert.GreaterOrEqual(minThickness, 12,
                $"resolved circuit-inlay trace stroke measured {minThickness} texels wide in the baked " +
                "texture, below the ~12 texel floor DECISIONS.md section 3's legibility rule requires");
        }

        private static bool IsBlack(Color32 c) => c.r == 0 && c.g == 0 && c.b == 0;

        private static int VerticalRunThrough(Color32[] px, int size, int x, int y)
        {
            int top = y;
            while (top > 0 && !IsBlack(px[(top - 1) * size + x])) top--;
            int bottom = y;
            while (bottom < size - 1 && !IsBlack(px[(bottom + 1) * size + x])) bottom++;
            return bottom - top + 1;
        }

        private static int HorizontalRunThrough(Color32[] px, int size, int x, int y)
        {
            int left = x;
            while (left > 0 && !IsBlack(px[y * size + (left - 1)])) left--;
            int right = x;
            while (right < size - 1 && !IsBlack(px[y * size + (right + 1)])) right++;
            return right - left + 1;
        }
    }
}
