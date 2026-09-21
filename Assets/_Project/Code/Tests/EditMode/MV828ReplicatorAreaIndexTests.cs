using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-828 — proven from code at 26548e1: <c>WorldRunner.Configure</c> stamped every Replicator's
    /// AreaIndex from <c>AreaAccumulationDirector.AreaIndexOf(area.id)</c>, but a world config's area id
    /// (e.g. World 2's "a3") is never the "area&lt;N&gt;" string that parser recognises — every Replicator
    /// resolved AreaIndex 0, so <c>NearestEligible</c> never matched a robot in real play.
    ///
    /// This test loads a world through the real <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/>/
    /// <see cref="AreaAccumulationDirector"/>/<see cref="WorldRunner"/> path — no direct
    /// <c>SetAreaIndex</c> call anywhere in this file — using the same non-"area&lt;N&gt;" raw id shape
    /// ("a3") World 2 actually authors, so the exact real defect reproduces regardless of the fixture's
    /// own numeric index. One combat area ("a3") carries one Replicator and four garrisoned Rushers (at
    /// controlled distances) plus a fifth held back in the ordinary ambient queue, giving every one of
    /// AC1(a)-(e) a deterministic, observable outcome from the SAME built scene:
    ///
    /// (a) every Replicator's AreaIndex matches its zone's real index and is &gt;= 1 (the core WorldRunner
    /// bug — MV-828 Change 1);
    /// (b) the real <see cref="AreaAccumulationDirector.PlayerCrossedIntoArea"/> signal assigns exactly
    /// the two nearest garrisoned robots (MV-820 R1, unaffected by this ticket — proves the AreaIndex fix
    /// makes R1 reachable at all);
    /// (c) a third robot, forced into Telegraph the moment its slot frees, keeps that slot for 3 s of
    /// ticks instead of being dropped and having a spare farther robot steal it (MV-828 D1/Change 2), and
    /// reaches ReplicatorSeeking (and is genuinely still tracked — it gets drawn in) once Recover runs;
    /// (d) a robot released into the area later, once every existing assignee is gone, is picked up
    /// within 0.6 s by the new periodic retry (MV-828 D2/Change 3) — nothing else would ever trigger it;
    /// (e) that same robot's doubled twin inherits the box's AreaIndex and is already in Chase, never
    /// Dormant, within one tick of spawning (MV-828 D3/Change 4).
    ///
    /// Fails on 26548e1: AreaIndexOf("a3") is 0, so every assertion in (a) fails immediately (AreaIndex 0
    /// != the zone's real index, and 0 is not &gt;= 1) — the run never gets far enough for (b)-(e) to
    /// mean anything on that commit.
    /// </summary>
    public sealed class MV828ReplicatorAreaIndexTests
    {
        private GameObject _root;
        private GameObject _areaGo;
        private GameObject _playerGo;
        private Camera[] _suppressedAmbientCameras;

        private static readonly MethodInfo DirectorUpdateMethod =
            typeof(AreaAccumulationDirector).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo DirectorTimerField =
            typeof(AreaAccumulationDirector).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly PropertyInfo RobotCurrentProperty =
            typeof(RobotEnemy).GetProperty("Current", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo RobotStateTimerField =
            typeof(RobotEnemy).GetField("_stateTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo RobotTickRecoverMethod =
            typeof(RobotEnemy).GetMethod("TickRecover", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo RobotActiveListField =
            typeof(RobotEnemy).GetField("_active", BindingFlags.NonPublic | BindingFlags.Static);

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            if (_areaGo != null) Object.DestroyImmediate(_areaGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_root != null) Object.DestroyImmediate(_root);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
        }

        /// <summary>Same "distinctive far-off origin" idiom every other Replicator EditMode test in this
        /// suite uses (see e.g. MV820ReplicatorQueueingTests) — EditMode tests share one physics scene
        /// for the whole cc-verify run with no per-test reset, so near-origin coordinates risk colliding
        /// with another test's own leftover robots and having NearestEligible pick a stranger instead of
        /// this fixture's own carefully-placed one.</summary>
        private const float OriginX = 41000f;
        private const float OriginZ = 63000f;

        /// <summary>Stub -> "a1" (empty pass-through) -> "a3" (the real content), the same raw,
        /// non-"area&lt;N&gt;" id shape World 2 actually authors ("a3" is a literal example from the
        /// ticket's own observation). "a1" exists only so <see cref="AreaAccumulationDirector.Configure"/>'s
        /// own automatic first-area fill has something to resolve to zero robots against (baseThreat 0);
        /// "a3" carries the Replicator, a controlled 4-robot garrison at known distances from it (3/4/5/6 m),
        /// and a 5th Rusher authored only in composition — left in the ordinary ambient queue, exactly the
        /// "robot released into the area later" AC1(d) needs.</summary>
        private static WorldConfig BuildWorld() => new WorldConfig
        {
            world = "MV-828 Test World",
            dials = new WorldDials
            {
                areaCount = 2, baseThreat = 0f, threatGrowth = 0f,
                pacingRhythm = new[] { 1f, 1f }, powerupCadence = 99,
            },
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", index = 0, role = "entry",
                    origin = new WorldAreaOrigin { x = OriginX - 2f, z = OriginZ - 6f }, size = new WorldAreaSize { w = 4f, d = 6f },
                },
                new WorldArea
                {
                    id = "a1", index = 1, role = "normal",
                    origin = new WorldAreaOrigin { x = OriginX - 10f, z = OriginZ }, size = new WorldAreaSize { w = 20f, d = 20f },
                },
                new WorldArea
                {
                    id = "a3", index = 2, role = "normal", garrisonDensity = "normal",
                    origin = new WorldAreaOrigin { x = OriginX - 10f, z = OriginZ + 20f }, size = new WorldAreaSize { w = 20f, d = 20f },
                    composition = new WorldComposition { rusher = 5 },
                    garrison = new[]
                    {
                        new WorldGarrisonEntry { kind = "rusher", x = OriginX, z = OriginZ + 35f }, // A: 3 m from the box
                        new WorldGarrisonEntry { kind = "rusher", x = OriginX, z = OriginZ + 34f }, // B: 4 m
                        new WorldGarrisonEntry { kind = "rusher", x = OriginX, z = OriginZ + 33f }, // C: 5 m
                        new WorldGarrisonEntry { kind = "rusher", x = OriginX, z = OriginZ + 32f }, // Spare: 6 m
                    },
                    replicators = new[]
                    {
                        // MV-872: capacity 2 (not 4) — the queue is now capped by whichever of
                        // Replicator.MaxQueueSlots (raised 2 -> 6) and this box's own remaining capacity
                        // is smaller, so this fixture's own "only the 2 nearest are assigned at once"
                        // narrative (AC1(b)/(c)/(d) below) now depends on capacity, not on MaxQueueSlots.
                        new WorldReplicator { id = "a3_rep1", x = OriginX, z = OriginZ + 38f, capacity = 2 },
                    },
                },
            },
            gates = new[]
            {
                new WorldGate
                {
                    id = "g0", width = 3f, opensWith = "start",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                },
                new WorldGate
                {
                    id = "g1", width = 3f, opensWith = "primary",
                    from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a3", wall = "S", pos = 0.5f },
                },
            },
        };

        private static void InvokeDirectorUpdate(AreaAccumulationDirector director) =>
            DirectorUpdateMethod.Invoke(director, null);

        /// <summary>Bypasses AreaAccumulationDirector's real Time.deltaTime-driven release timer (an
        /// EditMode run can't advance frames) — same idiom
        /// AreaAccumulationDirectorGarrisonAndPlacementTests.ForceReleaseTimerReady already uses.</summary>
        private static void ForceReleaseTimerReady(AreaAccumulationDirector director) =>
            DirectorTimerField.SetValue(director, 999f);

        /// <summary>AreaAccumulationDirector's own garrison/ambient placement builds a robot via a plain
        /// <c>GameObject.CreatePrimitive</c> + <c>AddComponent&lt;RobotEnemy&gt;</c> (already active, so
        /// the later <c>SetActive(true)</c> is a same-state no-op) — Unity never fires OnEnable for
        /// either step outside Play mode (the same documented quirk
        /// <see cref="AreaAccumulationDirector.RestoreArea"/>'s own doc comment names), so
        /// <see cref="RobotEnemy.Active"/> — what <see cref="Replicator.NearestEligible"/> itself scans —
        /// silently stays empty for every real garrison/ambient spawn in this test unless something
        /// registers it. Adds the static-list entry directly (not a full OnEnable call, which would also
        /// re-run ResetState and stamp a placed-Dormant robot back to a fresh Chase) — the one thing
        /// OnEnable does that production code actually depends on here.</summary>
        private static void RegisterAllUnregisteredRobots()
        {
            var list = (System.Collections.Generic.List<RobotEnemy>)RobotActiveListField.GetValue(null);
            foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
                if (!list.Contains(r)) list.Add(r);
        }

        private static RobotEnemy FindRobotAt(float x, float z)
        {
            foreach (RobotEnemy r in RobotEnemy.Active)
            {
                if (Mathf.Approximately(r.transform.position.x, x) && Mathf.Approximately(r.transform.position.z, z))
                    return r;
            }
            return null;
        }

        private static void ForceTelegraph(RobotEnemy r)
        {
            RobotCurrentProperty.SetValue(r, RobotEnemy.State.Telegraph);
        }

        /// <summary>Forces a robot straight into Recover-completion (MV-828 D1's own "on entering Recover
        /// it seeks its own current slot" — <see cref="RobotEnemy.TickRecover"/> is what actually runs
        /// that hand-off).</summary>
        private static void ForceRecoverCompletion(RobotEnemy r)
        {
            RobotCurrentProperty.SetValue(r, RobotEnemy.State.Recover);
            RobotStateTimerField.SetValue(r, 999f);
            RobotTickRecoverMethod.Invoke(r, new object[] { 0f });
        }

        [Test]
        public void ReplicatorAreaIndex_ResolvesFromWorldRunner_AndDrivesQueueingPendingRetryAndTwins()
        {
            WorldConfig cfg = BuildWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            // Caps the ordinary ambient release exactly at the garrison count (4): garrison itself
            // BYPASSES this cap entirely (MV-417 — a garrison is placed synchronously, independent of
            // the queue's own concurrent cap), but the plain "while queued and under cap" release loop
            // FillArea also runs would otherwise instantly dump the 5th (queue-only) Rusher into the
            // scene the moment the area fills — AC1(d) needs it to stay queued until a slot is freed.
            DevTuning.MaxActiveRobots = 4f;

            _root = new GameObject("MV-828 Root");
            MapBuild built = MapRuntime.Build(map, _root.transform);

            _areaGo = new GameObject("Area Accumulation");
            var director = _areaGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, built.Cover); // auto-fills area1 (empty — baseThreat 0)

            var runner = _root.AddComponent<WorldRunner>();
            runner.Configure(cfg, map, built, director);

            Assert.AreEqual(1, built.Replicators.Count, "setup failure: the fixture must build exactly one Replicator");
            Replicator box = built.Replicators[0];
            // AddComponent's own Awake never runs outside Play mode (Replicator.Build's own doc comment).
            typeof(Replicator).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(box, null);

            // === AC1(a): AreaIndex resolves from the real WorldRunner path, not AreaIndexOf(area.id) ===
            WorldArea a3 = cfg.Area("a3");
            Assert.AreEqual(a3.index, box.AreaIndex,
                "MV-828: the Replicator's AreaIndex must equal the index of the zone containing it");
            Assert.GreaterOrEqual(box.AreaIndex, 1,
                "MV-828: a combat-area Replicator's AreaIndex must never resolve to 0");

            // Real area-entry signal, wired manually (Replicator.Start() never runs outside Play mode —
            // see that method's own doc comment) — this is the actual event AreaAccumulationDirector
            // fires, not a direct OnAreaEntered call.
            director.PlayerCrossedIntoArea += box.OnAreaEntered;

            // The gate-open head start (MV-245/MV-514): garrison is placed synchronously here, well
            // before Max's physical position crosses in — exactly the real ordering BackyardPath drives
            // off AreaGate.Opened.
            director.EnterArea(2);
            RegisterAllUnregisteredRobots();
            Debug.Log($"MV828-DIAG after EnterArea(2): queued={director.QueuedCount} active={director.ActiveCount} totalRobots={RobotEnemy.ActiveCount}");

            RobotEnemy robotA = FindRobotAt(OriginX, OriginZ + 35f);
            RobotEnemy robotB = FindRobotAt(OriginX, OriginZ + 34f);
            RobotEnemy robotC = FindRobotAt(OriginX, OriginZ + 33f);
            RobotEnemy spare = FindRobotAt(OriginX, OriginZ + 32f);
            Assert.IsNotNull(robotA, "setup failure: garrisoned robot A (3 m) was not placed");
            Assert.IsNotNull(robotB, "setup failure: garrisoned robot B (4 m) was not placed");
            Assert.IsNotNull(robotC, "setup failure: garrisoned robot C (5 m) was not placed");
            Assert.IsNotNull(spare, "setup failure: garrisoned spare robot (6 m) was not placed");

            // Snapshot taken before any consumption/doubling can run: AC1(e)'s twin search below needs
            // "known before any twin could have spawned", not "known right before the search loop" — a
            // twin can already be in flight by then (from C's own consumption further down), and a
            // snapshot taken that late would wrongly capture it as "known" and never detect it.
            var knownBefore = new System.Collections.Generic.HashSet<RobotEnemy>(RobotEnemy.Active);

            // The real physical-crossing signal — Max's position, not a direct OnAreaEntered call.
            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = new Vector3(OriginX, 0f, OriginZ + 38f);
            InvokeDirectorUpdate(director);
            Debug.Log($"MV828-DIAG after crossing Update(): queued={director.QueuedCount} active={director.ActiveCount} totalRobots={RobotEnemy.ActiveCount}");

            // === AC1(b): exactly the two nearest become ReplicatorSeeking on the real entry signal ===
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, robotA.Current,
                "MV-828 AC1(b): the nearest robot (3 m) must be ReplicatorSeeking after the real area-entry signal");
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, robotB.Current,
                "MV-828 AC1(b): the second-nearest robot (4 m) must be ReplicatorSeeking after the real area-entry signal");
            Assert.AreNotEqual(RobotEnemy.State.ReplicatorSeeking, robotC.Current,
                "setup failure: with this fixture's own capacity of 2 (not Replicator.MaxQueueSlots), " +
                "the 3rd-nearest must not be assigned yet");
            Assert.AreNotEqual(RobotEnemy.State.ReplicatorSeeking, spare.Current,
                "setup failure: the spare robot must not be assigned yet");

            // === AC1(c): assigned while in Telegraph, still tracked after 3 s, reaches ===
            // === ReplicatorSeeking (and is genuinely still owned) after Recover ===
            ForceTelegraph(robotC);
            robotA.CancelReplicatorSeeking();
            robotA.TagNoReplicatePermanent(); // simulates "no longer eligible" so it can't be re-picked
            box.TickConsumption(0.02f); // cleanup drops A, refills the freed slot with C (now Telegraph)

            Assert.IsTrue(robotC.IsAssignedToReplicator,
                "MV-828 D1: a robot assigned while in Telegraph must be tagged assigned immediately");
            Assert.AreEqual(RobotEnemy.State.Telegraph, robotC.Current,
                "setup failure: C must still be finishing its Telegraph, not yet ReplicatorSeeking");

            for (int i = 0; i < 300; i++) box.TickConsumption(0.01f); // 300 x 0.01 s = 3 s

            Assert.IsTrue(robotC.IsAssignedToReplicator,
                "MV-828 D1: a robot assigned while in Telegraph must still be assigned after 3 s of ticks");
            Assert.AreNotEqual(RobotEnemy.State.ReplicatorSeeking, spare.Current,
                "MV-828 D1: the spare robot's slot must not have been stolen from the pending Telegraph " +
                "assignee by the cleanup pass wrongly dropping it");

            spare.TagNoReplicatePermanent(); // its part in AC1(c) is proven; retire it

            ForceRecoverCompletion(robotC);
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, robotC.Current,
                "MV-828 D1: on entering Recover, a pending assignee must seek its own current slot");

            // Retire B (never itself under test past AC1(b)) so C — sitting behind it in the FIFO
            // queue — can actually reach slot 0: only the queue head is ever eligible for Intake.
            robotB.CancelReplicatorSeeking();
            robotB.TagNoReplicatePermanent();
            box.TickConsumption(0.02f); // flush: drops B, retargets C onto the now-vacant slot 0

            robotC.transform.position = robotC.ReplicatorSeekTarget; // genuinely slot 0 now
            int guardC = 0;
            while (robotC.IsAlive && guardC++ < 300) box.TickConsumption(0.02f);
            Assert.IsFalse(robotC.IsAlive,
                "MV-828 D1: a robot that reaches ReplicatorSeeking after Recover must still be genuinely " +
                "tracked by the box's own queue — it must actually get drawn in and consumed, not orphaned");
            Debug.Log($"MV828-DIAG after C dies: queued={director.QueuedCount} active={director.ActiveCount} totalRobots={RobotEnemy.ActiveCount}");

            // === AC1(d): a robot released into the area later, with a slot empty, is assigned ===
            // === within 0.6 s (the new periodic retry — nothing else triggers this; C's own draw-in ===
            // === above already left the queue empty with nobody else eligible) ===
            EnemySpawner spawner = box.GetComponent<EnemySpawner>();
            ForceReleaseTimerReady(director);
            InvokeDirectorUpdate(director); // releases the 5th (queue-only) Rusher into area index 2
            RegisterAllUnregisteredRobots();

            // Identified by aliveness, not object identity: AreaAccumulationDirector pools a dead
            // robot's own GameObject by kind (OnEnemyDied), so this release may literally REUSE C's own
            // now-dead RobotEnemy reference — a reference-based "new since a snapshot" diff would wrongly
            // still call it "known" and miss it. A/B/Spare are excluded by name; C is excluded for free
            // since it's the one thing here that is NOT alive.
            RobotEnemy robotD = null;
            foreach (RobotEnemy r in RobotEnemy.Active)
            {
                if (!r.IsAlive || r == robotA || r == robotB || r == spare) continue;
                if (r.AreaIndex == box.AreaIndex) { robotD = r; break; }
            }
            Assert.IsNotNull(robotD, "setup failure: the 5th Rusher was not released into area index 2");

            float elapsedD = 0f;
            const float dtD = 0.02f;
            bool assignedWithinBudget = false;
            while (elapsedD < 0.6f + 1e-4f)
            {
                box.TickConsumption(dtD);
                elapsedD += dtD;
                if (robotD.IsAssignedToReplicator) { assignedWithinBudget = true; break; }
            }
            Assert.IsTrue(assignedWithinBudget,
                $"MV-828 D2: a robot released into the area after entry, with a slot empty, must be " +
                $"assigned within 0.6 s of ticks (nothing else triggers this — {elapsedD:F2}s elapsed)");
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, robotD.Current,
                "setup failure: D2's assignment must reach ReplicatorSeeking directly (D was never mid-attack)");

            // === AC1(e): a doubled twin inherits the box's AreaIndex and is already in Chase, ===
            // === never Dormant, as soon as it spawns. Keep ticking — C's own consumption above ===
            // === already has a twin in flight, so this never depends on D reaching Intake itself. ===
            RobotEnemy twin = null;
            int guardTwin = 0;
            while (twin == null && guardTwin++ < 400)
            {
                box.TickConsumption(0.02f);
                RegisterAllUnregisteredRobots();
                foreach (RobotEnemy r in RobotEnemy.Active)
                {
                    if (knownBefore.Contains(r) || r == robotD) continue;
                    twin = r;
                    break;
                }
            }
            Assert.IsNotNull(twin, "setup failure: no twin ever emitted from this box");
            Assert.AreEqual(box.AreaIndex, twin.AreaIndex,
                "MV-828 D3: a doubled twin must inherit the box's own AreaIndex, never the pooled 0 Apply() resets to");
            Assert.AreEqual(RobotEnemy.State.Chase, twin.Current,
                "MV-828 D3: a doubled twin must land in Chase — awake and attacking — never Dormant/Emerging");
        }
    }
}
