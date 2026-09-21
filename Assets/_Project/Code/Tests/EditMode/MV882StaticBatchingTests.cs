using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-882: <c>MapRuntime</c>/<c>StormdrainKit</c> built every area's floor, walls, cover and deck
    /// geometry without ever calling <see cref="StaticBatchingUtility"/>.Combine, unlike World 1's
    /// hand-authored dressing layers (<c>BackyardBackdrop</c>/<c>BackyardDressing</c>/
    /// <c>BackyardHomeShed</c>/<c>BackyardEntryDoor</c>, which all do) — every generated cube submitted
    /// its own draw call every frame. Fails on the base commit named in the PR: <c>MapRuntime.Build</c>
    /// never adds a <see cref="MapStaticBatchRoot"/> to the map root at all, so this test's very first
    /// lookup for one returns null.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting a RESOLVED value only (Rule 2,
    /// Tier 2): membership in <see cref="MapStaticBatchRoot.Statics"/> — the actual array
    /// <c>MapRuntime.Build</c> hands to <c>StaticBatchingUtility.Combine</c> — never an authored
    /// constant. Every static kind (floor, wall, cover) must be IN it; every kind that moves or
    /// repaints after build (sludge, ramp, deck slab, area gate/hatch, grate) must be OUT of it. Run
    /// against World 2's own real shipped config, which authors every one of those kinds already, so
    /// the fixture needs no hand-built <c>MapData</c>.
    /// </summary>
    public sealed class MV882StaticBatchingTests
    {
        [Test]
        public void MapRuntimeBuild_PutsOnlyNonMovingGeometryInTheStaticBatch()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV882 host").transform;
            try
            {
                MapBuild build = MapRuntime.Build(map, host);

                Transform mapRoot = host.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");

                var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
                Assert.IsNotNull(batchRoot, "MapRuntime never registered a MapStaticBatchRoot on its map root");

                var statics = new HashSet<GameObject>(batchRoot.Statics ?? System.Array.Empty<GameObject>());
                Assert.IsNotEmpty(statics, "the static batch is empty -- this test proves nothing");

                // ---- static kinds: must be IN the batch ----
                Transform mapFloor = mapRoot.Find("Map Floor");
                Assert.IsNotNull(mapFloor, "MapRuntime never built its own Map Floor");
                Assert.IsTrue(statics.Contains(mapFloor.gameObject), "Map Floor must be in the static batch");

                StructuralWall[] walls = mapRoot.GetComponentsInChildren<StructuralWall>(true);
                Assert.IsNotEmpty(walls, "World 2 must author at least one wall for this test to mean anything");
                foreach (StructuralWall wall in walls)
                    Assert.IsTrue(statics.Contains(wall.gameObject), $"{wall.name} (wall) must be in the static batch");

                Assert.IsNotEmpty(build.Cover, "World 2 must author at least one cover piece for this test to mean anything");
                foreach (CoverPiece piece in build.Cover)
                    Assert.IsTrue(statics.Contains(piece.Body), $"{piece.Cover.Name} (cover) must be in the static batch");

                // ---- moving/repainted kinds: must be OUT of the batch ----
                SludgeFlow[] sludge = mapRoot.GetComponentsInChildren<SludgeFlow>(true);
                Assert.IsNotEmpty(sludge, "World 2 must author at least one sludge tile for this test to mean anything");
                foreach (SludgeFlow s in sludge)
                    Assert.IsFalse(statics.Contains(s.gameObject), $"{s.name} (sludge, flows) must not be in the static batch");

                DeckVisibility[] decks = mapRoot.GetComponentsInChildren<DeckVisibility>(true);
                Assert.IsNotEmpty(decks, "World 2 must author at least one deck for this test to mean anything");
                foreach (DeckVisibility d in decks)
                    Assert.IsFalse(statics.Contains(d.gameObject),
                        $"{d.name} (deck slab, DeckVisibility MaterialPropertyBlock-tints it) must not be in the static batch");

                // Covers both a wall-standing area gate and a deck-standing hatch (BuildHatch adds an
                // AreaGate too) in one sweep -- both hinge-swing open.
                AreaGate[] gates = mapRoot.GetComponentsInChildren<AreaGate>(true);
                Assert.IsNotEmpty(gates, "World 2 must author at least one area gate/hatch for this test to mean anything");
                foreach (AreaGate g in gates)
                    Assert.IsFalse(statics.Contains(g.gameObject), $"{g.name} (area gate/hatch, opens) must not be in the static batch");

                GrateShudder[] grates = mapRoot.GetComponentsInChildren<GrateShudder>(true);
                Assert.IsNotEmpty(grates, "World 2 must author at least one grate for this test to mean anything");
                foreach (GrateShudder grate in grates)
                    foreach (Renderer r in grate.GetComponentsInChildren<Renderer>(true))
                        Assert.IsFalse(statics.Contains(r.gameObject),
                            $"{r.name} (grate bar, GrateShudder moves it) must not be in the static batch");

                List<RampSlab> ramps = MapGeometry.Ramps(map).ToList();
                Assert.IsNotEmpty(ramps, "World 2 must author at least one ramp for this test to mean anything");
                Transform[] allChildren = mapRoot.GetComponentsInChildren<Transform>(true);
                foreach (RampSlab ramp in ramps)
                {
                    Transform rampGo = allChildren.FirstOrDefault(t => t.name == ramp.Id);
                    Assert.IsNotNull(rampGo, $"{ramp.Id}: ramp body was never built");
                    Assert.IsFalse(statics.Contains(rampGo.gameObject), $"{ramp.Id} (ramp) must not be in the static batch");
                }
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }
    }
}
