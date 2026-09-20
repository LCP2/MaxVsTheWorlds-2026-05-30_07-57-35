using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-859: Lee, on the live build, opened deck gate g32 (a12's west wall &lt;-&gt; a15's east wall,
    /// z ~118.5) from the Replicator Nest's upper walkway into the Trolley Yard's walkway, but could not
    /// walk through it. Reading the code found two bugs: (1) <see cref="MapRuntime"/>'s
    /// <c>BuildDeckParapet</c> (MV-852) builds a solid 1 m parapet across every deck edge
    /// <c>DeckMouthWalls</c> doesn't already know is a mouth, and <c>DeckMouthWalls</c> never looked at
    /// gates, so a parapet stood squarely across g32's own opening on both a12_deck1's west edge and
    /// a15_deck1's east edge; (2) <see cref="MapGeometry.Walls"/> cuts every gate's doorway hole through
    /// the FULL wall height regardless of the gate's level, so the ordinary floor-level wall between a12
    /// and a6/a15 has a 3 m gap at floor level under g32 too — Max could walk into the Nest at ground
    /// level without ever destroying the Replicators that gate the real door (g31), bypassing it.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values only (Tier 2):
    /// (a) a CharacterController-sized probe on a12's deck walks west through g32's own opening onto
    /// a15's deck; (b) a second probe on the FLOOR directly below g32 cannot walk west into a15/a6 — the
    /// wall now stays solid below deck height. Fails on dd85276 (the commit before this fix): probe (a)
    /// stalls against the parapet MV-852 built across the gate's own mouth, and probe (b) sails straight
    /// through the floor-level gap toward the Nest.
    /// </summary>
    public sealed class MV859DeckGateOpeningTests
    {
        [Test]
        public void DeckGateOpensThroughItsOwnParapet_AndFloorBelowItStaysClosed()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject("MV859 World2 Root").transform;
            GameObject deckProbeGo = null;
            GameObject floorProbeGo = null;
            try
            {
                MapBuild built = MapRuntime.Build(map, root);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                Assert.IsTrue(built.Actors.TryGetValue("g32", out GameObject gate32) && gate32 != null,
                    "setup failure: deck gate g32 was never built");
                // "with g32 open" (AC1) — isolate the parapet/wall-sill fix under test from the separate
                // hinge-swing animation a real break plays, which this ticket does not touch.
                gate32.SetActive(false);
                Physics.SyncTransforms();

                // ---- (a) the deck-level walk: a12's deck -> through g32's centre -> a15's deck -------
                deckProbeGo = new GameObject("MV859 Deck Probe", typeof(CharacterController));
                var deckCc = deckProbeGo.GetComponent<CharacterController>();
                deckCc.radius = 0.5f;
                deckCc.height = 1.6f;
                deckCc.center = Vector3.up * 0.8f;
                deckProbeGo.transform.position = new Vector3(310f, 2.6f, 118.5f); // a12_deck1's own top (2.5) + a hair
                Physics.SyncTransforms();

                Vector3 deckDest = new Vector3(295f, 2.6f, 118.5f); // well onto a15_deck1 (x 272-302)
                for (int i = 0; i < 400 && deckProbeGo.transform.position.x > deckDest.x + 0.05f; i++)
                {
                    Vector3 to = deckDest - deckProbeGo.transform.position; to.y = 0f;
                    deckCc.Move(to.normalized * Mathf.Min(0.05f, to.magnitude));
                }

                Assert.Less(deckProbeGo.transform.position.x, 301f,
                    $"MV-859 AC1: a CharacterController on a12's deck walking west through g32's centre " +
                    $"must reach a15's deck (x<301); it stalled at {deckProbeGo.transform.position}");

                // ---- (b) the floor-level gap under the same gate must now be closed -------------------
                floorProbeGo = new GameObject("MV859 Floor Probe", typeof(CharacterController));
                var floorCc = floorProbeGo.GetComponent<CharacterController>();
                floorCc.radius = 0.5f;
                floorCc.height = 1.6f;
                floorCc.center = Vector3.up * 0.8f;
                floorProbeGo.transform.position = new Vector3(303f, 0.1f, 118.5f);
                Physics.SyncTransforms();

                Vector3 floorDest = new Vector3(295f, 0.1f, 118.5f);
                for (int i = 0; i < 400 && floorProbeGo.transform.position.x > floorDest.x + 0.05f; i++)
                {
                    Vector3 to = floorDest - floorProbeGo.transform.position; to.y = 0f;
                    floorCc.Move(to.normalized * Mathf.Min(0.05f, to.magnitude));
                }

                Assert.GreaterOrEqual(floorProbeGo.transform.position.x, 302f,
                    $"MV-859 AC1: a CharacterController on the FLOOR at (303,0,118.5) walking west must be " +
                    $"stopped at x>=302 (the floor-level gap under a [DECK] gate must stay closed); it " +
                    $"reached {floorProbeGo.transform.position}");
            }
            finally
            {
                if (deckProbeGo != null) Object.DestroyImmediate(deckProbeGo);
                if (floorProbeGo != null) Object.DestroyImmediate(floorProbeGo);
                Object.DestroyImmediate(root.gameObject);
            }
        }
    }
}
