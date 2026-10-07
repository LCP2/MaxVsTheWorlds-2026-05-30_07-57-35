using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1098 — Lee (device, TestFlight v0.11.7, World 2): deck garrison robots mis-shelved. "Area 3 up.
    /// No robots. Looks like they might be being spawned underneath" — a17 (the "Junction Hall (deck)"
    /// overlay of floor area a3) seeds its 11 authored level-1 robots at floor height, invisible under the
    /// real deck mesh, rather than at deck elevation.
    ///
    /// Root cause: <see cref="Garrison.DeckFootprints"/>'s overlay ternary always redirected to the
    /// OVERLAY TARGET's (a3's) <c>decks</c> array for a level-1 slot's height/leash lookup — but a3 authors
    /// no decks at all (it's a floor room; its deck geometry is authored on the overlay, a17, itself: see
    /// <c>a17_deck1..4</c>). That shape is the opposite of a13/a15 (Trolley Yard), where the floor AND the
    /// overlay both duplicate the deck rects (MV-907) — which is the only reason that pair ever worked
    /// through the same buggy redirect. The fix prefers an area's OWN <c>decks</c> when it authors any
    /// directly, falling back to the overlay target only when it has none — exactly what this static
    /// method's own (pre-existing, unimplemented) doc comment already promised.
    ///
    /// The same bug silently emptied <see cref="RobotEnemy.SetDeckFootprint"/>'s leash for a17 too (it
    /// reuses <see cref="Garrison.DeckFootprints"/>), so AC(c) below also proves the fix restores the
    /// leash, not just placement height — disproving this ticket's own "Hypothesis for a17" (rule 4: a
    /// correctly-placed robot wandering off a narrow walkway). It was never correctly placed to begin with.
    ///
    /// Real World 2 map/config, the real <see cref="AreaAccumulationDirector"/> fill/garrison path (same
    /// floor-climb-then-walkway route <c>MV1049DeckRevisitGarrisonTests</c> already proves reachable), and
    /// a <see cref="RobotEnemy.Tick"/> the test drives directly for both the 8s dwell (AC a) and the 20s
    /// leash simulation (AC c) — never an authored constant, a private field as the thing under test, or a
    /// bare presence/count check standing in for a per-entry resolved-value assertion (Testing policy
    /// Rules 1-3).
    ///
    /// ONE new test (MV-465 Rule 1), covering all three ACs as sub-assertions of one scenario. Must fail on
    /// d30d293 (this ticket's own base commit) — see the fix comment for the quoted failure output.
    /// // Guards MV-1098
    /// </summary>
    public sealed class MV1098DeckGarrisonPlacementWakeLeashTests
    {
        private const float Dt = 1f / 60f;

        private static readonly FieldInfo VerticalVelField =
            typeof(RobotEnemy).GetField("_verticalVel", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _host;
        private GameObject _areaGo;
        private GameObject _playerGo;
        private GameObject _pathGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            DormantWakeScheduler.ResetForTests();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);

            // AC(c)'s 20s of real awake-and-attacking simulation fires real projectiles (BolterBolt etc.)
            // — root-level GameObjects, never parented under "Area Robots" above, so a bolt in flight when
            // the test ends would otherwise survive into the next test. MV622BolterEngagesSentinelTests
            // (and others) find their own fresh projectile by the same "<Kind> (stand-in)" scene-root name
            // EnemySpawner/BolterBolt/etc. all use - a stray leftover with that name is exactly what let
            // this test's own a17 bolters be picked up by a LATER test's GameObject.Find instead.
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go != null && go.name.EndsWith("(stand-in)")) Object.DestroyImmediate(go);

            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_areaGo != null) Object.DestroyImmediate(_areaGo);
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
            if (_host != null) Object.DestroyImmediate(_host);

            EnemyNavigation.Reset();
            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            DormantWakeScheduler.ResetForTests();
            DevTuning.Reset();
        }

        // Same real shipped route MV1049DeckRevisitGarrisonTests walks: floor chain 1-14, then the
        // walkway itself (a15 "13 Up" -> a12 "12 Up" -> a11 "11 Up" -> a10 "10 Up" -> a16 -> a17).
        private static readonly int[] FloorClimb = { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };
        private static readonly int[] Walkway = { 15, 12, 11, 10, 16, 17 };

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        /// <summary>Same idiom as MV1086ReplicatorDeckEligibilityTests.InvokeTick: EditMode never runs a
        /// real physics step, so gravity integrated every single <see cref="RobotEnemy.Tick"/> would
        /// otherwise drift a robot downward frame over frame with nothing to arrest it (no live collision
        /// resolution against the deck mesh the way Play mode/device actually provides it). Zeroing
        /// <c>_verticalVel</c> stops that drift from COMPOUNDING, but one frame's worth of unarrested fall
        /// still lands every single frame regardless — negligible over MV1086's own 10s floor-level case,
        /// not over this ticket's 20s held ON a deck. Re-pinning Y to <paramref name="holdY"/> after every
        /// tick is what a real collider would already be doing for free; it isolates the thing actually
        /// under test here — the leash's X/Z clamp — from this EditMode-only artefact.</summary>
        private static void InvokeTick(RobotEnemy e, float dt, float holdY)
        {
            e.Tick(dt);
            VerticalVelField.SetValue(e, 0f);
            Vector3 p = e.transform.position;
            e.transform.position = new Vector3(p.x, holdY, p.z);
        }

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

        /// <summary>The Y a live garrison robot for <paramref name="entry"/> should rest at: the deck (or
        /// floor) height <see cref="Garrison.ResolveLevelHeight"/> resolves, PLUS that entry's own kind's
        /// ground clearance (<see cref="EnemyArchetype.SpawnHeight"/>, half its own collider height) —
        /// the same offset <see cref="AreaAccumulationDirector.PlacePendingGarrison"/> adds on top of the
        /// resolved deck Y for every spawn, deck or floor alike, so comparing against the bare deck height
        /// alone would read every real robot as roughly ColliderHeight/2 "too high" regardless of this
        /// ticket's own bug.</summary>
        private static float ExpectedRestingY(WorldArea area, WorldConfig cfg, WorldGarrisonEntry entry)
        {
            float baseY = Garrison.ResolveLevelHeight(area, cfg, new Vector2(entry.x, entry.z));
            float clearance = EnemyKindNames.TryParse(entry.kind, out EnemyKind kind)
                ? EnemyArchetype.For(kind, cfg).SpawnHeight
                : 0f;
            return baseY + clearance;
        }

        private static Vector3 DeckProbePositionFor(WorldConfig cfg, WorldArea area)
        {
            List<Rect> rects = Garrison.DeckFootprints(area, cfg);
            Assert.That(rects.Count, Is.GreaterThan(0), $"setup failure: area {area.id} has no deck rects");
            Rect rect = rects[0];
            Vector2 center = rect.center;
            float y = Garrison.ResolveLevelHeight(area, cfg, center);
            return new Vector3(center.x, y, center.y);
        }

        /// <summary>The live robot standing at authored XZ (<paramref name="x"/>, <paramref name="z"/>) in
        /// <paramref name="areaIndex"/> — the HIGHEST one, same reasoning as MV1049's own helper, so a
        /// floor/deck column that shares an XZ never accidentally reads its own duplicate.</summary>
        private static RobotEnemy LiveGarrisonRobotAt(int areaIndex, float x, float z)
        {
            RobotEnemy best = null;
            foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
            {
                if (r == null || !r.IsAlive || r.AreaIndex != areaIndex) continue;
                Vector3 p = r.transform.position;
                if (Mathf.Abs(p.x - x) > 0.05f || Mathf.Abs(p.z - z) > 0.05f) continue;
                if (best == null || p.y > best.transform.position.y) best = r;
            }
            return best;
        }

        [Test]
        public void DeckGarrison_PlacedOnDeckStaysDormantTillMaxJoinsIt_AndNeverLeavesTheDeckOnceAwake()
        {
            // Same benign collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see MV890AreaGateDressingPairingTests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);
            Assert.IsNotNull(BackyardPathMapField, "BackyardPath._map went missing - EnemyNavigation.Map can't be seeded");

            // Seeds EnemyNavigation.Map (same idiom as MV944FloorDeckCrossLevelDamageTests/
            // MV1086ReplicatorDeckEligibilityTests) so CombatLevel.SameLevel - which fails OPEN/true with
            // no live map - resolves floor-vs-deck against the real geometry instead of masking AC(a)'s
            // own level-gated wake check entirely.
            _pathGo = new GameObject("MV1098-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            BackyardPathMapField.SetValue(path, map);

            _host = new GameObject("MV1098 Host");
            MapBuild built = MapRuntime.Build(map, _host.transform);

            // Same reasoning as MV1002/MV1049: starve the ambient queue so only garrison (which bypasses
            // its own cap) is ever active, so an ambient overflow spawn can never coincidentally land on an
            // authored column and mask a missing/misplaced garrison entry.
            DevTuning.MaxActiveRobots = 1f;

            _areaGo = new GameObject("Area Accumulation");
            var areaDirector = _areaGo.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg);
            areaDirector.Configure(map, built.Cover);

            _playerGo = new GameObject("Player") { tag = "Player" };

            WorldArea a12 = cfg.AreaByIndex(12);
            Assert.IsNotNull(a12, "setup failure: World 2 must still author area 12");
            WorldGarrisonEntry a12FloorEntry = a12.garrison.First(e => e.level <= 0);
            WorldGarrisonEntry[] a12DeckEntries = a12.garrison.Where(e => e.level > 0).ToArray();
            Assert.That(a12DeckEntries.Length, Is.GreaterThan(0),
                "setup failure: a12 authors no level-1 garrison entries - this test proves nothing for AC(a)");

            // --- floor climb: area1 (Configure()'s own start) up to area14, each area's floor visit seeds
            // --- its own floor-level garrison. a12 arrives at its FLOOR position here (ProbePositionFor
            // --- gives the zone's own floor centre for a level-0 zone) - AC(a)'s own "Max walks into a12
            // --- on the floor" moment, as distinct from a12's later deck REVISIT during the walkway below.
            foreach (int toIndex in FloorClimb)
            {
                Vector3 pos = toIndex == 12
                    ? new Vector3(a12FloorEntry.x, 0.5f, a12FloorEntry.z) // stood right by a known floor entry
                    : ProbePositionFor(map, map.Zone($"area{toIndex}"));

                _playerGo.transform.position = pos;
                InvokePrivate(areaDirector, "Update");
                areaDirector.EnterArea(toIndex);

                if (toIndex != 12) continue;

                // --- AC(a): Max stays on a12's floor for 8 simulated seconds. Drives CentralWakeCheck
                // --- directly rather than through DormantWakeScheduler.Tick: a freshly SetActive(true)'d
                // --- pooled robot's OnEnable (which is what normally adds it to RobotEnemy.Active, the
                // --- list the scheduler itself scans) never fires reliably outside Play mode (same gap
                // --- MV1086ReplicatorDeckEligibilityTests.RegisterIfMissing works around) - calling the
                // --- real per-robot wake entry point directly sidesteps that registration gap rather than
                // --- also asserting on it, which isn't what this ticket is about.
                var a12Robots = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None)
                    .Where(r => r != null && r.IsAlive && r.AreaIndex == 12).ToList();
                Assert.That(a12Robots.Count, Is.GreaterThan(0), "setup failure: a12's own garrison never placed");
                for (int f = 0; f < 480; f++)
                    foreach (RobotEnemy r in a12Robots)
                        r.CentralWakeCheck(Dt);

                foreach (WorldGarrisonEntry entry in a12DeckEntries)
                {
                    RobotEnemy deckRobot = LiveGarrisonRobotAt(12, entry.x, entry.z);
                    Assert.IsNotNull(deckRobot,
                        $"MV-1098: a12's authored deck entry ({entry.kind} at {entry.x},{entry.z}) must be a " +
                        "live robot once Max reaches the floor below it - found none");
                    Assert.IsTrue(deckRobot.IsDormant,
                        $"MV-1098: a12's deck entry at ({entry.x},{entry.z}) woke while Max was still on the " +
                        "floor below - floor and deck must wake separately");

                    float expectedY = ExpectedRestingY(a12, cfg, entry);
                    float actualY = deckRobot.transform.position.y;
                    Assert.That(actualY, Is.EqualTo(expectedY).Within(0.2f),
                        $"MV-1098: a12's deck entry at ({entry.x},{entry.z}) sits at Y={actualY:F2}, expected " +
                        $"resting height {expectedY:F2} (within 0.2m)");
                }

                bool anyFloorRobotAwake = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None)
                    .Any(r => r != null && r.IsAlive && r.AreaIndex == 12 && r.Level == 0 && r.IsAwake);
                Assert.IsTrue(anyFloorRobotAwake,
                    "MV-1098: no level-0 robot in a12 ever woke in 8s with Max standing right beside one on " +
                    "the floor - this test proves nothing about level-gated wake if the SAME-level case never fires");
            }

            // --- the walkway itself: a15 ("13 Up") -> a12 ("12 Up") -> a11 ("11 Up") -> a10 ("10 Up")
            // --- -> a16 -> a17. a17 is a TRUE overlay (its own area index, visited exactly once). ---
            foreach (int toIndex in Walkway)
            {
                Vector3 pos = toIndex == 10 || toIndex == 11 || toIndex == 12
                    ? DeckProbePositionFor(cfg, cfg.AreaByIndex(toIndex))
                    : ProbePositionFor(map, map.Zone($"area{toIndex}"));

                _playerGo.transform.position = pos;
                InvokePrivate(areaDirector, "Update");
                areaDirector.EnterArea(toIndex);
            }

            // --- AC(b): a17 filled the way play fills it - all 11 authored robots stand on a deck rect,
            // --- at deck height. ---
            WorldArea a17 = cfg.AreaByIndex(17);
            Assert.IsNotNull(a17, "setup failure: World 2 must still author area 17");
            Assert.AreEqual("a3", a17.overlays, "setup failure: a17 must still overlay a3 - this is the shape MV-1098 is about");

            List<Rect> a17DeckRects = Garrison.DeckFootprints(a17, cfg);
            Assert.That(a17DeckRects.Count, Is.GreaterThan(0),
                "MV-1098: Garrison.DeckFootprints(a17) returned no rects - a17 authors its own decks directly " +
                "and must resolve to them without needing a3 (its overlay target) to duplicate them");

            WorldGarrisonEntry[] a17DeckEntries = a17.garrison.Where(e => e.level > 0).ToArray();
            Assert.AreEqual(11, a17DeckEntries.Length,
                "setup failure: a17 must still author exactly 11 level-1 garrison entries");

            var a17Robots = new List<(WorldGarrisonEntry entry, RobotEnemy robot)>(a17DeckEntries.Length);
            foreach (WorldGarrisonEntry entry in a17DeckEntries)
            {
                RobotEnemy r = LiveGarrisonRobotAt(17, entry.x, entry.z);
                Assert.IsNotNull(r,
                    $"MV-1098: a17's authored deck entry ({entry.kind} at {entry.x},{entry.z}) must be a live " +
                    "robot once Max reaches the gantry - found none");

                Vector3 p = r.transform.position;
                float expectedY = ExpectedRestingY(a17, cfg, entry);
                Assert.That(p.y, Is.EqualTo(expectedY).Within(0.2f),
                    $"MV-1098: a17's deck entry at ({entry.x},{entry.z}) resolved to Y={p.y:F2}, expected " +
                    $"resting height {expectedY:F2} - placed at (or near) floor height instead of its deck");

                bool insideDeck = a17DeckRects.Exists(rect => rect.Contains(new Vector2(p.x, p.z)));
                Assert.IsTrue(insideDeck,
                    $"MV-1098: a17's deck entry at ({entry.x},{entry.z}) resolved to ({p.x:F1},{p.z:F1}) - " +
                    "outside every deck rect");

                a17Robots.Add((entry, r));
            }

            // --- AC(c): Max on a17's deck, robots awake and attacking for 20 simulated seconds - every
            // --- one of them is still on a deck rect at deck height throughout. Activate() directly
            // --- (a real, public, documented wake entry point - MV1086ReplicatorDeckEligibilityTests'
            // --- SpawnAwakeViaPool is the same idiom: isolate the mechanic under test, the leash, from an
            // --- unrelated and geometry-fragile dependency, here a guaranteed line-of-sight wake). ---
            Vector3 deckStandPoint = DeckProbePositionFor(cfg, a17);
            _playerGo.transform.position = deckStandPoint;

            foreach (var (_, robot) in a17Robots) robot.Activate();

            for (int f = 0; f < 1200; f++) // 1200 * 1/60f = 20s
                foreach (var (entry, robot) in a17Robots)
                    if (robot.IsAlive) InvokeTick(robot, Dt, ExpectedRestingY(a17, cfg, entry));

            foreach (var (entry, robot) in a17Robots)
            {
                Assert.IsTrue(robot.IsAlive, "setup failure: an a17 deck robot died mid-simulation with nothing attacking it");
                Assert.IsFalse(robot.IsDormant, "setup failure: Activate() did not wake this a17 deck robot");

                Vector3 p = robot.transform.position;
                bool stillOnDeck = a17DeckRects.Exists(rect => rect.Contains(new Vector2(p.x, p.z)));
                Assert.IsTrue(stillOnDeck,
                    $"MV-1098: an awake a17 deck robot ({entry.kind} authored at {entry.x},{entry.z}) ended up at " +
                    $"({p.x:F1},{p.z:F1}) after 20s of chasing Max on the deck - outside every deck rect (the " +
                    "leash failed to hold it)");

                float expectedY = ExpectedRestingY(a17, cfg, entry);
                Assert.That(p.y, Is.EqualTo(expectedY).Within(0.2f),
                    $"MV-1098: an awake a17 deck robot ({entry.kind} authored at {entry.x},{entry.z}) ended up " +
                    $"at Y={p.y:F2} after 20s - expected resting height {expectedY:F2} (within 0.2m)");
            }
        }
    }
}
