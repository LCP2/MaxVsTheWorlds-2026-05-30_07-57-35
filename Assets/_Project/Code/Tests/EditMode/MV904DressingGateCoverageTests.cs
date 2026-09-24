using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-904 — MV-887's area gate (<see cref="MapStaticBatchRoot.ApplyAreaGate"/>) only ever disables a
    /// renderer it TAGGED with a zone. <see cref="MapStaticBatchRoot"/>'s own <c>TagDressingSludge</c>
    /// walked only the "Stormdrain Dressing" root's "Sludge" child, so every OTHER thing
    /// <see cref="StormdrainDressing.Dress"/> builds (wall kerbs/pipes/lamps/soffits, the overhead
    /// structure, the drain-machinery props that replace cover boxes, panel joints/bays/stains,
    /// hazard-bulkhead fittings) was never tagged and so never gated — always on, everywhere, for the
    /// whole run. That untagged dressing is the bulk of a built World 2 area's renderer count, which is
    /// why Lee's live-build readout (MV-904's evidence image) showed 19,657 of 31,105 renderers still
    /// enabled after MV-887/MV-890/MV-891 landed.
    ///
    /// Fails on the base commit this branch was cut from (<c>d635be9</c>, pre-MV-904): the assertion
    /// below re-measures the SAME baseline enabled count area10 produces on that commit, so it fails on
    /// itself (0% reduction) until the widened tagging below actually ships. See the fix commit / Jira
    /// comment for this test's own base-commit failure output.
    ///
    /// Withdrawn by design-chat + triage adjudication 2026-09-22/23 (see ticket): the original literal
    /// "under 25% enabled" ceiling. area10 carries a genuine <see cref="MapLink"/> to area16, a 106m x
    /// 12m elevated deck corridor with no floor zone underneath it, dressed along its whole length —
    /// correctly attributing that corridor's own dressing to it (rather than scattering it onto whichever
    /// ordinary room happens to sit nearest, which measured LESS correct) keeps area10 over any fixed
    /// absolute ceiling regardless of how complete the tagging is. The criteria here assert the RELATIVE
    /// reduction and the MECHANISM (tagging coverage, MV-890 guard) instead — deliberately independent of
    /// which census (EditMode build vs. live on-screen) the number is read against.
    ///
    /// One consolidated test (MV-465 Rule 1) against the real shipped World 2 config, built the same
    /// Build-then-Dress order <see cref="MaxWorlds.Arena.BackyardPath.Awake"/> uses for World 2 (same
    /// trick <c>MV890AreaGateDressingPairingTests</c> already uses), with <see cref="MapStaticBatchRoot"/>'s
    /// own <c>Start()</c> reflection-invoked (never fires on its own outside Play mode). Every assertion
    /// reads a RESOLVED value off the built hierarchy (Rule 2, Tier 2) — never an authored constant, never
    /// a rendered pixel.
    /// </summary>
    public sealed class MV904DressingGateCoverageTests
    {
        // MV-904 AC1 — measured directly on this branch's own base commit d635be9 (current main before
        // this ticket's fix), same Build → Dress → Start → ApplyAreaGate("area10") setup this test
        // performs below. Recorded as a constant (rather than re-derived at runtime, which would have
        // nothing pre-fix to compare against within a single run) so a future regression shows up as a
        // number moving, not as a silently-passing test. Captured from this exact test's own base-commit
        // failure output (cc-verify Logs\editmode-results.xml, d635be9): "MV-904 AC1: area10 enabled
        // 22425/28814 ... reduction -0.5% (must be >= 60.0%)" — see the fix commit for the full quote.
        private const int BaselineEnabledArea10 = 22425;
        private const int BaselineTotalArea10 = 28814;

        [Test]
        public void GatedToArea10_EnabledCountDrops60PercentFromBase_DressingCoverageComplete_AndMV890GuardHoldsEverywhere()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see other World2 EditMode tests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV904 Host");
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);

                // The exact BackyardPath.Awake order for World 2: Build, THEN Dress, both before
                // MapStaticBatchRoot.Start() (and its first ApplyAreaGate call) has ever run.
                StormdrainDressing.Dress(host.transform, map, built.Cover);

                Transform mapRoot = host.transform.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
                var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
                Assert.IsNotNull(batchRoot, "MapRuntime never attached its own MapStaticBatchRoot");

                InvokePrivate(batchRoot, "Start");
                batchRoot.ApplyAreaGate("area10");

                var rendererZones =
                    (Dictionary<Renderer, List<string>>)GetPrivateField(batchRoot, "_rendererZones");
                Assert.IsNotNull(rendererZones, "setup failure: MapStaticBatchRoot never carried its own rendererZones");

                Transform dressingRoot = host.transform.Find("Stormdrain Dressing");
                Transform sludgeHost = dressingRoot != null ? dressingRoot.Find("Sludge") : null;

                // ---- AC1/AC2 tagging-report breakdown ----
                int mapGeomTotal = 0, mapGeomTagged = 0;
                int dressingTotal = 0, dressingTagged = 0;
                int sludgeTotal = 0, sludgeTagged = 0;
                int gameplayTotal = 0;
                int groundSlabTotal = 0;
                int enabledCount = 0, totalCount = 0;

                foreach (Renderer r in host.GetComponentsInChildren<Renderer>(true))
                {
                    totalCount++;
                    if (r.enabled) enabledCount++;

                    // MV-934: "tagged" alone used to mean "the gate can control this renderer" -- true
                    // when every controllable renderer stayed individually tracked. Now a renderer the
                    // gate tagged can ALSO have been folded into a combined static mesh for its own zone
                    // (MapStaticBatchRoot.CombineZoneGeometry), which removes its own dict entry and
                    // permanently disables its own renderer by design -- that is the fix working, not a
                    // coverage gap. "Accounted for" is the real invariant this test guards: either still
                    // individually tracked, or permanently off because something else (the combined mesh)
                    // now draws it. What must never happen is untracked AND still enabled -- exactly the
                    // "always on, everywhere, forever" defect this ticket (MV-904) exists to catch.
                    bool tagged = rendererZones.ContainsKey(r);
                    bool accounted = tagged || !r.enabled;
                    bool isSludge = sludgeHost != null && r.transform.IsChildOf(sludgeHost);
                    bool isDressing = !isSludge && dressingRoot != null && r.transform.IsChildOf(dressingRoot);
                    bool isGameplay = !isSludge && !isDressing &&
                        (r.GetComponentInParent<RobotEnemy>() != null ||
                         r.GetComponentInParent<Replicator>() != null ||
                         r.GetComponentInParent<MowerHutch>() != null ||
                         r.GetComponentInParent<BigBermudaBoss>() != null ||
                         r.GetComponentInParent<AreaGate>() != null);
                    bool isGroundSlab = !isSludge && !isDressing && !isGameplay && r.name == "Map Floor";

                    if (isSludge) { sludgeTotal++; if (accounted) sludgeTagged++; }
                    else if (isDressing) { dressingTotal++; if (accounted) dressingTagged++; }
                    else if (isGameplay) gameplayTotal++;
                    else if (isGroundSlab) groundSlabTotal++;
                    else { mapGeomTotal++; if (accounted) mapGeomTagged++; }
                }

                float reduction = BaselineEnabledArea10 > 0 ? 1f - (float)enabledCount / BaselineEnabledArea10 : 0f;
                float dressingCoverage = dressingTotal > 0 ? (float)dressingTagged / dressingTotal : 1f;

                Debug.Log("MV-904 AC1: area10 enabled " +
                    $"{enabledCount}/{totalCount} vs base-commit baseline {BaselineEnabledArea10}/{BaselineTotalArea10} " +
                    $"— reduction {reduction:P1} (must be >= 60.0%).");
                Debug.Log("MV-904 AC2: accounted-for coverage (tagged, or permanently off because MV-934 " +
                    "folded it into a combined zone mesh instead) — " +
                    $"stormdrainDressing(non-sludge) {dressingTagged}/{dressingTotal} ({dressingCoverage:P1}), " +
                    $"sludge {sludgeTagged}/{sludgeTotal}, mapGeometry {mapGeomTagged}/{mapGeomTotal}, " +
                    $"gameplay {gameplayTotal} (excluded by design), groundSlab {groundSlabTotal} (excluded by design).");

                // ---- AC3: the MV-890 guard, generalised to every one of World 2's 22 areas — gating to
                // a dressed cover piece's OWN zone must never re-enable its box (the "pipes encased in
                // grey blocks" regression MV-890 fixed), no matter which area is current. ----
                int areasChecked = 0;
                foreach (MapZone z in map.zones)
                {
                    if (z == null) continue;
                    areasChecked++;
                    batchRoot.ApplyAreaGate(z.id);

                    foreach (CoverPiece piece in built.Cover)
                    {
                        if (piece.Body == null || piece.Cover.Dressing == CoverDressing.None) continue;
                        Renderer boxRenderer = piece.Body.GetComponent<Renderer>();
                        if (boxRenderer == null) continue;
                        if (!rendererZones.TryGetValue(boxRenderer, out List<string> pieceZones)) continue;
                        if (!pieceZones.Contains(z.id)) continue; // only check while its own zone is active

                        Assert.IsFalse(boxRenderer.enabled,
                            $"MV-904/MV-890 guard: '{piece.Body.name}' was dressed (its box renderer disabled by " +
                            $"StormdrainDressing) and is tagged with {z.id} — gating to its own zone must never " +
                            "re-enable it (the 'pipes encased in grey blocks' regression), for every area in the world");
                    }
                }
                Assert.AreEqual(22, areasChecked,
                    "setup failure: World 2 must carry exactly 22 zones for the MV-890 guard to run over 'every area in the world' (AC3)");

                // ---- AC4: per-area enabled-renderer range across all 22 areas, so a future regression
                // is visible as a number rather than as a frame-rate complaint. ----
                int narrowest = int.MaxValue, widest = int.MinValue;
                string narrowestId = null, widestId = null;
                foreach (MapZone z in map.zones)
                {
                    if (z == null) continue;
                    batchRoot.ApplyAreaGate(z.id);
                    int enabledHere = 0;
                    foreach (Renderer r in host.GetComponentsInChildren<Renderer>(true))
                        if (r.enabled) enabledHere++;
                    if (enabledHere < narrowest) { narrowest = enabledHere; narrowestId = z.id; }
                    if (enabledHere > widest) { widest = enabledHere; widestId = z.id; }
                }
                Debug.Log("MV-904 AC4: per-area enabled-renderer range across " +
                    $"{areasChecked} areas — narrowest {narrowestId} ({narrowest}), widest {widestId} ({widest}).");

                // Restore to area10 (the zone every assertion above/below is stated against).
                batchRoot.ApplyAreaGate("area10");

                // ---- AC2 (second half): TagWallZones behaviour is unchanged — a wall bordering no room
                // is still left untagged. If the nearest-zone fallback this ticket adds for dressing/
                // static props had also leaked into TagWallZones, a wall's "no room on this side" sample
                // would get backfilled to the nearest zone instead of staying genuinely absent, and every
                // StructuralWall would carry 2 tags instead of some legitimately carrying just 1 (a true
                // boundary wall, bordering the map edge on one side and a single room on the other). ----
                bool foundSingleSidedWall = false;
                foreach (Renderer r in mapRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.GetComponent<StructuralWall>() == null) continue;
                    if (rendererZones.TryGetValue(r, out List<string> wallZones) && wallZones.Count == 1)
                    {
                        foundSingleSidedWall = true;
                        break;
                    }
                }
                Assert.IsTrue(foundSingleSidedWall,
                    "MV-904 AC2: TagWallZones must still leave a boundary wall's roomless side untagged — " +
                    "expected at least one StructuralWall tagged with exactly one zone, found none (the " +
                    "nearest-zone fallback may have leaked into wall tagging)");

                // ---- AC1 / AC2 (first half) final gates ----
                Assert.GreaterOrEqual(reduction, 0.6f,
                    $"MV-904 AC1: area10 enabled count must drop at least 60% from the base-commit baseline " +
                    $"({BaselineEnabledArea10}/{BaselineTotalArea10}) — measured {enabledCount}/{totalCount} " +
                    $"({reduction:P1} reduction).");
                Assert.GreaterOrEqual(dressingCoverage, 0.99f,
                    $"MV-904 AC2: non-sludge Stormdrain Dressing accounted-for coverage must be >= 99% — " +
                    $"measured {dressingTagged}/{dressingTotal} ({dressingCoverage:P1}).");
            }
            finally
            {
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(target, null);

        private static object GetPrivateField(object target, string fieldName) =>
            target.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(target);
    }
}
