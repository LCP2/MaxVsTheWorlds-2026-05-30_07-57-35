using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1086 — Lee's device play of TestFlight v0.11.7, World 2 area a10 ("Overflow Cistern"): the
    /// floor Replicator (a10_rep1) lured a robot standing on the upper deck — "can't get to it and should
    /// be separate... No robots on the upper level of area 10." Root cause: <c>Replicator.IsEligibleFor</c>
    /// never checked <see cref="CombatLevel.SameLevel"/>, so a10's own 16 level-1 (deck) garrison entries
    /// (authored under the SAME area id as its 18 level-0 floor ones) were eligible for the floor box, and
    /// MV-1066's guaranteed-arrival glide carried them off their own level to close the gap — a
    /// Replicator-shaped hole in MV-944's "floor and deck fight separately" rule.
    ///
    /// Real World 2 map/config, a10's own single real Replicator (stamped by the real
    /// <see cref="WorldRunner"/>), and its own real authored garrison entries (<see cref="Garrison.SeedSlots"/>)
    /// — the nearest non-Lurker/Turret level-0 and level-1 entry to the box, both spawned through the
    /// real pooled spawn path (<see cref="EnemySpawner"/>'s own private <c>CreateInstance</c>, same idiom
    /// as <c>MV984AwakeRobotsTakeDamageTests.SpawnViaPool</c>) so both start genuinely awake — "otherwise
    /// eligible" per the ticket's own AC. Max's own area-entry is driven through the real, public
    /// <see cref="Replicator.OnAreaEntered"/> entry point, then 10 simulated seconds of the real
    /// <see cref="RobotEnemy.Tick"/> / <see cref="Replicator.TickConsumption"/> pair — never a shortcut
    /// bypassing either.
    ///
    /// Must fail on d30d293 (this ticket's own base commit) — see the fix comment for this test's own
    /// captured failure output.
    ///
    /// ONE new test (MV-465 Rule 1). Tier 2 throughout (Rule 2): every assertion reads a resolved runtime
    /// value — IsAssignedToReplicator / IsBeingDrawnIn / transform.position.y off the real robots, the
    /// real REPL row's own `taken` counter off the real session-events CSV — never an authored constant,
    /// never a rendered pixel.
    /// // Guards MV-1086
    /// </summary>
    public sealed class MV1086ReplicatorDeckEligibilityTests
    {
        private static readonly FieldInfo VerticalVelField =
            typeof(RobotEnemy).GetField("_verticalVel", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo CreateInstanceMethod =
            typeof(EnemySpawner).GetMethod("CreateInstance", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo RobotActiveListField =
            typeof(RobotEnemy).GetField("_active", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _host;
        private GameObject _areaGo;
        private GameObject _playerGo;
        private GameObject _pathGo;
        private RobotEnemy _floorRobot;
        private RobotEnemy _deckRobot;
        private string _tempDir;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            FactoryExitZones.ResetForTests();
            DormantWakeScheduler.ResetForTests();
            EnemySpawner.ResetReplicatorReservations();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            Bootstrap.SetActiveSessionRecorderForTest(null);

            if (_floorRobot != null) UnityEngine.Object.DestroyImmediate(_floorRobot.gameObject);
            if (_deckRobot != null) UnityEngine.Object.DestroyImmediate(_deckRobot.gameObject);
            if (_playerGo != null) UnityEngine.Object.DestroyImmediate(_playerGo);
            if (_areaGo != null) UnityEngine.Object.DestroyImmediate(_areaGo);
            if (_pathGo != null) UnityEngine.Object.DestroyImmediate(_pathGo);
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);

            EnemyNavigation.Reset();
            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            FactoryExitZones.ResetForTests();
            DormantWakeScheduler.ResetForTests();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();

            if (_tempDir != null && Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }

        private static void InvokeTick(RobotEnemy e, float dt)
        {
            e.Tick(dt);
            VerticalVelField.SetValue(e, 0f);
        }

        /// <summary>Same reasoning as MV1066ReplicatorLureReproTests.RegisterAllUnregisteredRobots — a
        /// pooled robot's OnEnable (which is what normally adds it to <see cref="RobotEnemy.Active"/>,
        /// the list <see cref="Replicator.NearestEligible"/> itself scans) never fires reliably outside
        /// Play mode, so a freshly SetActive(true)'d instance can silently stay invisible to it.</summary>
        private static void RegisterIfMissing(RobotEnemy r)
        {
            var list = (List<RobotEnemy>)RobotActiveListField.GetValue(null);
            if (!list.Contains(r)) list.Add(r);
        }

        /// <summary>Same real pooled spawn path as MV984AwakeRobotsTakeDamageTests.SpawnViaPool — drives
        /// the real, private EnemySpawner.CreateInstance via reflection (the normal pool/factory path
        /// every in-game robot is built through, not a hand-rolled stand-in), which leaves the instance
        /// inactive, then activates it (running the real OnEnable/ResetState, putting it in a genuinely
        /// awake Chase state) at the given world position.</summary>
        private static RobotEnemy SpawnAwakeViaPool(EnemyArchetype archetype, Vector3 pos, int areaIndex)
        {
            var spawnerGo = new GameObject("MV1086 test spawner");
            try
            {
                var spawner = spawnerGo.AddComponent<EnemySpawner>();
                Assert.IsNotNull(CreateInstanceMethod, "EnemySpawner.CreateInstance went missing");

                LogAssert.ignoreFailingMessages = true;
                RobotEnemy e;
                try { e = (RobotEnemy)CreateInstanceMethod.Invoke(spawner, new object[] { archetype }); }
                finally { LogAssert.ignoreFailingMessages = false; }

                e.transform.SetParent(null, worldPositionStays: false);
                e.transform.position = pos;
                e.gameObject.SetActive(true);
                e.SetAreaIndex(areaIndex);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)
                return e;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(spawnerGo);
            }
        }

        [Test]
        public void FloorReplicatorInAreaA10_NeverLuresTheDeckRobot_OnlyTheFloorRobotIsTakenIn()
        {
            const float dt = 1f / 60f;
            const int frames = 600; // MV-1086's own "10 simulated seconds"

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);
            Assert.IsNotNull(BackyardPathMapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");

            // Same idiom as MV944FloorDeckCrossLevelDamageTests/MV1001PickupDeckHeightAndLevelTests — seeds
            // EnemyNavigation.Map so CombatLevel.SameLevel (fails OPEN/true with no live map, by its own
            // doc comment) has a real map to resolve floor-vs-deck against instead of masking this ticket's
            // own fix entirely.
            _pathGo = new GameObject("MV1086-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            BackyardPathMapField.SetValue(path, map);

            WorldArea a10 = cfg.AreaByIndex(10);
            Assert.IsNotNull(a10, "setup failure: World 2 must still author area 10");

            _host = new GameObject("MV1086 Host");
            MapBuild built = MapRuntime.Build(map, _host.transform);

            _areaGo = new GameObject("Area Accumulation");
            var director = _areaGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, built.Cover); // auto-fills area1

            // Stamps real AreaIndex/Id onto every built Replicator (MV-828) — without this NearestEligible
            // matches nothing, same ordering every other Replicator EditMode test in this suite relies on.
            var runner = _host.AddComponent<WorldRunner>();
            runner.Configure(cfg, map, built, director);

            List<Replicator> a10Reps = built.Replicators.Where(r => r.AreaIndex == 10).ToList();
            Assert.AreEqual(1, a10Reps.Count, "setup failure: a10 must still author exactly one Replicator");
            Replicator box = a10Reps[0];
            // AddComponent's own Awake never runs outside Play mode (Replicator.Build's own doc comment).
            box.Build();
            FactoryExitZones.Register(box);

            // Real authored garrison data (MV-1086's own Cause: a10 holds floor/level-0 and deck/level-1
            // robots under ONE area id) — the nearest non-Lurker/Turret entry of each level to the box, so
            // both are genuinely "otherwise eligible" (close enough to matter), never a hand-picked spot.
            int seedCount = Garrison.SeedCount(10, cfg);
            Garrison.Seed[] slots = Garrison.SeedSlots(a10, seedCount, cfg);
            Assert.That(slots.Length, Is.GreaterThan(0), "setup failure: a10 must author a non-zero garrison");

            Vector3 boxPos = box.transform.position;

            Garrison.Seed floorSeed = slots
                .Where(s => s.Level == 0 && s.Kind.HasValue && s.Kind != EnemyKind.Lurker && s.Kind != EnemyKind.Turret)
                .OrderBy(s => Vector3.Distance(s.Position, boxPos))
                .First();
            Garrison.Seed deckSeed = slots
                .Where(s => s.Level == 1 && s.Kind.HasValue && s.Kind != EnemyKind.Lurker && s.Kind != EnemyKind.Turret)
                .OrderBy(s => Vector3.Distance(s.Position, boxPos))
                .First();

            Assert.Greater(deckSeed.Position.y, floorSeed.Position.y,
                "setup failure: a10's own nearest deck entry must resolve ABOVE its nearest floor entry " +
                "for this to be a floor/deck test at all");

            _floorRobot = SpawnAwakeViaPool(EnemyArchetype.For(floorSeed.Kind.Value, cfg), floorSeed.Position, 10);
            _deckRobot = SpawnAwakeViaPool(EnemyArchetype.For(deckSeed.Kind.Value, cfg), deckSeed.Position, 10);
            // MV-697: the real level/deck-footprint stamp PlacePendingGarrison itself gives a level-1
            // garrison member — without it this robot reads as an ordinary (unleashed) level-0 one.
            _deckRobot.SetLevel(1);
            _deckRobot.SetDeckFootprint(Garrison.DeckFootprints(a10, cfg));

            RegisterIfMissing(_floorRobot);
            RegisterIfMissing(_deckRobot);

            CollectionAssert.Contains(RobotEnemy.Active, _floorRobot,
                "setup failure: the floor robot never registered in RobotEnemy.Active");
            CollectionAssert.Contains(RobotEnemy.Active, _deckRobot,
                "setup failure: the deck robot never registered in RobotEnemy.Active");

            // Max standing on the floor in a10, a couple of metres back from the box.
            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = new Vector3(boxPos.x, 0.5f, boxPos.z - 2f);

            _tempDir = Path.Combine(Path.GetTempPath(), "mv1086-" + Guid.NewGuid().ToString("N"));
            var recorder = new PerfSessionRecorder(_tempDir, "testbuild", DateTime.UtcNow);
            Bootstrap.SetActiveSessionRecorderForTest(recorder);

            // The real, public area-entry entry point (Replicator.OnAreaEntered's own doc comment: "an
            // EditMode test can drive it directly") — fills the queue immediately, same as a real crossing.
            box.OnAreaEntered(10);

            float deckMinY = _deckRobot.transform.position.y;
            bool deckEverAssigned = false;
            bool deckEverDrawnIn = false;

            for (int f = 0; f < frames; f++)
            {
                if (_floorRobot.IsAlive) InvokeTick(_floorRobot, dt);
                if (_deckRobot.IsAlive) InvokeTick(_deckRobot, dt);
                box.TickConsumption(dt);

                if (_deckRobot.IsAlive)
                {
                    deckEverAssigned |= _deckRobot.IsAssignedToReplicator;
                    deckEverDrawnIn |= _deckRobot.IsBeingDrawnIn;
                    deckMinY = Mathf.Min(deckMinY, _deckRobot.transform.position.y);
                }
            }

            // The ticket's own "after 10 simulated seconds" window ends exactly here — every AC assertion
            // below reads state as of frame 600, nothing later.
            float deckYFloor = map.deckHeight - 0.1f;

            Assert.IsFalse(deckEverAssigned,
                "MV-1086: the DECK robot was assigned to the FLOOR replicator (IsAssignedToReplicator true "
                + "at some point) — a10's floor box must only ever lure robots on its own combat level");
            Assert.IsFalse(deckEverDrawnIn,
                "MV-1086: the DECK robot was drawn into the FLOOR replicator's hatch (IsBeingDrawnIn true "
                + "at some point)");
            Assert.That(deckMinY, Is.GreaterThanOrEqualTo(deckYFloor),
                $"MV-1086: the DECK robot's own y dropped to {deckMinY:F2} (deck height {map.deckHeight:F2}) "
                + "— floor and deck are separate combat spaces (MV-944), this box must never pull it down");

            Assert.IsFalse(_floorRobot.IsAlive,
                "MV-1086: the FLOOR robot (same level as the box) was never taken in within 10s — this test "
                + "proves nothing about level separation if the box cannot even take its own level's robot");

            // A few more box-only ticks (the floor robot is already despawned, and the untouched deck robot
            // has nothing left to react to) purely so the REPL telemetry's own 5s cadence — independent of
            // this ticket's 10s AC window above — flushes one more row reflecting the now-settled outcome.
            for (int f = 0; f < 360; f++) box.TickConsumption(dt);

            recorder.Flush();
            string[] eventLines = File.ReadAllLines(recorder.EventsCsvPath);
            string[] replLines = eventLines.Where(l => l.Contains(",REPL,")).ToArray();
            Assert.That(replLines.Length, Is.GreaterThan(0),
                "setup failure: Max standing in a10 must have written at least one REPL row");

            // The REPL row itself carries no robot-name field (area/box/in/cap/q/elig/taken counts only,
            // see Replicator.TickConsumption's own RecordEvent call) — the real, measurable proxy for
            // "names only the floor robot" is that its own `taken` counter, which only ever increments for
            // a robot that actually passed the hatch, never reflects more than the one eligible robot.
            Assert.That(replLines, Has.None.Contains("taken=2"),
                "MV-1086: a REPL row recorded taken=2 — the deck robot must never contribute to this box's "
                + "own taken count");
            Assert.That(replLines.Last(), Does.Contain("taken=1"),
                "MV-1086: the final REPL row must show exactly the floor robot taken in, and only it");
        }
    }
}
