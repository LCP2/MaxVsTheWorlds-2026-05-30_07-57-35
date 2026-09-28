using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1002 — Lee (TestFlight, World 2): dying on a DECK (a15 "Trolley Yard", overlaying a13) and
    /// hitting CONTINUE left every area reached afterward (a18, a19, a20) with no robots at all. Root
    /// cause: <see cref="RespawnPlanner.Resolve(int, bool)"/> used to fall back to <c>deathArea - 1</c> —
    /// raw index arithmetic that assumes a world's area INDEX order matches its PLAY order. World 2's
    /// gantry decks break that assumption (a15 overlays the much-earlier a13), so a death on a15
    /// respawned "into" a14 (Replicator Nest) — nowhere Max had ever actually stood.
    ///
    /// <see cref="AreaAccumulationDirector.SetCurrentArea"/> then reset both <c>CurrentArea</c> AND the
    /// physical-crossing tracker (<c>_physicalArea</c>) to that wrong area UNCHECKED. The very next real
    /// crossing (Max walking the true route, into a12) found a14 not directly <c>MapLink</c>-adjacent to
    /// a12 — <see cref="AreaAccumulationDirector.Update"/>'s one-hop fallback latched there FOREVER
    /// (<c>LogBlockedAreaJump</c>), since nothing ever drives the tracker back near a14 again. Every area
    /// gated ahead of that point pre-places its garrison (MV-514's <c>PlacePendingGarrison</c>) against
    /// <c>ReachableAreas(_physicalArea)</c>, computed from the now-permanently-stuck, wrong area —
    /// permanently parked (invisible) and never unparked, since unparking only ever happens via a later,
    /// legitimate <c>ParkByReach</c> call the stuck tracker can no longer produce.
    ///
    /// The fix (this ticket): <see cref="AreaAccumulationDirector.PredecessorOf"/> answers "where did Max
    /// actually come from" from real crossing history instead of index arithmetic, deck-aware
    /// (<see cref="WorldRunner"/>'s own resolution asks for the BASE FLOOR area's predecessor for a deck
    /// death, not the deck's own immediate one — skipping past whatever setpiece area sits directly
    /// between them, here a14). This test walks the REAL shipped World 2 gate graph (same idiom as
    /// <c>Mv920AreaTrackingGantryTests</c>) up to and including a death on a15, drives the real
    /// <see cref="WorldRunner.Continue"/>, then keeps walking the real route through a12/a11/a10/a16/a17
    /// to a18/a19/a20 — proving every one of them still produces its authored garrison.
    ///
    /// Must fail on 8c86feb: it doesn't even compile there (CS1061/CS1501 — no
    /// <c>AreaAccumulationDirector.PredecessorOf</c>, no 3-arg <c>RespawnPlanner.Resolve</c> overload).
    /// That's deliberate, not incidental: on 8c86feb, <c>WorldRunner.ResolveDeathArea</c>'s own
    /// pre-existing bug (2-arg, height-unaware <c>MapData.ZoneAt</c>) silently misattributes a death
    /// standing on a15 to a13 (its identical-footprint floor) instead — and since a13 sits in World 2's
    /// plain monotonic 1-14 floor chain, the OLD <c>deathArea - 1</c> formula applied to that WRONG
    /// deathArea (13) coincidentally lands on the SAME area (12) this fix's predecessor lookup gives on
    /// purpose, masking the respawn-arithmetic defect end-to-end. Fixing that ResolveDeathArea bug too
    /// (also this ticket — a death on a deck must resolve to the deck, not silently to its floor) is
    /// what lets deathArea actually reach 15 and makes the direct oldStyle/newStyle comparison below
    /// mean anything; the compile-time failure is the sharpest available proof that the whole fix,
    /// including the crossing-history API this test also calls directly, is genuinely new.
    /// </summary>
    public sealed class MV1002DeckDeathRespawnGarrisonTests
    {
        private GameObject _host;
        private GameObject _areaGo;
        private GameObject _playerGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            DeathRunState.Reset();
            Time.timeScale = 1f;
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            var overlay = Object.FindFirstObjectByType<DeathOverlay>();
            if (overlay != null) Object.DestroyImmediate(overlay.gameObject);

            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_areaGo != null) Object.DestroyImmediate(_areaGo);
            if (_host != null) Object.DestroyImmediate(_host);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            DeathRunState.Reset();
            DevTuning.Reset();
        }

        // The real shipped route to and past the a15 death, in two legs — the same non-monotonic gantry
        // graph Mv920AreaTrackingGantryTests.RealWorldGantryWalk walks (g32 a14-a15, g33 a15-a12, g34
        // a12-a11, g35 a11-a10, g36 a10-a16, g37 a16-a17), verified against MapData.AreLinked below.
        private static readonly int[] PreDeathWalk = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
        private static readonly int[] PostDeathWalk = { 12, 11, 10, 16, 17, 18, 19, 20 };

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        private static void InvokeOnPlayerDied(WorldRunner runner) =>
            typeof(WorldRunner).GetMethod("OnPlayerDied", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(runner, null);

        /// <summary>Same idiom as Mv920AreaTrackingGantryTests.ProbePositionFor — a world position that
        /// resolves, via <see cref="MapData.ZoneAt(float,float,float)"/>, to <paramref name="zone"/>: its
        /// own centre at floor height for a level-0 room, or the centre of its own Deck/Hatch entity at
        /// deck height for a level&gt;0 overlay (a15, a17).</summary>
        private static Vector3 ProbePositionFor(MapData map, MapZone zone)
        {
            Assert.IsNotNull(zone, "setup failure: ProbePositionFor given a null zone");
            if (zone.level == 0) return new Vector3(zone.x, 0.5f, zone.z);

            foreach (MapEntity e in map.entities)
            {
                if (e == null) continue;
                if (e.Kind != EntityKind.Deck && e.Kind != EntityKind.Hatch) continue;
                if (!zone.Contains(e.x, e.z)) continue;
                return new Vector3(e.x, map.deckHeight, e.z);
            }

            Assert.Fail($"setup failure: no Deck/Hatch entity found inside level>0 zone '{zone.id}'");
            return default;
        }

        private static int LiveRobotCountInArea(int areaIndex)
        {
            int count = 0;
            foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
                if (r != null && r.IsAlive && r.AreaIndex == areaIndex) count++;
            return count;
        }

        private static void AssertAuthoredReplicatorsPresent(WorldConfig cfg, MapBuild built, int areaIndex)
        {
            WorldArea area = cfg.AreaByIndex(areaIndex);
            WorldReplicator[] replicators = area?.replicators ?? System.Array.Empty<WorldReplicator>();
            for (int i = 0; i < replicators.Length; i++)
            {
                WorldReplicator r = replicators[i];
                if (r == null) continue;
                string replicatorId = string.IsNullOrEmpty(r.id) ? $"{area.id}_replicator{i + 1}" : r.id;

                Assert.IsTrue(built.Actors.TryGetValue(replicatorId, out GameObject go) && go != null,
                    $"area{areaIndex}: authored Replicator '{replicatorId}' must have been built");

                var replicator = go.GetComponent<Replicator>();
                Assert.IsNotNull(replicator, $"area{areaIndex}: '{replicatorId}' must carry a Replicator component");

                // Same EditMode limitation MV438DeathOverlayTests documents for AreaGate: Awake() (where
                // Replicator.Build() constructs its own DestructibleHealth) isn't reliably invoked for a
                // built actor outside Play mode, so IsAlive reads false until driven directly — Build()
                // is public exactly so a test can do this.
                if (!replicator.IsAlive) replicator.Build();

                Assert.IsTrue(replicator.IsAlive, $"area{areaIndex}: authored Replicator '{replicatorId}' must be present on entry");
            }
        }

        [Test]
        public void DeathOnADeck_RespawnsAtTheFloorsRoutePredecessor_AndEveryLaterAreaStillGarrisons()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            // Setup-failure guards, not ticket assertions: the real graph this test depends on.
            Assert.IsTrue(map.AreLinked("area13", "area14"), "setup failure: World 2's a13-a14 gate (g31) changed");
            Assert.IsTrue(map.AreLinked("area14", "area15"), "setup failure: World 2's a14-a15 gate (g32) changed");
            for (int i = 1; i < PostDeathWalk.Length; i++)
                Assert.IsTrue(map.AreLinked($"area{PostDeathWalk[i - 1]}", $"area{PostDeathWalk[i]}"),
                    $"setup failure: area{PostDeathWalk[i - 1]}-area{PostDeathWalk[i]} is no longer a real MapLink");

            WorldArea a15 = cfg.AreaByIndex(15);
            Assert.AreEqual("a13", a15.overlays, "setup failure: a15 must still overlay a13");

            _host = new GameObject("MV1002 Host");
            MapBuild built = MapRuntime.Build(map, _host.transform);

            // Starve the ambient queue's own cap so only garrison (which bypasses it — AreaSpawnQueue.
            // TryTakeForGarrison's own doc comment) is ever active on entry; otherwise ambient overflow
            // could pad a live count above the authored garrison figure this test pins exactly.
            DevTuning.MaxActiveRobots = 1f;

            _areaGo = new GameObject("Area Accumulation");
            var areaDirector = _areaGo.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg);
            areaDirector.Configure(map, built.Cover);

            var runner = _host.AddComponent<WorldRunner>();
            runner.Configure(cfg, map, built, areaDirector);

            _playerGo = new GameObject("Player") { tag = "Player" };

            // --- walk the real route up to and including the death on a15 ("13 Up") ---
            for (int i = 1; i < PreDeathWalk.Length; i++)
            {
                int toIndex = PreDeathWalk[i];
                _playerGo.transform.position = ProbePositionFor(map, map.Zone($"area{toIndex}"));
                InvokePrivate(areaDirector, "Update");
                areaDirector.EnterArea(toIndex);
            }
            Assert.That(areaDirector.PhysicalArea, Is.EqualTo(15), "precondition: Max is physically standing on a15");

            // Direct, position-independent confirmation of the arithmetic itself — isolated from
            // WorldRunner.ResolveDeathArea's own height-awareness fix (also MV-1002; the E2E flow below
            // already exercises that end to end). For a REAL death at a15, the OLD raw-index formula
            // gives 14 (deathArea - 1); the NEW deck-aware predecessor lookup gives 12 (a13's own route
            // predecessor) instead — these must differ, or this comparison proves nothing.
            int predecessorForA15 = areaDirector.PredecessorOf(13); // a15 overlays a13 (asserted above)
            RespawnPlan oldStyle = RespawnPlanner.Resolve(15, deathGateIsConditionGated: false);
            RespawnPlan newStyle = RespawnPlanner.Resolve(15, deathGateIsConditionGated: false, predecessorForA15);
            Assert.That(oldStyle.RespawnAreaIndex, Is.EqualTo(14),
                "setup failure: raw index-1 arithmetic for a15 must give 14 for this comparison to mean anything");
            Assert.That(newStyle.RespawnAreaIndex, Is.EqualTo(12),
                "MV-1002: the deck-aware predecessor lookup for a death on a15 must give a13's own route " +
                "predecessor (12), not the raw deathArea-1 value (14)");

            // --- Max dies on the deck, then hits CONTINUE ---
            InvokeOnPlayerDied(runner);
            Assert.IsTrue(runner.HasPendingRespawn, "precondition: a death must leave a respawn pending");
            runner.Continue();

            Assert.That(areaDirector.CurrentArea, Is.EqualTo(12),
                "MV-1002: a death on a15 (deck, overlays a13) must respawn at a13's own route predecessor " +
                "(a12) — NOT a14 (deathArea - 1's raw-index arithmetic), which Max never actually stood in");

            // --- keep walking the real route: a12 -> a11 -> a10 -> a16 -> a17 -> a18 -> a19 -> a20 ---
            // Each of a18/a19/a20's live robot count is asserted immediately ON ITS OWN ENTRY, not
            // after the walk moves past it — ParkByReach correctly re-parks an area's resting robots
            // once the player is no longer near it (MV-966), so checking only at the very end would
            // read a legitimately-parked EARLIER area as if it were the MV-1002 defect.
            var toCheck = new HashSet<int> { 18, 19, 20 };
            for (int i = 1; i < PostDeathWalk.Length; i++)
            {
                int toIndex = PostDeathWalk[i];
                _playerGo.transform.position = ProbePositionFor(map, map.Zone($"area{toIndex}"));
                InvokePrivate(areaDirector, "Update");
                areaDirector.EnterArea(toIndex);

                if (!toCheck.Contains(toIndex)) continue;

                WorldArea area = cfg.AreaByIndex(toIndex);
                int seedCount = Garrison.SeedCount(toIndex, cfg);
                // MV-559/MV-601: every AUTHORED garrison entry is placed regardless of the density-dial
                // seed count when there are more of them — SeedSlots, not SeedCount alone, is the real
                // authored figure.
                int expectedGarrison = Garrison.SeedSlots(area, seedCount, cfg).Length;
                Assert.That(expectedGarrison, Is.GreaterThan(0),
                    $"setup failure: area{toIndex} must author a non-zero garrison for this test to mean anything");

                int liveCount = LiveRobotCountInArea(toIndex);
                Assert.That(liveCount, Is.EqualTo(expectedGarrison),
                    $"MV-1002: area{toIndex} must show its authored garrison ({expectedGarrison}) live on entry " +
                    $"— read {liveCount}. A death on a15 that respawned into an unreached area permanently " +
                    "parks every area gated past it (ReachableAreas computed from a stuck physical tracker)");

                AssertAuthoredReplicatorsPresent(cfg, built, toIndex);
            }
        }
    }
}
