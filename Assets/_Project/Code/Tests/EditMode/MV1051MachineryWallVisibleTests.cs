using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1051: <see cref="ReefDressing.DressCover"/> hid EVERY <see cref="CoverDressing.Machinery"/>
    /// piece's own renderer and stood a single 1 m-radius <see cref="MaxWorlds.Rendering.ReefKit.
    /// BuildCoolantTurret"/> cylinder at its centre — fine for the 2x2 blocks that pattern was designed
    /// for, but the V6 layout (MV-1030) also authors machinery WALLS up to 18 m long. World 3 area 4's
    /// own four long pieces (<c>a4_c1</c> 1x18, <c>a4_c2</c> 1x11, <c>a4_c5</c> 1x18, <c>a4_c7</c> 1x9)
    /// each drew as one 1 m blip with an invisible collider either side — the "vertical invisible wall"
    /// Lee hit entering a4 from the west gate (iOS v0.11.3).
    ///
    /// Asserts RESOLVED state only (Rule 2, Tier 2), through the real load path
    /// (<see cref="MapRuntime.Build"/> -&gt; <see cref="ReefDressing.DressCover"/>, the same order
    /// <see cref="MV1019ReefFloorContrastTests"/> and <see cref="MV745World3HullDressingTests"/> already
    /// use): for every a4 machinery piece wider than the fix's own 2.5 m threshold on either horizontal
    /// axis, the block's own renderer must stay enabled and its resolved <see cref="Renderer.bounds"/>
    /// must cover at least 90% of the piece's authored XZ footprint. Pieces at or under 2.5 m on both
    /// axes (a4_c3/c4/c6/c8/c9) keep today's turret swap by design (the ticket's own "Change" section)
    /// and are deliberately out of scope here — a turret was never meant to fill a 2x2 footprint, only
    /// mark it.
    ///
    /// Fails on 12419fa (the commit before this fix): every machinery piece's own renderer is disabled
    /// outright, so the first assertion (<c>Assert.IsTrue(r.enabled, ...)</c>) fails for <c>a4_c1</c>.
    ///
    /// MV-1216: the Reef's own config now authors these four pieces' dressing as
    /// <see cref="CoverDressing.ReefManifold"/> (the V8 layout's rename of the old bare "machinery"
    /// word, MV-1215), which — unlike the old <see cref="CoverDressing.Machinery"/> path this test
    /// originally keyed on — always hides the cover piece's own body and builds a separate
    /// <see cref="ReefKit.BuildManifold"/> root instead, for every size. The 90%-footprint-coverage
    /// check this test exists for still has to hold; it just has to look at whichever renderer is
    /// actually live over the footprint now, not assume it is <c>piece.Body</c>'s own.
    /// <see cref="ReefKit.BuildManifold"/> scales its own "Base" box to the piece's full authored
    /// <c>size.x</c>/<c>size.z</c>, so the coverage check still passes for the real reason MV-1051
    /// cared about: no invisible wall segment either side of a long manifold run.
    /// </summary>
    public sealed class MV1051MachineryWallVisibleTests
    {
        private const float MinCoverageFraction = 0.90f;

        [Test]
        public void LongMachineryCoverInAreaFour_KeepsItsOwnVisibleBlockAcrossItsFullFootprint()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV1051 World3 Probe Root");
            try
            {
                // Exactly BackyardPath.Awake's own order: geometry, then the Reef-only cover pass.
                MapBuild built = MapRuntime.Build(map, host.transform);
                ReefDressing.DressCover(host.transform, built.Cover);

                List<CoverPiece> longA4Machinery = built.Cover
                    .Where(p => p.Cover.Name.StartsWith("a4_") && p.Cover.Dressing == CoverDressing.ReefManifold)
                    .Where(p => p.Cover.Size.x > 2.5f || p.Cover.Size.z > 2.5f)
                    .ToList();

                Assert.AreEqual(4, longA4Machinery.Count,
                    "World 3 area 4 should still author exactly its 4 long machinery pieces " +
                    "(a4_c1, a4_c2, a4_c5, a4_c7) for this test to mean anything");

                Renderer[] liveRenderers = host.GetComponentsInChildren<Renderer>(true);

                var failures = new List<string>();
                foreach (CoverPiece piece in longA4Machinery)
                {
                    Rect footprint = piece.Cover.Footprint;
                    float area = footprint.width * footprint.height;

                    float bestCoverage = 0f;
                    foreach (Renderer r in liveRenderers)
                    {
                        if (r == null || !r.enabled) continue;

                        Bounds b = r.bounds;
                        float ix = Mathf.Max(0f, Mathf.Min(footprint.xMax, b.max.x) - Mathf.Max(footprint.xMin, b.min.x));
                        float iz = Mathf.Max(0f, Mathf.Min(footprint.yMax, b.max.z) - Mathf.Max(footprint.yMin, b.min.z));
                        bestCoverage = Mathf.Max(bestCoverage, (ix * iz) / area);
                    }

                    if (bestCoverage < MinCoverageFraction)
                        failures.Add($"{piece.Cover.Name}: no enabled renderer covers at least " +
                                     $"{MinCoverageFraction:P0} of its {area:F1} m^2 authored footprint " +
                                     $"(best found: {bestCoverage:P0})");
                }

                Assert.IsEmpty(failures, "MV-1051 machinery footprint violations:\n" + string.Join("\n", failures));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
