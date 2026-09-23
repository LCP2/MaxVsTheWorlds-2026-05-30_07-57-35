using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-919 (Lee, 2026-09-23, live build): on World 2's raised walkway (a15's Trolley Yard deck and
    /// the chain of decks either side of it) "the Sentinels and Max still go across into walls and walk
    /// through walls". The required-first-step measurement (posted to the Jira comment) found the
    /// walkway's own long side rails (N/S, <see cref="MaxWorlds.Arena.MapRuntime.BuildDeckParapetOpenAt"/>'s
    /// un-holed run) already fully solid — the actual gap sat at every area-boundary [DECK] gate join
    /// (E/W): <see cref="MaxWorlds.Arena.MapRuntime"/>'s own <c>TryDeckGateSpan</c> (private, reflection
    /// not needed here — its effect is what this test measures) resolved the doorway hole from
    /// <see cref="MapGeometry.Doorway"/>, which centres/sizes it against the two AREAS' full shared
    /// wall. A deck's own edge is only a narrow 3 m slice of that much taller wall (World 2's decks sit
    /// at one end of a much bigger area rect), so the hole could straddle the deck's own bounds — mostly
    /// past one end (wasted) and short of the doorway's own reach at the other — leaving a stray 0.5 m
    /// sliver of parapet neither the doorway nor the deck's own edge actually needed, instead of one
    /// clean full-width opening. Sentinels route every step through
    /// <see cref="MaxWorlds.Core.CharacterControllerMotion.SafeMove"/> (MV-624), the same swept
    /// <c>CharacterController.Move</c> Max's own <see cref="MaxWorlds.Player.PlayerController"/> uses —
    /// so this one gap explains both bodies in Lee's screenshots, not a second Sentinel-only defect.
    ///
    /// One consolidated EditMode test (testing policy MV-465, Rule 1) covering all three tiers this
    /// ticket's AC asks for: AC2 (resolved parapet-coverage length per edge, Tier 2, numeric — not a
    /// pass/fail alone), AC3 (a driven <c>CharacterController</c> stopped at a genuinely walled edge)
    /// and AC4 (a13/a19/a21 stay unwalled, unaffected). Fails on the base commit (before this fix): the
    /// six E/W gate-join assertions below read a stray 0.5 m covered instead of the expected 0 m — e.g.
    /// a15_deck1's E edge measured covered=0.5 (16.7%) pre-fix vs the expected 0 (0%, doorway width 3 m
    /// consuming the whole 3 m edge) this test asserts.
    /// </summary>
    public sealed class Mv919DeckParapetGateSpanTests
    {
        [Test]
        public void WalkwayParapet_OpensExactlyTheAuthoredGateOrAbutment_NeverAStraySliver()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV919 host").transform;
            try
            {
                MapRuntime.Build(map, host);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                // ---- AC2: resolved parapet-collider coverage per edge, in metres, not just pass/fail ----
                // Long side rails (N/S): every one of these decks is unbroken along its own full length —
                // nothing abuts or gates them, so 100% coverage is the correct, unaffected baseline.
                AssertCoverage(host, "a15_deck1", Wall.N, alongX: true, expected: 30f);
                AssertCoverage(host, "a15_deck1", Wall.S, alongX: true, expected: 30f);
                AssertCoverage(host, "a10_deck1", Wall.N, alongX: true, expected: 26f);
                AssertCoverage(host, "a10_deck1", Wall.S, alongX: true, expected: 26f);
                AssertCoverage(host, "a11_deck1", Wall.N, alongX: true, expected: 26f);
                AssertCoverage(host, "a11_deck1", Wall.S, alongX: true, expected: 26f);
                AssertCoverage(host, "a12_deck1", Wall.N, alongX: true, expected: 24f);
                AssertCoverage(host, "a12_deck1", Wall.S, alongX: true, expected: 24f);
                AssertCoverage(host, "a16_deck1", Wall.N, alongX: true, expected: 106f);
                AssertCoverage(host, "a16_deck1", Wall.S, alongX: true, expected: 106f);
                AssertCoverage(host, "a17_deck1", Wall.N, alongX: true, expected: 3f);
                AssertCoverage(host, "a17_deck1", Wall.W, alongX: false, expected: 30f);
                AssertCoverage(host, "a17_deck2", Wall.N, alongX: true, expected: 3f);
                AssertCoverage(host, "a17_deck2", Wall.S, alongX: true, expected: 3f);
                AssertCoverage(host, "a17_deck3", Wall.E, alongX: false, expected: 3f);
                AssertCoverage(host, "a17_deck3", Wall.W, alongX: false, expected: 3f);
                AssertCoverage(host, "a17_deck4", Wall.N, alongX: true, expected: 8f);
                AssertCoverage(host, "a17_deck4", Wall.S, alongX: true, expected: 8f);

                // Area-boundary [DECK] gate joins (E/W): doorway width (3 m, world2_config.json's own
                // authored "width") equals the deck's own short edge (3 m) at every one of these joins —
                // the fix opens that edge cleanly and completely (0 m covered), not the pre-fix 0.5 m stub.
                AssertCoverage(host, "a15_deck1", Wall.E, alongX: false, expected: 0f);
                AssertCoverage(host, "a15_deck1", Wall.W, alongX: false, expected: 0f);
                AssertCoverage(host, "a10_deck1", Wall.E, alongX: false, expected: 0f);
                AssertCoverage(host, "a10_deck1", Wall.W, alongX: false, expected: 0f);
                AssertCoverage(host, "a11_deck1", Wall.E, alongX: false, expected: 0f);
                AssertCoverage(host, "a11_deck1", Wall.W, alongX: false, expected: 0f);
                AssertCoverage(host, "a12_deck1", Wall.E, alongX: false, expected: 0f);
                AssertCoverage(host, "a12_deck1", Wall.W, alongX: false, expected: 0f);
                AssertCoverage(host, "a16_deck1", Wall.E, alongX: false, expected: 0f);
                AssertCoverage(host, "a16_deck1", Wall.W, alongX: false, expected: 0f);
                AssertCoverage(host, "a17_deck1", Wall.S, alongX: true, expected: 0f); // gate g21, a17->a18
                AssertCoverage(host, "a17_deck4", Wall.E, alongX: false, expected: 0f); // gate g37, a16->a17

                // MV-899's own abutment openings (SAME area, a17's bend) stay exactly as they were —
                // this ticket narrows gate joins only, never touches AbuttingDeckSpans.
                AssertCoverage(host, "a17_deck1", Wall.E, alongX: false, expected: 27f); // 30 - 3m abut
                AssertCoverage(host, "a17_deck2", Wall.E, alongX: false, expected: 27f);
                AssertCoverage(host, "a17_deck2", Wall.W, alongX: false, expected: 27f);
                AssertCoverage(host, "a17_deck3", Wall.N, alongX: true, expected: 14f); // 20 - two 3m abuts
                AssertCoverage(host, "a17_deck3", Wall.S, alongX: true, expected: 14f);
                AssertCoverage(host, "a17_deck4", Wall.W, alongX: false, expected: 0f); // fully abutted by a17_deck2

                // ---- AC3: a driven CharacterController is actually stopped at a genuinely walled edge ----
                // a15_deck1's S rail, well clear of either E/W gate join (deck spans x 272..302; probed
                // at x=287, the deck's own centre) — must block a controller driven straight into it.
                AssertControllerBlocked(host, map, "a15_deck1", new Vector3(287f, 0f, 118.4f), Vector3.back);
                // a17_deck1's W rail (its own genuinely outer edge, MV-899's own reference point).
                AssertControllerBlocked(host, map, "a17_deck1", new Vector3(62.4f, 0f, 111f), Vector3.left);

                // ---- AC4: a13/a19/a21 stay unwalled — no parapet collider built on any of them ----
                AssertNoParapet(host, "a13_deck1");
                AssertNoParapet(host, "a19_deck1");
                AssertNoParapet(host, "a21_deck1");
                AssertNoParapet(host, "a21_deck2");
                AssertNoParapet(host, "a21_deck3");
                AssertNoParapet(host, "a21_deck4");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        /// <summary>Sums the along-the-wall extent of every built parapet blocker collider on
        /// <paramref name="wall"/>, the resolved (Tier 2) ground truth of how much of that edge is
        /// actually solid — not an authored constant, not a rendered pixel.</summary>
        private static void AssertCoverage(Transform host, string deckId, Wall wall, bool alongX, float expected)
        {
            string prefix = $"{deckId}_parapet_{wall}_";
            var colliders = host.GetComponentsInChildren<Collider>(true)
                .Where(c => c.gameObject.name.StartsWith(prefix) && c.gameObject.name.EndsWith("_collider"))
                .ToList();

            float covered = colliders.Sum(c => alongX ? c.bounds.size.x : c.bounds.size.z);

            Assert.AreEqual(expected, covered, 0.1f,
                $"MV-919: {deckId}'s {wall} edge should have {expected:0.#} m of parapet collider, " +
                $"measured {covered:0.#} m from {colliders.Count} built collider(s)");
        }

        private static void AssertNoParapet(Transform host, string deckId)
        {
            bool any = host.GetComponentsInChildren<Collider>(true)
                .Any(c => c.gameObject.name.StartsWith($"{deckId}_parapet_"));
            Assert.IsFalse(any, $"MV-919: {deckId} is authored unwalled — it must build no parapet collider at all");
        }

        /// <summary>Drives a fresh <see cref="CharacterController"/> straight at a wall from just inside
        /// <paramref name="deckId"/>'s own footprint and asserts it travels nowhere near the full
        /// attempted distance — the same swept <c>Move</c> both Max and every Sentinel actually use.</summary>
        private static void AssertControllerBlocked(Transform host, MapData map, string deckId, Vector3 startXz, Vector3 direction)
        {
            DeckSlab slab = MapGeometry.Decks(map).First(d => d.Id == deckId);

            var go = new GameObject("MV919 probe", typeof(CharacterController));
            go.transform.SetParent(host, worldPositionStays: true);
            go.transform.position = new Vector3(startXz.x, slab.TopY, startXz.z); // standing on the deck surface
            var cc = go.GetComponent<CharacterController>();
            cc.radius = 0.3f;
            cc.height = 1.8f;
            cc.center = new Vector3(0f, 0.9f, 0f); // capsule foot at the deck surface, overlapping the 1 m parapet band above it
            cc.skinWidth = 0.02f;
            cc.minMoveDistance = 0f;

            Vector3 before = go.transform.position;
            const float attemptedTotal = 4f;
            for (int i = 0; i < 20; i++) cc.Move(direction.normalized * 0.2f);
            float travelled = Vector3.Distance(before, go.transform.position);

            Object.DestroyImmediate(go);

            Assert.Less(travelled, attemptedTotal * 0.5f,
                $"MV-919: a CharacterController driven {attemptedTotal} m into {deckId}'s wall from " +
                $"{before} travelled {travelled:0.##} m — it must be stopped by the parapet collider, " +
                "not pass through it");
        }
    }
}
