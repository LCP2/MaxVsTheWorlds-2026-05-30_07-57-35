using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-899 (Lee, 2026-09-22, live build): where a raised World 2 walkway bends — a17's two long N/S
    /// decks (a17_deck1 x 62-65, a17_deck2 x 79-82, both z 96-126) joined partway up by the E/W cross
    /// deck a17_deck3 (x 62-82, z 110-113) — Max cannot walk from the N/S run onto the cross deck. The
    /// ticket's own hypothesis blamed <c>MapRuntime.BuildDeckEdgeBeam</c>; the required first step (see
    /// the Jira comment) found that's wrong — that beam's collider is always stripped
    /// (<see cref="MaxWorlds.Rendering.StormdrainKit.Box"/> calls <c>Strip</c>), walled deck or not. The
    /// real blocker is <c>BuildDeckParapet</c>'s own invisible, non-stripped collider box (MV-852):
    /// a17's decks are all authored <c>"walled": true</c>, and MV-852 walls every edge blind to a second
    /// deck occupying the far side of that same edge line — the same way MV-859 already found it blind
    /// to a gate's own doorway.
    ///
    /// One consolidated EditMode test (testing policy MV-465, Rule 1), asserting RESOLVED values only
    /// (Tier 2 — built collider bounds, never an authored constant or a rendered pixel): at a point on
    /// the shared a17_deck1/a17_deck3 edge line (world x=65, inside the z 110-113 span a17_deck3 itself
    /// covers), no built collider may block it; at a point on a17_deck1's own genuinely outer west edge
    /// (x=62 — no other a17 deck ever reaches that line), a collider must still block it exactly as
    /// before. Fails on 1ffb857 (the commit before this fix): the shared-edge point is blocked by
    /// a17_deck1's own parapet, built the full length of its east wall with no gap for a17_deck3.
    /// </summary>
    public sealed class Mv899DeckCornerAbutmentTests
    {
        [Test]
        public void DeckEdgeAbuttingAnotherDeck_OpensWhileGenuineOuterEdgeStaysWalled()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV899 host").transform;
            try
            {
                MapRuntime.Build(map, host);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                var deckBodyNames = new[] { "a17_deck1", "a17_deck2", "a17_deck3", "a17_deck4" };
                var colliders = host.GetComponentsInChildren<Collider>(true)
                    .Where(c => !deckBodyNames.Contains(c.gameObject.name))
                    .ToList();
                Assert.IsNotEmpty(colliders, "setup failure: a17 built no edge colliders at all");

                // The shared edge: a17_deck1's own east wall (x=65) over the exact z-range a17_deck3
                // (x 62-82, z 110-113) covers — Max must be free to cross here.
                var sharedEdgePoint = new Vector3(65f, 3.0f, 111.5f);
                bool sharedEdgeBlocked = colliders.Any(c => c.bounds.Contains(sharedEdgePoint));
                Assert.IsFalse(sharedEdgeBlocked,
                    $"MV-899: a17_deck1's east edge at {sharedEdgePoint} sits on the shared join with " +
                    "a17_deck3 (both decks at height 2.5) — Max must be able to cross it, but a built " +
                    "collider still blocks this point");

                // A genuinely outer edge on the SAME deck (west wall, x=62 — nothing else in a17 ever
                // reaches this line) must stay exactly as walled as before.
                var outerEdgePoint = new Vector3(62f, 3.0f, 111.5f);
                bool outerEdgeBlocked = colliders.Any(c => c.bounds.Contains(outerEdgePoint));
                Assert.IsTrue(outerEdgeBlocked,
                    $"MV-899: a17_deck1's genuinely outer west edge at {outerEdgePoint} must stay walled " +
                    "— no other deck ever reaches x=62 in a17");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }
    }
}
