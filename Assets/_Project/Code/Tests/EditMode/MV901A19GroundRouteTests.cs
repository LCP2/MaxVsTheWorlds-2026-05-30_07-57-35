using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-901 — a19 (Pump Hall) lost its upper level in Lee's V8/V10 workbook (MV-900 applies the
    /// config change; this ticket only proves it). AC1/AC2 are plain config-content facts, checked
    /// directly below. The one new test this ticket is allowed (testing policy MV-465, Rule 1) proves
    /// the thing those two facts alone don't: that <see cref="a19"/>'s ground-level neighbours still
    /// chain all the way to <c>a21</c> (The Wet Well, the boss area) once a19 carries no deck to route
    /// through.
    ///
    /// Fails on 9ec8548 (immediately before MV-900 merged, world2_config.json revision "WORLD 2 v3
    /// 2026-09-20"): a19 there still authors <c>a19_deck1</c> (x 0, z 19, w 34, d 3, height 2.5) and
    /// two ramps (<c>a19_ramp1</c>, <c>a19_ramp2</c>) — see the fix comment for the quoted failure.
    /// Passes on the shipped V10 config, where a19's <c>decks</c>/<c>ramps</c> arrays are empty.
    ///
    /// The walk starts at a19 itself, not World 2's global entry ("stub"), on purpose: World 2's own
    /// gate graph (unchanged by this ticket) forces the earlier a10-a18 stretch through a walled DECK
    /// route (every gate between a10/a11/a12/a14/a15/a16/a17/a18 carries the "[DECK]" <c>opensWith</c>
    /// suffix — MV-852's intentional "walled upper route" design) — a literal floor-links-only walk
    /// from World 2's true entry can never reach a18, let alone a19, regardless of this ticket. What
    /// Lee's requirement (and this ticket's own scope) actually needs proven is narrower: once the
    /// player has come down off that raised route and landed in a19 (through g22, already floor-level
    /// on both old and new config), the REST of the world — a19 -&gt; a20 -&gt; a21 — opens using
    /// floor-level links only. That is exactly what this test walks, on the real built
    /// <see cref="MapData"/> link graph (not the authored <see cref="WorldConfig.gates"/> list), reading
    /// each link's level off its built gate <see cref="MapEntity"/> the same way
    /// <see cref="MapRuntime.BuildAreaGate"/> itself does.
    /// </summary>
    public sealed class MV901A19GroundRouteTests
    {
        [Test]
        public void A19HasNoDeck_AndGroundRouteReachesA21UsingFloorLevelLinksOnly()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");

            WorldArea a19 = cfg.Area("a19");
            Assert.IsNotNull(a19, "MV-901: world2_config.json must still author an 'a19' area");
            Assert.IsTrue(a19.decks == null || a19.decks.Length == 0,
                $"MV-901: a19's upper level was removed (Lee's V8 workbook) — expected 0 decks, got {a19.decks?.Length ?? 0}");
            Assert.IsTrue(a19.ramps == null || a19.ramps.Length == 0,
                $"MV-901: a19's upper level was removed (Lee's V8 workbook) — expected 0 ramps, got {a19.ramps?.Length ?? 0}");

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            // Floor-level-only adjacency over the BUILT link graph: a link counts only when its own
            // gate entity's level is 0 — MapEntity.level is 1 exactly when the authored opensWith
            // carried WorldMapLoader.DeckGateSuffix ("[DECK]"), the same flag MapRuntime.BuildAreaGate
            // reads to build a gate at deck height instead of the floor.
            var adjacency = new Dictionary<string, List<string>>();
            foreach (MapLink link in map.links)
            {
                if (link?.from == null || link.to == null) continue;
                MapEntity gate = map.Entity(link.gate);
                if (gate == null || gate.level > 0) continue;

                if (!adjacency.TryGetValue(link.from, out List<string> a)) adjacency[link.from] = a = new List<string>();
                a.Add(link.to);
                if (!adjacency.TryGetValue(link.to, out List<string> b)) adjacency[link.to] = b = new List<string>();
                b.Add(link.from);
            }

            const string a19Zone = "area19";
            const string a21Zone = "area21";

            var visited = new HashSet<string> { a19Zone };
            var queue = new Queue<string>();
            queue.Enqueue(a19Zone);
            while (queue.Count > 0)
            {
                string cur = queue.Dequeue();
                if (!adjacency.TryGetValue(cur, out List<string> neighbours)) continue;
                foreach (string n in neighbours)
                {
                    if (!visited.Add(n)) continue;
                    queue.Enqueue(n);
                }
            }

            Assert.IsTrue(visited.Contains(a21Zone),
                "MV-901: a21 (The Wet Well, the boss area) must be reachable from a19 using floor-level " +
                $"links only now that a19's deck is gone — floor-reachable set from a19: {string.Join(",", visited.OrderBy(z => z))}");
        }
    }
}
