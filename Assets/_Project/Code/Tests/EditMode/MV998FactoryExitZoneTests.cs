using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-998 — Lee's own TestFlight observation: robots stand still right at a factory's exit (a
    /// hutch's own doorway/mouth fan, a Replicator's ramp), so the next one out can never clear the
    /// doorway either — the source reads as switched off even though it is still alive. Fixed by never
    /// letting a robot come to rest inside a factory's own exit zone: a spawned robot musters past the
    /// mouth before going Dormant (<see cref="EnemySpawner"/>'s own muster-point hand-off to
    /// <see cref="RobotEnemy"/>'s new <c>Mustering</c> state — Change 1), and
    /// <see cref="RobotEnemy.BeginDormant"/>'s own zone gate plus <see cref="RobotEnemy.CentralWakeCheck"/>'s
    /// safety sweep (Change 2/3) catch every other path a robot could settle back into a zone from,
    /// including a Replicator twin resting on its own ramp.
    ///
    /// A real <see cref="MowerHutch"/>/<see cref="EnemySpawner"/> pair and a real
    /// <see cref="Replicator"/>, built the same isolated, un-obstructed way
    /// <c>EnemySpawnerTests</c>/<c>ReplicatorTests</c> already build one (bare primitive +
    /// <c>Build()</c> — Awake/OnEnable never run as a side effect of AddComponent outside Play mode, so
    /// both are invoked directly): deliberately NOT embedded in a full built level. What this test
    /// proves is the robot's OWN emergence/rest state machine — given clear ground to muster onto, does
    /// it ever come to rest back in the doorway — not whether some unrelated area's authored cover
    /// happens to sit close enough to block a straight walk, which is a level-design/pathing question
    /// this ticket's own scope excludes ("Do not re-raise").
    ///
    /// ONE test (testing policy MV-465, Rule 1), covering both halves the ticket's own AC1 names — a
    /// hutch proving Change 1 (emergence never rests in the doorway it just walked out of) and a
    /// Replicator proving Change 2/3 (a robot resting on its own out-ramp foot is walked clear by the
    /// very next wake-check pass) — not two independent regressions, the same "no robot may come to
    /// rest inside a factory's exit zone" rule proven against both factory kinds the ticket names.
    ///
    /// Must fail on base commit a024d21: <see cref="RobotEnemy.State.Mustering"/>,
    /// <see cref="EnemySpawner.ExitZoneContains"/> and <see cref="Replicator.ExitZoneContains"/> did not
    /// exist there, so this fails to COMPILE on that commit — the same "fails on the base commit" the
    /// project's testing policy accepts.
    ///
    /// Tier 2 (resolved values) throughout: every assertion reads a robot's own settled
    /// <c>transform.position</c>/<c>Current</c> after real emergence/mustering ticks, never an authored
    /// constant.
    /// </summary>
    public sealed class MV998FactoryExitZoneTests
    {
        private static readonly MethodInfo SpawnKindMethod =
            typeof(EnemySpawner).GetMethod("SpawnKind", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo TickMethod =
            typeof(RobotEnemy).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo VerticalVelField =
            typeof(RobotEnemy).GetField("_verticalVel", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _hutchGo;
        private GameObject _replicatorGo;
        private GameObject _loneRobotGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            FactoryExitZones.ResetForTests();
            DormantWakeScheduler.ResetForTests();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hutchGo != null) Object.DestroyImmediate(_hutchGo);
            if (_replicatorGo != null) Object.DestroyImmediate(_replicatorGo);
            if (_loneRobotGo != null) Object.DestroyImmediate(_loneRobotGo);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            FactoryExitZones.ResetForTests();
            DormantWakeScheduler.ResetForTests();
            DevTuning.Reset();
        }

        /// <summary>Ticks the robot, then zeroes its own accumulated fall speed. This fixture has no
        /// real level floor under it (deliberately — see the class doc comment), so
        /// <c>CharacterController.isGrounded</c> never trips true and gravity would otherwise
        /// accumulate without bound across hundreds of ticks, oversized-Move-splitting almost the whole
        /// frame budget into the vertical component and starving FaceAndMove's own lateral walk — a
        /// batch-mode/no-floor artifact, not anything this ticket's own XZ-only exit-zone geometry
        /// (<see cref="MaxWorlds.Enemies.FactoryExitZone"/> flattens Y away entirely) cares about.</summary>
        private static void InvokeTick(RobotEnemy e, float dt)
        {
            TickMethod.Invoke(e, new object[] { dt });
            VerticalVelField.SetValue(e, 0f);
        }

        [Test]
        public void NoRobotRestsInsideAFactorysExitZone_HutchEmergenceAndReplicatorRamp()
        {
            const float dt = 1f / 60f;
            const int maxExtraFrames = 1800; // 30 simulated seconds — well past RobotEnemy.MusterTimeout (8s) each

            // ==== Part 1 (Change 1): a real hutch — 8 robots spawned in sequence must all complete
            // emergence and come to rest OUTSIDE the doorway/mouth they walked out of, never parked in
            // it blocking the next one out. ============================================================
            _hutchGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _hutchGo.name = "MV998 Mower Hutch";
            _hutchGo.transform.position = new Vector3(0f, 1f, 15f);
            MowerHutch hutch = _hutchGo.AddComponent<MowerHutch>();
            hutch.Build(); // Awake never runs as a side effect of AddComponent outside Play mode (MowerHutch's own doc)
            EnemySpawner spawner = _hutchGo.GetComponent<EnemySpawner>();
            Assert.IsNotNull(spawner, "setup failure: a real hutch must carry its own EnemySpawner");
            FactoryExitZones.Register(spawner); // same "OnEnable doesn't fire from AddComponent" reasoning

            const int robotCount = 8;
            var robots = new RobotEnemy[robotCount];
            for (int i = 0; i < robotCount; i++)
            {
                robots[i] = (RobotEnemy)SpawnKindMethod.Invoke(spawner, new object[] { EnemyKind.Rusher, false });
                Assert.AreEqual(RobotEnemy.State.Emerging, robots[i].Current,
                    $"setup failure: robot {i} must spawn straight into Emerging");

                // Tick a beat before the next one spawns, same "in sequence" cadence a real production
                // stream emits under, not all 8 dropped on the same frame.
                for (int f = 0; f < 20; f++)
                    foreach (RobotEnemy r in robots) if (r != null) InvokeTick(r, dt);
            }

            // Tick until every one of them has settled Dormant, bounded so a genuine stall fails loudly
            // instead of hanging cc-verify.
            int frame = 0;
            bool allDormant;
            do
            {
                foreach (RobotEnemy r in robots) InvokeTick(r, dt);
                frame++;
                allDormant = true;
                foreach (RobotEnemy r in robots) if (r.Current != RobotEnemy.State.Dormant) allDormant = false;
            } while (!allDormant && frame < maxExtraFrames);

            for (int i = 0; i < robotCount; i++)
            {
                Assert.AreEqual(RobotEnemy.State.Dormant, robots[i].Current,
                    $"MV-998: robot {i} must complete emergence and come to rest — none may time out stuck " +
                    "mid-walk forever");
                Assert.IsFalse(spawner.ExitZoneContains(robots[i].transform.position),
                    $"MV-998: robot {i}'s RESOLVED rest position {robots[i].transform.position} lies inside " +
                    "the hutch's own exit zone — it would block the next robot out");
            }

            // ==== Part 2 (Change 2/3): a robot resting on a Replicator's out-ramp foot — the very next
            // wake-check pass must walk it clear rather than leave it parked on the ramp. ==============
            _replicatorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _replicatorGo.name = "MV998 Replicator";
            _replicatorGo.transform.position = new Vector3(80f, 1f, 15f); // clear of the hutch above
            _replicatorGo.transform.localScale = new Vector3(2f, 1.5f, 2f); // the ticket's authored footprint
            Replicator replicator = _replicatorGo.AddComponent<Replicator>();
            replicator.Build(); // same "Awake never fires from AddComponent outside Play mode" reasoning

            // Temporarily unregistered: BeginDormant's own zone gate would otherwise re-route this
            // placement immediately, and the point of this half is "a robot that IS resting on the ramp"
            // (matching the ticket's own worded scenario) — proving the SWEEP walks it clear, not the
            // entry gate.
            _loneRobotGo = new GameObject("MV998-replicator-twin");
            var cc = _loneRobotGo.AddComponent<CharacterController>();
            var lone = _loneRobotGo.AddComponent<RobotEnemy>();
            CcField.SetValue(lone, cc);
            OnEnableMethod.Invoke(lone, null); // same reflection idiom ReplicatorTests.NewRusher already uses
            lone.ResetState();
            lone.transform.position = replicator.OutRampFootPosition;
            lone.BeginDormant();
            Assert.AreEqual(RobotEnemy.State.Dormant, lone.Current,
                "setup failure: the twin must actually be resting on the ramp for this half to mean anything");
            Assert.IsTrue(replicator.ExitZoneContains(lone.transform.position),
                "setup failure: the out-ramp foot must itself be inside the Replicator's own exit zone");

            FactoryExitZones.Register(replicator);

            // One scheduler pass — CentralWakeCheck is the exact method DormantWakeScheduler.Tick calls
            // for every Dormant robot each pass, so this exercises Change 3 directly.
            lone.CentralWakeCheck(DormantWakeScheduler.TickInterval);
            Assert.AreEqual(RobotEnemy.State.Mustering, lone.Current,
                "MV-998: the very next wake-check pass must start walking a ramp-resting robot clear");

            frame = 0;
            while (lone.Current != RobotEnemy.State.Dormant && frame < maxExtraFrames)
            {
                InvokeTick(lone, dt);
                frame++;
            }

            Assert.AreEqual(RobotEnemy.State.Dormant, lone.Current,
                "MV-998: the twin must actually finish mustering and come back to rest, not wander forever");
            Assert.IsFalse(replicator.ExitZoneContains(lone.transform.position),
                $"MV-998: the twin's RESOLVED rest position {lone.transform.position} still lies inside the " +
                "Replicator's own exit zone");
        }
    }
}
