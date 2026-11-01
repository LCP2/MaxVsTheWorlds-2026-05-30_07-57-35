using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1334: the Reef deck's circuit inlay baked a trace+node layout into EVERY cell of the
    /// <see cref="WorldMaterials.ReefDeckGridCells"/>-square supertile (MV-1256), and MV-1316 made that
    /// mask actually visible on Metal/GLES. With Mobile-tier HDR on (MV-1273) the result bloomed and
    /// read louder than the robots (Lee, 31 Oct). The fix thins every trace, drops round nodes
    /// entirely, and only lights ~1 cell in 4.
    ///
    /// One test, three RESOLVED-value checks (testing policy Tier 2), none a restated copy of an
    /// authored constant: (a) what share of <see cref="StylizedTextures.ReefDeckEmission"/>'s actual
    /// baked cells carry any lit texel at all (AC2's "20-30%"), (b) whether every lit pixel still
    /// belongs to a cell-spanning straight trace rather than a localised node blob (AC2's "no round
    /// node shapes remain"), and (c) the REAL deck material's resolved _EmissionColor for whichever
    /// graphics backend this run is actually on, compared against <see cref="WorldMaterials.ReefDeckEmissionD3D"/>/
    /// <see cref="WorldMaterials.ReefDeckEmissionOther"/> (AC3's "the two emission constants").
    ///
    /// Fails on the base commit: every one of the 16 cells carries an inlay (100%, not 20-30%), the
    /// baked mask still has round node blobs, and the resolved material emission reads the pre-MV-1334
    /// 0.85/0.55 pair.
    /// </summary>
    public sealed class MV1334ReefDeckInlayDensityTests
    {
        [Test]
        public void ReefDeckEmission_SparserDimmerAndNodeFree()
        {
            Texture2D tex = StylizedTextures.ReefDeckEmission();
            int size = tex.width;
            Color32[] px = tex.GetPixels32();

            int cells = WorldMaterials.ReefDeckGridCells;
            int cellSize = size / cells;
            int totalCells = cells * cells;
            int litCells = 0;

            for (int gy = 0; gy < cells; gy++)
            for (int gx = 0; gx < cells; gx++)
            {
                int originX = gx * cellSize, originY = gy * cellSize;
                if (!CellHasAnyLitTexel(px, size, originX, originY, cellSize)) continue;
                litCells++;

                // No round node shapes: every lit texel in a lit cell must belong to a column or row
                // that is fully lit top-to-bottom/edge-to-edge WITHIN the cell - the shape a straight,
                // cell-spanning trace draws. A node is a small filled disc whose own pixels mostly fail
                // both checks (its span in either axis is a fraction of the cell, never the whole cell).
                for (int ly = 0; ly < cellSize; ly++)
                for (int lx = 0; lx < cellSize; lx++)
                {
                    int x = originX + lx, y = originY + ly;
                    if (IsBlack(px[y * size + x])) continue;

                    bool onFullColumn = ColumnFullyLitWithinCell(px, size, x, originY, cellSize);
                    bool onFullRow = RowFullyLitWithinCell(px, size, y, originX, cellSize);
                    Assert.IsTrue(onFullColumn || onFullRow,
                        $"lit texel at ({x},{y}) in cell ({gx},{gy}) belongs to neither a full-height " +
                        "column trace nor a full-width row trace — looks like a round node blob rather " +
                        "than a straight trace");
                }
            }

            float share = (float)litCells / totalCells;
            Assert.GreaterOrEqual(share, 0.20f,
                $"{litCells}/{totalCells} cells ({share:P0}) carry an inlay — below AC2's 20% floor");
            Assert.LessOrEqual(share, 0.30f,
                $"{litCells}/{totalCells} cells ({share:P0}) carry an inlay — above AC2's 30% ceiling");

            // Literal caps, same idiom MV1332ShipBloomCapTests already uses for this exact reason: a
            // reference to WorldMaterials.ReefDeckEmissionD3D/ReefDeckEmissionOther here wouldn't even
            // compile on the pre-fix base commit, which would make this a compile-error "failure"
            // rather than the real resolved-value mismatch (0.85/0.55 vs these) the ticket's fix-first
            // proof needs to show.
            bool isDirect3D = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11
                || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D12;
            float expectedEmission = isDirect3D ? 0.40f : 0.25f;
            Color resolvedEmission = WorldMaterials.M_ShipFloor.GetColor("_EmissionColor");

            Assert.AreEqual(expectedEmission, resolvedEmission.r, 0.001f,
                $"resolved deck _EmissionColor.r ({resolvedEmission.r}) does not match the " +
                $"{(isDirect3D ? "D3D" : "non-D3D")} constant ({expectedEmission})");
            Assert.AreEqual(expectedEmission, resolvedEmission.g, 0.001f);
            Assert.AreEqual(expectedEmission, resolvedEmission.b, 0.001f);
        }

        private static bool IsBlack(Color32 c) => c.r == 0 && c.g == 0 && c.b == 0;

        private static bool CellHasAnyLitTexel(Color32[] px, int size, int originX, int originY, int cellSize)
        {
            for (int ly = 0; ly < cellSize; ly++)
            for (int lx = 0; lx < cellSize; lx++)
                if (!IsBlack(px[(originY + ly) * size + (originX + lx)])) return true;
            return false;
        }

        private static bool ColumnFullyLitWithinCell(Color32[] px, int size, int x, int originY, int cellSize)
        {
            for (int ly = 0; ly < cellSize; ly++)
                if (IsBlack(px[(originY + ly) * size + x])) return false;
            return true;
        }

        private static bool RowFullyLitWithinCell(Color32[] px, int size, int y, int originX, int cellSize)
        {
            for (int lx = 0; lx < cellSize; lx++)
                if (IsBlack(px[y * size + (originX + lx)])) return false;
            return true;
        }
    }
}
