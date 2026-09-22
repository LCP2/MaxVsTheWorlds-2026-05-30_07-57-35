using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-890 — a REGRESSION from MV-887: World 2's area gate (<see cref="MapStaticBatchRoot.ApplyAreaGate"/>)
    /// re-enabled a cover piece's box the moment its own zone became current, even though
    /// <see cref="StormdrainDressing.Dress"/> had already disabled that exact renderer earlier the same
    /// frame (the "collider stays, art swaps" contract every <c>DressCover</c> case keeps — the box has no
    /// art of its own left once dressed). A full-size grey box drawing again, around a dressing prop scaled
    /// to a fraction of its footprint, is exactly the reported "pipes are now encased in grey blocks".
    /// Separately, a deck/hatch overlay zone (<see cref="MapZone.level"/> &gt; 0) carries no
    /// <see cref="MapLink"/> to the floor zone it roofs — it is reached by a ramp, not a doorway — so it
    /// never entered <see cref="MapStaticBatchRoot.ApplyAreaGate"/>'s own "active" set no matter which
    /// floor zone was current, which is exactly the reported "a17's upper floor is gone" for a player
    /// standing in a3 (World 2's own real data: "area17" links sideways to area16/area18 only, never down
    /// to "area3", the floor zone it overlays).
    ///
    /// Fails on d85c178 (MV-887 + MV-888, the build Lee reported this against): before this ticket's fix,
    /// <see cref="MapStaticBatchRoot.ApplyAreaGate"/> unconditionally set every tagged renderer's own
    /// <c>enabled</c> from zone membership alone, with no notion of "already retired by dressing", and
    /// never folded a footprint-sharing overlay zone into "active" at all.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), against the real shipped World 2 config —
    /// exactly the world the live build regressed on, not a synthetic fixture. Builds the world the same
    /// way <see cref="MaxWorlds.Arena.BackyardPath.Awake"/> does for World 2 (<see cref="MapRuntime.Build"/>
    /// then <see cref="StormdrainDressing.Dress"/>, in that order, before the gate ever runs — reproducing
    /// the exact same-frame sequence the live build hits), then reflection-invokes
    /// <see cref="MapStaticBatchRoot"/>'s own <c>Start()</c> (same trick <c>MV887AreaRendererGateTests</c>
    /// already uses) gated to "area3" (World 2's own "Junction Hall (floor)"). Every assertion reads a
    /// RESOLVED value off the built hierarchy (Rule 2, Tier 2) — never an authored constant, never a
    /// rendered pixel.
    /// </summary>
    public sealed class MV890AreaGateDressingPairingTests
    {
        [Test]
        public void GatedToArea3_DressedCoverBoxStaysHidden_AndArea17DeckStaysLit()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see other World2 EditMode tests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV890 Host");
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);

                // The exact BackyardPath.Awake order for World 2: Build, THEN Dress — both still inside
                // the same Awake, before MapStaticBatchRoot.Start() (and its first ApplyAreaGate call)
                // has ever run. Reproducing this order is the whole point: the bug only exists because
                // Dress's own Renderer.enabled = false lands before the gate's first pass, not after.
                StormdrainDressing.Dress(host.transform, map, built.Cover);

                Transform mapRoot = host.transform.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
                var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
                Assert.IsNotNull(batchRoot, "MapRuntime never attached its own MapStaticBatchRoot");

                InvokePrivate(batchRoot, "Start");

                // Start() gates to whatever AreaAccumulationDirector.PhysicalArea resolves to with none
                // in the scene (area1, MapStaticBatchRoot's own documented fallback) — re-gate explicitly
                // to area3, the zone this test actually probes, the same way a real
                // PlayerCrossedIntoArea signal would once Max walked there.
                batchRoot.ApplyAreaGate("area3");

                // ---- fix 1/2: a dressed cover box in the CURRENT zone must never come back -------------
                CoverPiece dressedPiece = default;
                bool foundDressedPiece = false;
                foreach (CoverPiece piece in built.Cover)
                {
                    if (piece.Body == null || piece.Cover.Dressing == CoverDressing.None) continue;
                    MapZone zone = map.ZoneAt(piece.Body.transform.position.x, piece.Body.transform.position.y,
                        piece.Body.transform.position.z);
                    if (zone == null || zone.id != "area3") continue;
                    foundDressedPiece = true;
                    dressedPiece = piece;
                    break;
                }
                Assert.IsTrue(foundDressedPiece, "setup failure: area3 must carry at least one authored, dressed cover piece");

                Renderer boxRenderer = dressedPiece.Body.GetComponent<Renderer>();
                Assert.IsNotNull(boxRenderer, "setup failure: a cover piece's body must carry its own Renderer");
                Assert.IsFalse(boxRenderer.enabled,
                    $"MV-890: '{dressedPiece.Body.name}' is in area3 (the current/gated zone) and was dressed " +
                    "by StormdrainDressing — its box renderer must stay disabled forever, not be re-enabled " +
                    "just because its own zone is current (this is the 'pipes encased in grey blocks' bug)");

                // Its dressing must be what actually shows in its place — the box going dark is only a fix
                // if a piece of drain machinery is visible where the box used to be, not nothing at all.
                Transform coverDressingRoot = host.transform.Find("Stormdrain Dressing")?.Find("Cover");
                Assert.IsNotNull(coverDressingRoot, "setup failure: StormdrainDressing.Dress must build its own Cover host");
                bool anyDressingVisibleInArea3 = false;
                foreach (Renderer r in coverDressingRoot.GetComponentsInChildren<Renderer>(true))
                {
                    Vector3 p = r.transform.position;
                    MapZone zone = map.ZoneAt(p.x, p.y, p.z);
                    if (zone != null && zone.id == "area3" && r.enabled) { anyDressingVisibleInArea3 = true; break; }
                }
                Assert.IsTrue(anyDressingVisibleInArea3,
                    "MV-890: area3 is the current/gated zone, so at least one of its dressing renderers " +
                    "(the pipe/standpipe/machinery art that replaced its cover boxes) must be visible");

                // ---- fix 3: a17 (the deck overlaying a3) must be lit whenever a3 is current -------------
                Renderer deckRenderer = null;
                foreach (Renderer r in mapRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name != "a17_deck1") continue;
                    deckRenderer = r;
                    break;
                }
                Assert.IsNotNull(deckRenderer, "setup failure: World 2 must build its own 'a17_deck1' deck slab");
                Assert.IsTrue(deckRenderer.enabled,
                    "MV-890: area17 (Junction Hall deck) overlays area3's exact footprint (MV-697) and must " +
                    "stay enabled whenever area3 is current, even though it carries no MapLink of its own to " +
                    "area3 — this is the reported 'a17's upper floor is gone'");
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
    }
}
