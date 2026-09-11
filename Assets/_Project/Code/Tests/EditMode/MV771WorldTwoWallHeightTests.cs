using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-771: every wall in every world was 1.5 m — at the play camera you see clean over all of them,
    /// so no room has an interior and nothing is ever concealed (Lee: "still feels like a series of
    /// areas"). World 2's own cover was already authored at 1.6 m, taller than the wall beside it
    /// (<c>MapRuntime.cs</c> carries a comment acknowledging it) — the supporting measurement for why
    /// this ticket raises ONLY World 2's <c>wallHeight</c> to 3.0 m, leaving World 1 and World 3
    /// untouched.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1) asserting RESOLVED state (Tier 2): the
    /// loaded <see cref="MapData.wallHeight"/> per world, every built <see cref="StructuralWall"/>'s
    /// actual renderer bounds, every built area gate's actual renderer height, and — because raising the
    /// wall crosses <see cref="StormdrainKit"/>'s own 2.2 m richness threshold (MV-765) — that the
    /// Stormdrain dressing pass actually builds the richer pieces that threshold unlocks, not just that
    /// the number moved.
    /// </summary>
    public sealed class MV771WorldTwoWallHeightTests
    {
        [Test]
        public void WorldTwoWallHeight_RaisedTo3m_WorldsOneAndThreeUnchanged()
        {
            WorldConfig w1cfg = WorldLibrary.Load(WorldLibrary.World1);
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            WorldConfig w3cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(w1cfg, "World 1's own shipped config must load for this test to mean anything");
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsNotNull(w3cfg, "World 3's own shipped config must load for this test to mean anything");

            Assert.IsTrue(WorldMapLoader.TryLoad(w1cfg, out MapData w1map, out string w1reason), w1reason);
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData w2map, out string w2reason), w2reason);
            Assert.IsTrue(WorldMapLoader.TryLoad(w3cfg, out MapData w3map, out string w3reason), w3reason);

            Assert.AreEqual(3.0f, w2map.wallHeight, 0.001f, "World 2's own experiment: wallHeight 1.5 -> 3.0");
            Assert.AreEqual(1.5f, w1map.wallHeight, 0.001f, "World 1's wallHeight must not move for this ticket");
            Assert.AreEqual(1.5f, w3map.wallHeight, 0.001f, "World 3's wallHeight must not move for this ticket");

            var host = new GameObject("MV771 host").transform;
            try
            {
                MapBuild build = MapRuntime.Build(w2map, host);

                // ---- every built structural wall stands (at least) as tall as the raised wallHeight ----
                StructuralWall[] walls = host.GetComponentsInChildren<StructuralWall>(true);
                Assert.IsNotEmpty(walls, "World 2 must build at least one structural wall for this test to mean anything");
                foreach (StructuralWall w in walls)
                {
                    var rend = w.GetComponent<Renderer>();
                    Assert.IsNotNull(rend, $"{w.name} carries no renderer to resolve bounds from");
                    Assert.GreaterOrEqual(rend.bounds.size.y, 2.9f,
                        $"{w.name} resolves to {rend.bounds.size.y:F2} m tall — every World 2 wall must stand at least 2.9 m now");
                }

                // ---- every WALL area gate's resolved height matches the map's wallHeight exactly, so
                // a gate can never leave a gap above it. MapEntity.Kind, not the AreaGate component,
                // is what tells a wall gate apart from a deck hatch — WorldMapLoader/BuildHatch also
                // hangs an AreaGate on every hatch, but a hatch's "height" is its resolved deck Y, not
                // a wall height, and must not be checked against wallHeight here.
                MapEntity[] wallGates = w2map.entities
                    .Where(e => e != null && e.Kind == EntityKind.AreaGate).ToArray();
                Assert.IsNotEmpty(wallGates, "World 2 must author at least one wall gate for this test to mean anything");
                foreach (MapEntity e in wallGates)
                {
                    Assert.IsTrue(build.Actors.TryGetValue(e.id, out GameObject body), $"gate {e.id} was never built");
                    var rend = body.GetComponent<Renderer>();
                    Assert.IsNotNull(rend, $"{e.id} carries no renderer to resolve bounds from");
                    Assert.AreEqual(w2map.wallHeight, rend.bounds.size.y, 0.01f,
                        $"gate {e.id} resolves to {rend.bounds.size.y:F2} m — it must match the map's wallHeight exactly");
                }

                // ---- crossing the 2.2 m threshold (MV-765) restores the full soffit and the high pipe
                // run the Stormdrain kit deliberately suppressed at 1.5 m ----
                StormdrainDressing.Dress(host, w2map, build.Cover);
                Transform dressingHost = host.Find("Stormdrain Dressing");
                Assert.IsNotNull(dressingHost, "the dressing host was never built");

                Renderer[] allRenderers = dressingHost.GetComponentsInChildren<Renderer>(true);
                Assert.IsTrue(allRenderers.Any(r => r.name == "Soffit"),
                    "a 3.0 m wall must build at least one 'Soffit' piece");
                Assert.IsTrue(allRenderers.Any(r => r.name == "Pipe High"),
                    "a 3.0 m wall crosses the 2.2 m threshold and must build at least one 'Pipe High' run");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }
    }
}
