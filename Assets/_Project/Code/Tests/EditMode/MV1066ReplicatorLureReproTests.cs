using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1066 — Lee's fifth report: World 2 a13/a19 Replicators still never take robots in, even after
    /// MV-1047 (seeding <c>_playerInArea</c> at Start) shipped in v0.11.5. This is the ticket's own
    /// "reproduce in a13 first" fail-first test (Step 1): the REAL shipped World 2 route (area1..area12,
    /// then cross into a13 through g30 — same idiom as MV1027AuthoredGarrisonExemptFromMusterTests),
    /// a13's own real 12 built Replicators wired to the REAL <see cref="AreaAccumulationDirector.PlayerCrossedIntoArea"/>
    /// signal (never a direct <c>OnAreaEntered</c> call — same "the actual event, not a shortcut" idiom
    /// as MV828ReplicatorAreaIndexTests), Max standing still at a13's west entry (the same g30 far-side
    /// point MV918InvisibleGateLeafCornerTests already measured), and 60 simulated seconds of real
    /// per-frame ticking of every a13 robot's own <c>Tick</c> and every a13 Replicator's own
    /// <c>TickConsumption</c> — never a direct call bypassing either.
    ///
    /// Must fail on 9232044 (this ticket's own base commit) — see the fix comment for this test's own
    /// captured failure output and state dump, and the PROVEN blocker read off it.
    ///
    /// ONE new test (testing policy MV-465, Rule 1). Tier 2 throughout (Rule 2): every assertion reads a
    /// box's own resolved <see cref="EnemySpawner.Emitted"/> count after real ticking, never an authored
    /// constant, never a rendered pixel. // Guards MV-1066
    /// </summary>
    public sealed class MV1066ReplicatorLureReproTests
    {
        private static readonly MethodInfo DirectorUpdateMethod =
            typeof(AreaAccumulationDirector).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo VerticalVelField =
            typeof(RobotEnemy).GetField("_verticalVel", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo RobotActiveListField =
            typeof(RobotEnemy).GetField("_active", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo SeekStallTimerField =
            typeof(RobotEnemy).GetField("_seekStallTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo QueueField =
            typeof(Replicator).GetField("_queue", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _host;
        private GameObject _areaGo;
        private GameObject _playerGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            FactoryExitZones.ResetForTests();
            DormantWakeScheduler.ResetForTests();
            EnemySpawner.ResetReplicatorReservations();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);

            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_areaGo != null) Object.DestroyImmediate(_areaGo);
            if (_host != null) Object.DestroyImmediate(_host);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            FactoryExitZones.ResetForTests();
            DormantWakeScheduler.ResetForTests();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
        }

        /// <summary>Same "level-0 floor centre" idiom as MV1027AuthoredGarrisonExemptFromMusterTests.ProbePositionFor.</summary>
        private static Vector3 ProbePositionFor(MapZone zone)
        {
            Assert.IsNotNull(zone, "setup failure: ProbePositionFor given a null zone");
            return new Vector3(zone.x, 0.5f, zone.z);
        }

        /// <summary>a13's own west entry, the far side of g30 from a12 — the exact point
        /// MV918InvisibleGateLeafCornerTests already measured and named ("a13 (g30)" destX/destZ) as
        /// genuinely resolving to area13, not the gate zone or a12.</summary>
        private static readonly Vector3 A13WestEntry = new Vector3(274.3f, 0.5f, 105.8f);

        private static void InvokeTick(RobotEnemy e, float dt)
        {
            e.Tick(dt);
            VerticalVelField.SetValue(e, 0f);
        }

        private static void InvokeDirectorUpdate(AreaAccumulationDirector director) =>
            DirectorUpdateMethod.Invoke(director, null);

        /// <summary>Same reasoning as MV828ReplicatorAreaIndexTests.RegisterAllUnregisteredRobots — a
        /// garrison robot is built via plain GameObject.CreatePrimitive + AddComponent, which never fires
        /// OnEnable outside Play mode, so <see cref="RobotEnemy.Active"/> (what Replicator.NearestEligible
        /// itself scans) silently stays empty for every real garrison placement in this test unless
        /// something registers it directly.</summary>
        private static void RegisterAllUnregisteredRobots()
        {
            var list = (List<RobotEnemy>)RobotActiveListField.GetValue(null);
            foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
                if (!list.Contains(r)) list.Add(r);
        }

        /// <summary>The ticket's own required dump shape: "per box: in / cap / queue count / eligible
        /// count; per queued robot: state, distance to its slot, seconds stalled".</summary>
        private static string DumpState(IReadOnlyList<Replicator> boxes)
        {
            var sb = new StringBuilder();
            foreach (Replicator box in boxes)
            {
                int emitted = box.GetComponent<EnemySpawner>().Emitted;
                int eligible = RobotEnemy.Active.Count(r => Replicator.IsEligibleFor(r, box.AreaIndex));
                sb.AppendLine($"box={box.Id} in={(box.PlayerInArea ? 1 : 0)} cap={box.Capacity} " +
                              $"q={box.QueueCount}/{Replicator.MaxQueueSlots} elig={eligible} emitted={emitted}");

                var queue = (List<RobotEnemy>)QueueField.GetValue(box);
                for (int i = 0; i < queue.Count; i++)
                {
                    RobotEnemy r = queue[i];
                    if (r == null) continue;
                    Vector3 slot = box.QueueSlotPosition(i);
                    Vector3 d = r.transform.position - slot; d.y = 0f;
                    float stalled = (float)SeekStallTimerField.GetValue(r);
                    sb.AppendLine($"    slot{i}: state={r.Current} distToSlot={d.magnitude:F2}m stalled={stalled:F2}s");
                }
            }
            return sb.ToString();
        }

        [Test]
        public void StandingInA13For60Seconds_AtLeast10Of12ReplicatorsTakeInARobotAndEmitTwins()
        {
            const float dt = 1f / 60f;
            const int frames = 3600; // MV-1066's own "tick 60 s of simulated time"

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            WorldArea a13 = cfg.AreaByIndex(13);
            Assert.IsNotNull(a13, "setup failure: World 2 must still author area 13");

            _host = new GameObject("MV1066 Host");
            MapBuild built = MapRuntime.Build(map, _host.transform);

            // Same "isolate garrison placement from ambient top-up" idiom as MV1027/MV1002/MV559 — set
            // BEFORE Configure (not after): Configure constructs this director's own AreaSpawnQueue off
            // DevTuning.MaxActiveRobots AT THAT MOMENT, caching the default if this line ran too late.
            DevTuning.MaxActiveRobots = 0f;

            _areaGo = new GameObject("Area Accumulation");
            var director = _areaGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, built.Cover); // auto-fills area1

            // Stamps real AreaIndex/Id/spawner composition onto every built Replicator (MV-828) — without
            // this every Replicator resolves AreaIndex 0 and NearestEligible matches nothing.
            var runner = _host.AddComponent<WorldRunner>();
            runner.Configure(cfg, map, built, director);

            // AddComponent's own Awake never runs outside Play mode (Replicator.Build's own doc comment) —
            // call it directly, same as every other Replicator EditMode test in this suite.
            List<Replicator> a13Reps = built.Replicators.Where(r => r.AreaIndex == 13).ToList();
            Assert.AreEqual(12, a13Reps.Count,
                "setup failure: a13 must still author its own 12 Replicators for this reproduction to mean anything");
            foreach (Replicator box in a13Reps)
            {
                box.Build();
                FactoryExitZones.Register(box);
                // The real Start()-time wiring (never run automatically in EditMode) — the actual event
                // AreaAccumulationDirector fires, not a direct OnAreaEntered call.
                director.PlayerCrossedIntoArea += box.OnAreaEntered;
            }

            _playerGo = new GameObject("Player") { tag = "Player" };

            // --- walk the real route: area1 .. area12 current, then cross into a13 through g30 ---
            for (int index = 2; index <= 13; index++)
            {
                _playerGo.transform.position = index == 13 ? A13WestEntry : ProbePositionFor(map.Zone($"area{index}"));
                InvokeDirectorUpdate(director);
                director.EnterArea(index);
            }
            Assert.That(director.PhysicalArea, Is.EqualTo(13), "precondition: Max is physically standing in a13");
            Assert.That(director.CurrentArea, Is.EqualTo(13), "precondition: a13's own gate must have broken");

            int seedCount = Garrison.SeedCount(13, cfg);
            Garrison.Seed[] slots = Garrison.SeedSlots(a13, seedCount, cfg);
            Assert.That(slots.Length, Is.GreaterThan(0), "setup failure: a13 must author a non-zero garrison");

            RobotEnemy[] garrison = Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(r => r.AreaIndex == 13).ToArray();
            Assert.That(garrison.Length, Is.EqualTo(slots.Length),
                "setup failure: a13's own PlacePendingGarrison head start must have placed its full authored roster");

            // Without this, NearestEligible's own scan of RobotEnemy.Active sees nobody — see this
            // class's own doc comment on RegisterAllUnregisteredRobots.
            RegisterAllUnregisteredRobots();

            // --- tick 60 real simulated seconds: every a13 robot's own Tick, every a13 box's own
            // TickConsumption, never a shortcut that bypasses either ---
            for (int f = 0; f < frames; f++)
            {
                IReadOnlyList<RobotEnemy> active = RobotEnemy.Active;
                for (int i = 0; i < active.Count; i++)
                {
                    RobotEnemy r = active[i];
                    if (r != null && r.IsAlive && r.AreaIndex == 13) InvokeTick(r, dt);
                }

                foreach (Replicator box in a13Reps)
                    if (box.IsAlive) box.TickConsumption(dt);
            }

            int boxesWithTwins = a13Reps.Count(box => box.GetComponent<EnemySpawner>().Emitted >= 2);

            Assert.That(boxesWithTwins, Is.GreaterThanOrEqualTo(10),
                $"MV-1066: only {boxesWithTwins}/12 of a13's Replicators took in a robot and emitted its twins " +
                $"after 60s of Max standing at its west entry — Lee's own report (\"robots stand beside " +
                $"unblocked ramps and never move\") must reproduce here before any fix ships.\n{DumpState(a13Reps)}");
        }
    }
}
