using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1272 (one new EditMode test, MV-465 Rule 1): Lee saw the Mothership's wall tops strobe
    /// orange/blue on a real iPhone (TestFlight 0.18.0, 21 Oct) — two coplanar, differently-coloured
    /// surfaces depth-fighting. <see cref="ShipAreaKit.BuildWallDressing"/> dresses a structural wall
    /// once per area per side, so a wall shared by two neighbouring areas (e.g. a7's south wall IS a8's
    /// north wall) gets its top-only pieces built twice. Fails on base commit <c>5e204b3</c>: builds the
    /// real World 5 (Mothership) map and dressing for all 30 areas, then scans every pair of enabled
    /// renderers under the ship dressing roots ("Ship Wall Dressing", "Ship Floors", "Ship Set Pieces")
    /// for a coplanar, overlapping, different-coloured pair — see Logs/MV1272-failfirst.log, quoted in
    /// the fix comment, for the first 5 offending pairs by GameObject name.
    ///
    /// "Different sharedMaterial colours" is read off the renderer's own <see cref="Renderer.sharedMaterial"/>
    /// REFERENCE, not <see cref="Material.color"/>: <see cref="MaxWorlds.Rendering.MaterialLibrary.Tinted"/>
    /// bakes a kit tone into an albedo TEXTURE (so two differently-toned Tinted materials both carry a
    /// near-white <c>_BaseColor</c> and would look identical to a naive <c>.color</c> read) but caches one
    /// Material instance per quantised tone, so two renderers painted different tones always carry two
    /// distinct Material instances. Reference inequality is therefore the resolved-value (Tier 2) signal
    /// that actually tracks "different colour" for this project's material system, Unlit tones included
    /// (<see cref="MaxWorlds.Rendering.StormdrainKit.Unlit"/> is cached the same "one instance per colour"
    /// way).
    ///
    /// Pairs are swept in top-Y sorted order (not brute O(n^2)) since only renderers within the 0.002 m Y
    /// tolerance of each other can ever match — thousands of renderers across 30 areas sit at dozens of
    /// distinct Y bands (floor, kick strip, panel face, cap, pipes, trim), so the sweep touches a tiny
    /// neighbourhood per renderer in practice.
    ///
    /// A first pass on the literal AC1 wording (any overlap &gt; 0.01 m^2, no further exemption) found
    /// thousands of pairs — every one a long-shipped, working, deliberately-offset decorative layer one
    /// single kit CALL builds together on purpose (MV-1259/MV-1264's floor decal/seam-glow stack; one
    /// <c>BuildBulkheadPanel</c> call's own kick-plate glow lapping its own floor kick strip and a wall
    /// lamp's own light pool a few cm away), plus every additive light pool that overlaps BY DESIGN —
    /// none of them a duplicate build. What actually distinguishes "one call's own intentional layering"
    /// from "two different calls landing on each other" (the real bug:
    /// <see cref="ShipAreaKit.BuildWallDressing"/> invoking
    /// <see cref="MaxWorlds.Rendering.ShipKit.BuildBulkheadPanel"/> once per area per side) is which
    /// top-level child of the dressing root a renderer descends from — one such child IS one build
    /// call's own output (one "Wall {area} {side}", one "Atlas Floor A{n}", one hero/theme group). Two
    /// renderers under the SAME top-level child are that one call's own business; only a pair split
    /// across two DIFFERENT top-level children can be the cross-build duplicate this ticket is about.
    /// Additive/transparent surfaces (ZWrite off) are excluded outright — see the render-queue check
    /// below — since depth-FIGHTING is physically impossible for a surface that never writes depth.
    ///
    /// With both of those applied, the base-commit run (quoted in the fix comment, Logs/MV1272-failfirst.log)
    /// surfaced three distinct, real defects — none of them hypothetical, all found by actually running
    /// this test rather than by re-deriving the ticket's own (explicitly unproven) hypothesis:
    ///
    /// 1. The ticket's own hypothesis, confirmed: a wall shared by two areas got its cap/lamp-strip/rib
    ///    built twice (<see cref="ShipAreaKit.BuildWallDressing"/> fix: build them once per
    ///    <c>StructuralWall</c>, hide the second visit's renderers rather than skip building them, so a
    ///    per-side structural test like <c>MV1269ShipWallTopCapTests</c> still finds them).
    /// 2. NOT in the hypothesis: at an L-corner, two PERPENDICULAR walls' caps each span their own run
    ///    end-to-end, and both runs reach the same shared corner — so their thickness-wide footprints
    ///    overlap in a wallThickness-squared patch over the corner itself, each colour whatever its own
    ///    alternation put there. Fixed by insetting the cap's own tiled run at each end
    ///    (<c>ShipKit.BuildWallTopCap</c>), not by lifting — the duplicate geometry is removed, not hidden.
    /// 3. NOT in the hypothesis either: ~20 small, genuine, pre-existing coplanar overlaps between the
    ///    Mothership's per-area template theming (<c>ShipKit.BuildTemplateTheme</c> and friends: "Lane
    ///    Line", "Carpet Stripe", "Overhead Shadow", "Floor Number", "Grate Glow"/"Grate Glow Dim") and
    ///    the floor atlas's own seam grid (<c>ShipKit.BuildSeams</c>) — two unrelated kit passes that
    ///    both park a decal within a millimetre of the floor, independently, and sometimes land on the
    ///    same spot near an area's centre. Per the ticket's own change item 2 ("fix any other ... pair
    ///    your test finds: delete the duplicate, or lift one by at least 0.01 m"), each template-theme
    ///    piece was lifted 0.01 m.
    /// </summary>
    public sealed class MV1272WallTopDuplicateCapTests
    {
        private const float TopYTolerance = 0.002f;    // ticket AC1: "within 0.002 m in Y"
        private const float MinOverlapAreaXz = 0.01f;   // ticket AC1: "overlap by more than 0.01 m^2 in XZ"

        private static readonly string[] DressingRootNames = { "Ship Wall Dressing", "Ship Floors", "Ship Set Pieces" };

        private readonly struct Item
        {
            public readonly Renderer R;
            public readonly Material Mat;
            public readonly float TopY;
            public readonly Rect Xz;
            public readonly Transform BuildUnit;

            public Item(Renderer r, Material mat, float topY, Rect xz, Transform buildUnit)
            {
                R = r; Mat = mat; TopY = topY; Xz = xz; BuildUnit = buildUnit;
            }
        }

        /// <summary>The top-level child of <paramref name="dressingRoot"/> that <paramref name="t"/>
        /// descends from — one such child is one build call's own output (see class doc).</summary>
        private static Transform BuildUnitOf(Transform t, Transform dressingRoot)
        {
            Transform unit = t;
            while (unit.parent != null && unit.parent != dressingRoot)
                unit = unit.parent;
            return unit;
        }

        [Test]
        public void RealMothershipAllAreas_NoCoplanarOverlappingDifferentColouredDressingPair()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.Mothership);
            Assert.IsNotNull(cfg, "the Mothership's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            var root = new GameObject("MV-1272 Mothership Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                ShipDressing.Dress(root.transform, map, cfg, built.Cover);

                var items = new List<Item>();
                foreach (string rootName in DressingRootNames)
                {
                    Transform dressingRoot = root.transform.Find(rootName);
                    if (dressingRoot == null) continue;

                    foreach (Renderer r in dressingRoot.GetComponentsInChildren<Renderer>(false))
                    {
                        if (r == null || !r.enabled || r.sharedMaterial == null) continue;

                        // Depth-FIGHTING (the strobe this ticket is about) needs both surfaces to write
                        // depth and compete for the same z-buffer slot. StormdrainLightKit.AdditiveUnlit
                        // explicitly turns ZWrite off and pushes itself into the Transparent queue (a
                        // light pool is meant to overlap whatever is under it, on purpose) — it can never
                        // strobe regardless of how coplanar it sits, so it is not a candidate surface.
                        if (r.sharedMaterial.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.AlphaTest) continue;

                        Bounds b = r.bounds;
                        Transform buildUnit = BuildUnitOf(r.transform, dressingRoot);
                        items.Add(new Item(r, r.sharedMaterial, b.max.y, new Rect(b.min.x, b.min.z, b.size.x, b.size.z), buildUnit));
                    }
                }

                Assert.Greater(items.Count, 0,
                    "the Mothership's ship dressing roots must carry renderers for this test to mean anything");

                items.Sort((x, y) => x.TopY.CompareTo(y.TopY));

                var offenders = new List<string>();
                int checkedPairs = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    for (int j = i + 1; j < items.Count; j++)
                    {
                        float dy = items[j].TopY - items[i].TopY;
                        if (dy > TopYTolerance) break; // sorted ascending: nothing further can be in tolerance
                        checkedPairs++;

                        if (ReferenceEquals(items[i].Mat, items[j].Mat)) continue;

                        // Same top-level build unit = one call's own intentional layering (see class
                        // doc) — exempt. Only a cross-build-unit pair can be the duplicate this ticket
                        // is about.
                        if (items[i].BuildUnit == items[j].BuildUnit) continue;

                        float overlap = OverlapArea(items[i].Xz, items[j].Xz);
                        if (overlap <= MinOverlapAreaXz) continue;

                        offenders.Add($"'{items[i].BuildUnit.name}/{items[i].R.name}' ({items[i].Mat.name}) vs " +
                            $"'{items[j].BuildUnit.name}/{items[j].R.name}' ({items[j].Mat.name}) — " +
                            $"top Y {items[i].TopY:F4}/{items[j].TopY:F4}, XZ overlap {overlap:F3} m^2");
                    }
                }

                Assert.Greater(checkedPairs, 0,
                    "no pair of renderers shared a top-Y band — the sweep itself found nothing to compare");

                if (offenders.Count > 0)
                {
                    var report = new System.Text.StringBuilder();
                    report.AppendLine($"found {offenders.Count} coplanar, overlapping, different-coloured dressing pair(s) " +
                        $"across {items.Count} renderers checked. First {Mathf.Min(5, offenders.Count)}:");
                    for (int i = 0; i < Mathf.Min(5, offenders.Count); i++)
                        report.AppendLine($"  {i + 1}. {offenders[i]}");
                    Assert.Fail(report.ToString());
                }

                Assert.Pass($"{items.Count} renderers checked under the ship dressing roots, zero duplicate-cap pairs found.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static float OverlapArea(Rect a, Rect b)
        {
            float width = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float depth = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            if (width <= 0f || depth <= 0f) return 0f;
            return width * depth;
        }
    }
}
