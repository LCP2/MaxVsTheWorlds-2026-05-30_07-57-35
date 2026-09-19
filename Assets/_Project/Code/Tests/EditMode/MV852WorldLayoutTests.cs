using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-852: World 2 used to let you reach the upper level (and the boss route beyond it) without
    /// ever being forced past the Replicators the design calls "must destroy" — the old gate graph let
    /// Max slip from a11 straight up into a13/a14/a17-19 via ramps and DECK gates that never checked
    /// whether a single Replicator had died. This ticket forces a ground-only path from the entry stub
    /// through every Replicator area, gates the only door up (into a12) on all of them being destroyed,
    /// and rebuilds the upper level as a single deck chain with exactly two ramps.
    ///
    /// Fails on base commit 24aceb3 (before this ticket): that config still authors g12 (a11-&gt;a12,
    /// gated on a subset of Replicators, not the ground-route's actual "a2,a3,a5,a8,a9,a10,a11,a18,a17,
    /// a16,a6" list this ticket adds as g31) and no DECK-tagged chain running a12-&gt;a15-&gt;a16-&gt;a17-&gt;
    /// a18-&gt;a14-&gt;a19-&gt;a20 exists at all; ramps are authored inside a3/a6/a11/a16/a17/a18 (this
    /// ticket's own AC1c list), so the "only a12_ramp1/a20_ramp1" assertion below fails immediately.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1) over the real, shipped World 2 config, a
    /// pure gate-graph BFS matching <see cref="MapValidation"/>'s own <c>WorldReachability</c> shape
    /// (Rule 2, Tier 2 — resolved graph membership, not an authored constant): (a) ground-only
    /// reachability (ignoring every <c>[DECK]</c> gate) from <c>stub</c> is exactly the forced route,
    /// with a12 added only once the Replicator door is open, and a20 never ground-reachable; (b) every
    /// area the door's own condition names is inside that ground set; (c) the <c>[DECK]</c>-tagged chain
    /// from a12 visits exactly a15/a16/a17/a18/a14/a19/a20 in that order, the only ramps across every
    /// named area are a12_ramp1 and a20_ramp1, and a23 is reachable from stub over every gate.
    /// </summary>
    public sealed class MV852WorldLayoutTests
    {
        private static readonly string[] GroundRouteReplicatorAreas =
        {
            "a2", "a3", "a5", "a8", "a9", "a10", "a11", "a18", "a17", "a16", "a6",
        };

        [Test]
        public void GroundRouteForcesReplicatorDoor_AndUpperDeckChainRunsAboveIt()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");

            // === AC1a: ground-only reachability (ignoring [DECK] gates), Replicator door closed ===
            var expectedClosed = new HashSet<string>
            {
                "stub", "a1", "a2", "a3", "a5", "a4", "a8", "a9", "a10", "a11", "a18", "a17", "a16", "a6",
            };
            HashSet<string> groundClosed = GroundReachable(cfg, doorOpen: false);
            CollectionAssert.AreEquivalent(expectedClosed, groundClosed,
                "MV-852 AC1a: ground-only reachability with the Replicator door closed must be exactly the forced route");
            Assert.IsFalse(groundClosed.Contains("a20"), "MV-852 AC1a: a20 must never be ground-reachable from a3");
            Assert.IsFalse(groundClosed.Contains("a12"), "MV-852 AC1a: a12 must not be ground-reachable while the door is closed");

            var expectedOpen = new HashSet<string>(expectedClosed) { "a12" };
            HashSet<string> groundOpen = GroundReachable(cfg, doorOpen: true);
            CollectionAssert.AreEquivalent(expectedOpen, groundOpen,
                "MV-852 AC1a: opening the Replicator door must add exactly a12 to ground reachability");

            // === AC1b: every Replicator area the door's own condition names is ground-reachable ===
            WorldGate door = cfg.gates.FirstOrDefault(g => IsReplicatorDoor(g));
            Assert.IsNotNull(door, "setup failure: no gate into a12 gated on replicators-destroyed was found");
            string[] doorAreas = door.opensWith.Substring("replicators-destroyed:".Length).Split(',');
            CollectionAssert.AreEquivalent(GroundRouteReplicatorAreas, doorAreas,
                "MV-852 AC1b: the door's condition must name exactly every ground-route Replicator area");
            foreach (string areaId in doorAreas)
                Assert.IsTrue(groundClosed.Contains(areaId), $"MV-852 AC1b: '{areaId}' named in the door condition must be ground-reachable");

            // === AC1c: the [DECK] chain from a12 visits a15/a16/a17/a18/a14/a19/a20, in that order ===
            var expectedDeckOrder = new[] { "a12", "a15", "a16", "a17", "a18", "a14", "a19", "a20" };
            List<string> deckOrder = DeckChainOrder(cfg, "a12");
            Assert.AreEqual(expectedDeckOrder, deckOrder.ToArray(),
                "MV-852 AC1c: the [DECK]-gated chain from a12 must visit exactly this sequence, in order");

            // Only a12_ramp1 and a20_ramp1 exist among the named areas.
            string[] rampScopeAreas = { "a3", "a6", "a11", "a12", "a14", "a15", "a16", "a17", "a18", "a19", "a20" };
            var allRamps = new List<string>();
            foreach (string areaId in rampScopeAreas)
            {
                WorldArea area = cfg.Area(areaId);
                Assert.IsNotNull(area, $"setup failure: area '{areaId}' not found");
                foreach (WorldRamp r in area.ramps ?? Array.Empty<WorldRamp>()) allRamps.Add(r.id);
            }
            CollectionAssert.AreEquivalent(new[] { "a12_ramp1", "a20_ramp1" }, allRamps,
                "MV-852 AC1c: the only ramps across a3/a6/a11/a12/a14/a15/a16/a17/a18/a19/a20 must be a12_ramp1 and a20_ramp1");

            // a23 is reachable from stub over ALL gates (ground + deck).
            HashSet<string> everything = AllReachable(cfg);
            Assert.IsTrue(everything.Contains("a23"), "MV-852 AC1c: a23 must be reachable from stub over all gates");

            // === AC2: MapValidation (including WorldReachability) must pass the shipped config ===
            Assert.IsTrue(MapValidation.ValidateWorldConfig(cfg, out string reason), reason);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData _, out string loadReason), loadReason);
        }

        private static bool IsReplicatorDoor(WorldGate g) =>
            g?.opensWith != null && g.opensWith.StartsWith("replicators-destroyed:") &&
            (g.from.area == "a12" || g.to.area == "a12") && (g.from.area == "a6" || g.to.area == "a6");

        private static bool IsDeckGate(WorldGate g) => g?.opensWith != null && g.opensWith.Contains("[DECK]");

        /// <summary>The same bidirectional gate-graph BFS <see cref="MapValidation"/>'s own
        /// <c>WorldReachability</c> runs, restricted to non-<c>[DECK]</c> gates, with the Replicator
        /// door into a12 included only when <paramref name="doorOpen"/> is true.</summary>
        private static HashSet<string> GroundReachable(WorldConfig cfg, bool doorOpen)
        {
            var reached = new HashSet<string> { "stub" };
            var queue = new Queue<string>();
            queue.Enqueue("stub");
            while (queue.Count > 0)
            {
                string here = queue.Dequeue();
                foreach (WorldGate g in cfg.gates)
                {
                    if (g?.from == null || g.to == null || IsDeckGate(g)) continue;
                    if (IsReplicatorDoor(g) && !doorOpen) continue;
                    string next = g.from.area == here ? g.to.area : g.to.area == here ? g.from.area : null;
                    if (next != null && reached.Add(next)) queue.Enqueue(next);
                }
            }
            return reached;
        }

        private static HashSet<string> AllReachable(WorldConfig cfg)
        {
            var reached = new HashSet<string> { "stub" };
            var queue = new Queue<string>();
            queue.Enqueue("stub");
            while (queue.Count > 0)
            {
                string here = queue.Dequeue();
                foreach (WorldGate g in cfg.gates)
                {
                    if (g?.from == null || g.to == null) continue;
                    string next = g.from.area == here ? g.to.area : g.to.area == here ? g.from.area : null;
                    if (next != null && reached.Add(next)) queue.Enqueue(next);
                }
            }
            return reached;
        }

        /// <summary>Walks the single chain of <c>[DECK]</c>-tagged gates starting from
        /// <paramref name="startArea"/> — the upper route is a simple path, not a branching graph, so
        /// "whichever unvisited neighbour the next [DECK] gate reaches" fully determines visit order.</summary>
        private static List<string> DeckChainOrder(WorldConfig cfg, string startArea)
        {
            var order = new List<string> { startArea };
            var visited = new HashSet<string> { startArea };
            string current = startArea;
            while (true)
            {
                WorldGate next = cfg.gates.FirstOrDefault(g => IsDeckGate(g) &&
                    ((g.from.area == current && !visited.Contains(g.to.area)) ||
                     (g.to.area == current && !visited.Contains(g.from.area))));
                if (next == null) break;
                string nextArea = next.from.area == current ? next.to.area : next.from.area;
                order.Add(nextArea);
                visited.Add(nextArea);
                current = nextArea;
            }
            return order;
        }
    }
}
