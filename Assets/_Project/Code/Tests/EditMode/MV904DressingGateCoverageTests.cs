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
    /// renderer it TAGGED with a zone (<see cref="MapStaticBatchRoot"/>'s own doc: "a renderer never
    /// added here... is never touched and keeps whatever state it already had"). Before this ticket,
    /// <see cref="MapStaticBatchRoot"/>'s own <c>TagDressingSludge</c> walked only the "Stormdrain
    /// Dressing" root's "Sludge" child — every OTHER thing <see cref="StormdrainDressing.Dress"/> builds
    /// (wall kerbs/pipes/lamps/soffits, the overhead structure, the drain-machinery props that replace
    /// cover boxes, panel joints/bays/stains, hazard-bulkhead light fittings) was never tagged and so
    /// never gated — always on, everywhere, for the whole run. That untagged dressing is the bulk of a
    /// built World 2 area's renderer count, which is why Lee's own live-build readout (MV-904's evidence
    /// image) showed 19,657 of 31,105 renderers still enabled after MV-887/MV-890/MV-891 landed: map
    /// geometry (walls, cover, decks — tagged by <see cref="MapRuntime.Build"/> itself) was already being
    /// gated correctly; the dressing laid on top of it was not.
    ///
    /// Fails on b034639 (MV-890+MV-891, the build the evidence image was captured against): before this
    /// ticket's fix, standing in area10 with only its own zone plus real neighbours active still leaves
    /// nearly every dressing renderer in the WHOLE map enabled (measured: 77.7%), so the enabled fraction
    /// sits far above the 25% ceiling.
    ///
    /// STILL FAILS after this ticket's fix (tagging is now complete — every group below reads 100%
    /// tagged except the handful of always-disabled deck-parapet collider blockers, which were never
    /// meant to be tagged) — measured 28.4%, not under 25%. This is not a residual tagging gap: it is
    /// area10's own real, authored neighbourhood. area10 carries a genuine <c>MapLink</c> (gate g36) to
    /// area16, a 106 m x 12 m elevated deck corridor with no floor zone underneath it and kerb/pipe
    /// dressing running its whole length — once that corridor's own dressing is correctly attributed to
    /// it (rather than scattered onto whichever ordinary room happens to sit nearest, which is LESS
    /// correct, not more), it alone accounts for enough of the world's total dressing to keep area10's
    /// active neighbourhood over the ceiling. Left asserting the ticket's own literal threshold (not
    /// loosened) so this remains provably red until a human decides how to close the gap — see this
    /// ticket's Jira comment for the full measured breakdown and the two options that comment lays out.
    ///
    /// One consolidated test (MV-465 Rule 1) against the real shipped World 2 config, built the same
    /// Build-then-Dress order <see cref="MaxWorlds.Arena.BackyardPath.Awake"/> uses for World 2 (same
    /// trick <c>MV890AreaGateDressingPairingTests</c> already uses), with <see cref="MapStaticBatchRoot"/>'s
    /// own <c>Start()</c> reflection-invoked (never fires on its own outside Play mode). Two RESOLVED-value
    /// assertions (Rule 2, Tier 2): the gated-enabled fraction standing in area10, and — generalised from
    /// MV-890's single-area check to every dressed cover piece in the world — that gating to a dressed
    /// piece's OWN zone never re-enables its box (the "pipes encased in grey blocks" regression MV-890
    /// fixed must not come back as a side effect of widening what gets tagged). The second assertion
    /// passes; only the first (the 25% ceiling) remains red.
    /// </summary>
    public sealed class MV904DressingGateCoverageTests
    {
        [Test]
        public void GatedToArea10_EnabledRendererFractionUnder25Percent_AndDressedCoverNeverReenabled()
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

                // ---- AC1 tagging-report breakdown (also the source for the ticket's required Jira
                // comment) — a renderer's own owner group, and whether the gate's own rendererZones
                // dictionary carries it at all. ----
                var rendererZones =
                    (Dictionary<Renderer, List<string>>)GetPrivateField(batchRoot, "_rendererZones");
                Assert.IsNotNull(rendererZones, "setup failure: MapStaticBatchRoot never carried its own rendererZones");

                Transform dressingRoot = host.transform.Find("Stormdrain Dressing");
                Transform sludgeHost = dressingRoot != null ? dressingRoot.Find("Sludge") : null;

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

                    bool tagged = rendererZones.ContainsKey(r);
                    bool isSludge = sludgeHost != null && r.transform.IsChildOf(sludgeHost);
                    bool isDressing = !isSludge && dressingRoot != null && r.transform.IsChildOf(dressingRoot);
                    bool isGameplay = !isSludge && !isDressing &&
                        (r.GetComponentInParent<RobotEnemy>() != null ||
                         r.GetComponentInParent<Replicator>() != null ||
                         r.GetComponentInParent<MowerHutch>() != null ||
                         r.GetComponentInParent<BigBermudaBoss>() != null ||
                         r.GetComponentInParent<AreaGate>() != null);
                    bool isGroundSlab = !isSludge && !isDressing && !isGameplay && r.name == "Map Floor";

                    if (isSludge) { sludgeTotal++; if (tagged) sludgeTagged++; }
                    else if (isDressing) { dressingTotal++; if (tagged) dressingTagged++; }
                    else if (isGameplay) gameplayTotal++;
                    else if (isGroundSlab) groundSlabTotal++;
                    else { mapGeomTotal++; if (tagged) mapGeomTagged++; }
                }

                // Active zone set for area10 (report only) — recomputed off the same public
                // map.links/map.zones/MapZone.ShareFootprint data ApplyAreaGate itself reads, so the
                // Jira report can name exactly which zones the gate considered active without
                // reimplementing (or re-asserting) the gate's own resolved behaviour.
                var active = new HashSet<string> { "area10" };
                foreach (MapLink link in map.links)
                {
                    if (link == null) continue;
                    if (link.from == "area10") active.Add(link.to);
                    else if (link.to == "area10") active.Add(link.from);
                }
                var activeZoneObjs = new List<MapZone>();
                foreach (MapZone z in map.zones)
                    if (z != null && active.Contains(z.id)) activeZoneObjs.Add(z);
                foreach (MapZone z in map.zones)
                {
                    if (z == null || active.Contains(z.id)) continue;
                    foreach (MapZone match in activeZoneObjs)
                        if (MapZone.ShareFootprint(z, match)) { active.Add(z.id); break; }
                }
                var activeSorted = new List<string>(active);
                activeSorted.Sort();

                float pct = totalCount > 0 ? (float)enabledCount / totalCount : 0f;

                var untaggedSample = new List<string>();
                foreach (Renderer r in host.GetComponentsInChildren<Renderer>(true))
                {
                    if (untaggedSample.Count >= 20) break;
                    if (rendererZones.ContainsKey(r)) continue;
                    bool isSludge = sludgeHost != null && r.transform.IsChildOf(sludgeHost);
                    bool isDressing = !isSludge && dressingRoot != null && r.transform.IsChildOf(dressingRoot);
                    if (r.name == "Map Floor") continue;
                    untaggedSample.Add($"[{(isSludge ? "sludge" : isDressing ? "dressing" : "mapGeom")}] {r.name}@{r.transform.position:F2}");
                }
                Debug.Log("MV-904 untagged sample: " + string.Join(" | ", untaggedSample));

                Debug.Log("MV-904 tagging report (player in area10): " +
                    $"mapGeometry tagged {mapGeomTagged}/{mapGeomTotal}; " +
                    $"stormdrainDressing(non-sludge) tagged {dressingTagged}/{dressingTotal}; " +
                    $"sludge tagged {sludgeTagged}/{sludgeTotal}; " +
                    $"gameplay {gameplayTotal} (never tagged by design); " +
                    $"groundSlab {groundSlabTotal} (never tagged by design, single map-spanning slab); " +
                    $"active zones [{string.Join(", ", activeSorted)}] ({active.Count}/{map.zones.Length} total zones); " +
                    $"enabled {enabledCount}/{totalCount} ({pct:P1}).");

                // ---- AC3 / MV-890 guard, generalised to every dressed cover piece in the world, not
                // just area3 — widening what TagDressingSludge tags must never reopen the "pipes encased
                // in grey blocks" regression MV-890 fixed. Run BEFORE the AC2 assertion below: Assert.Less
                // throws on failure, and AC2 is expected to still be red (see class doc comment) — this
                // guard must still execute and report on its own merits rather than being skipped. -----
                foreach (CoverPiece piece in built.Cover)
                {
                    if (piece.Body == null || piece.Cover.Dressing == CoverDressing.None) continue;
                    Renderer boxRenderer = piece.Body.GetComponent<Renderer>();
                    if (boxRenderer == null || boxRenderer.enabled) continue; // only pieces Dress actually hid

                    // Reads back the SAME zone attribution the gate itself tagged this renderer with
                    // (rather than re-deriving it via a second, independent MapData.ZoneAt call) — a
                    // piece the gate's own nearest-zone fallback resolved (e.g. a16's own cover, whose
                    // exact position sits outside every zone's footprint) has no zone from a plain
                    // ZoneAt lookup, so re-deriving here would fail this test's own setup for a piece
                    // the production gate resolves correctly.
                    Assert.IsTrue(rendererZones.TryGetValue(boxRenderer, out List<string> pieceZones) && pieceZones.Count > 0,
                        $"setup failure: cover piece '{piece.Body.name}' was never tagged with a zone by the gate itself");

                    foreach (string zoneId in pieceZones)
                    {
                        batchRoot.ApplyAreaGate(zoneId);
                        Assert.IsFalse(boxRenderer.enabled,
                            $"MV-904/MV-890 guard: '{piece.Body.name}' was dressed (its box renderer disabled by " +
                            $"StormdrainDressing) and is tagged with {zoneId} — gating to its own zone must never " +
                            "re-enable it (the 'pipes encased in grey blocks' regression)");
                    }
                }

                Assert.Less(pct, 0.25f,
                    $"MV-904: standing in area10, {enabledCount}/{totalCount} ({pct:P1}) renderers are left " +
                    "enabled — must be under 25%. Breakdown — mapGeometry tagged " +
                    $"{mapGeomTagged}/{mapGeomTotal}, stormdrainDressing(non-sludge) tagged " +
                    $"{dressingTagged}/{dressingTotal}, sludge tagged {sludgeTagged}/{sludgeTotal}, gameplay " +
                    $"{gameplayTotal} (excluded by design), groundSlab {groundSlabTotal} (excluded by design). " +
                    $"active zones for area10: [{string.Join(", ", activeSorted)}] ({active.Count}/{map.zones.Length}).");
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
