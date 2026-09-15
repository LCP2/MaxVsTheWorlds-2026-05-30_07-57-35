using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-809 — a Replicator at the global robot budget ceiling used to consume a robot and emit
    /// nothing: <see cref="EnemySpawner.SpawnExact"/>'s loop silently spawns 0 when the field is full,
    /// but <see cref="Replicator.TickConsumption"/> despawned the intake robot and burned capacity
    /// unconditionally regardless. One robot in, none out, one capacity spent — a shredder.
    ///
    /// Fails on base commit f89c4e7: at the ceiling, <see cref="RobotEnemy.ActiveCount"/> falls by 1
    /// per cycle (the consumed robot is never replaced) and <see cref="Replicator.Capacity"/> still
    /// drops. Quoted in this ticket's fix comment.
    ///
    /// Tier 2 (resolved values): every assertion reads <see cref="RobotEnemy.ActiveCount"/> or
    /// <see cref="Replicator.Capacity"/> — both live, engine-resolved state, never an authored constant
    /// asserted back at itself.
    /// </summary>
    public sealed class MV809ReplicatorNeverShredsTests
    {
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnDisableMethod =
            typeof(RobotEnemy).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo ActiveField =
            typeof(RobotEnemy).GetField("_active", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo LiveField =
            typeof(EnemySpawner).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
        }

        // --- Construction: same reflection idiom MV775ReplicatorStagingTests/MV795FloodRobotImmunityTests
        // already use for a RobotEnemy that has no other way to reach RobotEnemy.ActiveCount in EditMode
        // — AddComponent never fires Awake/OnEnable as a side effect outside Play mode here. ---

        private RobotEnemy NewLiveRobot(Vector3 position)
        {
            var go = new GameObject("MV809-robot");
            _spawned.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            OnEnableMethod.Invoke(e, null);
            e.transform.position = position;
            return e;
        }

        private Replicator NewReplicator(Vector3 position, int capacity)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "MV809-replicator";
            _spawned.Add(go);
            go.transform.position = position;
            go.transform.localScale = new Vector3(2f, 1.5f, 2f);
            var replicator = go.AddComponent<Replicator>();
            replicator.Build();
            replicator.Configure(capacity);
            return replicator;
        }

        /// <summary>Registers any robot <paramref name="spawner"/> has actually emitted (its own
        /// <c>_live</c>, populated directly by <c>SpawnKind</c> regardless of Unity lifecycle quirks)
        /// but that <see cref="RobotEnemy.ActiveCount"/> doesn't know about yet — the mirror image of
        /// the OnEnable workaround above: <c>SpawnKind</c>'s own <c>SetActive(true)</c> doesn't
        /// synchronously fire OnEnable here either. Adds directly to the registry rather than invoking
        /// OnEnable (which would also re-run ResetState and stomp the Emerging state SpawnKind just
        /// set) — this test only needs the count to be right.</summary>
        private static void SyncNewlyLiveRobots(EnemySpawner spawner)
        {
            var live = (List<RobotEnemy>)LiveField.GetValue(spawner);
            var active = (List<RobotEnemy>)ActiveField.GetValue(null);
            foreach (RobotEnemy e in live)
                if (!active.Contains(e)) active.Add(e);
        }

        /// <summary>Lures <paramref name="robot"/>, snaps it to the arrive gate (same staging
        /// MV775ReplicatorStagingTests uses), then drives Intake through to the end of Output — one
        /// full Replicator cycle, end to end.</summary>
        private void RunOneFullCycle(Replicator replicator, EnemySpawner spawner, RobotEnemy robot)
        {
            replicator.TickLure();
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, robot.Current,
                "setup failure: the robot must be lured before this helper can drive a cycle");

            // MV-807: arrival is measured against the robot's own queue slot (slot 0), not a
            // hand-computed hatch-relative offset — same staging change ReplicatorTests and
            // MV775ReplicatorStagingTests make.
            robot.transform.position = robot.ReplicatorSeekTarget;

            replicator.TickConsumption(Replicator.IntakeSeconds + 0.01f);
            Assert.IsFalse(robot.IsAlive, "setup failure: the robot must be despawned into the Cycle beat by now");
            // MV-809: Despawn()'s SetActive(false) doesn't synchronously fire OnDisable in EditMode
            // (the same quirk this suite already works around for OnEnable) — invoke it directly so
            // RobotEnemy.ActiveCount reflects the despawn exactly as it would within the SAME frame in
            // Play mode, which is what the reservation math under test actually depends on.
            OnDisableMethod.Invoke(robot, null);

            // Same staged dt sequence as MV775ReplicatorStagingTests: the Intake-completing call above
            // already ticks the freshly-added PendingEmission's own timer by its own dt (TickConsumption
            // runs the Intake beat and the pending-emission loop off the SAME dt in one call), so the
            // first Cycle-beat tick below is 2.0s - not CycleSeconds - to land just past the 3s mark
            // without also overshooting CycleSeconds + EmitStaggerSeconds in the same call. (MV-808
            // lengthened IntakeSeconds 0.5 -> 1.0, shifting that seeded starting point from ~0.51s to
            // ~1.01s, so this first jump is shortened by the same 0.5s to land back on the same marks.)
            replicator.TickConsumption(2.0f); // cumulative ~3.01s: past CycleSeconds - first emission fires
            // MV-809: sync BETWEEN the two emissions, not just once at the end — in Play mode the
            // first emitted robot's OnEnable fires synchronously, so the second emission's own
            // GlobalHasRoom check already sees it. Syncing only after both would let the second
            // emission see room the first one had already spent, which is the same population error
            // this fix exists to close, just relocated into the test harness instead of the game.
            SyncNewlyLiveRobots(spawner);
            replicator.TickConsumption(0.45f); // cumulative ~3.46s: past CycleSeconds + EmitStaggerSeconds - second emission attempt

            SyncNewlyLiveRobots(spawner);
        }

        [Test]
        public void ReplicatorNeverShreds_AtTheGlobalBudgetCeiling()
        {
            LogAssert.ignoreFailingMessages = true; // BuildBody's collider-strip [Error], same as every other Replicator test
            DevTuning.GlobalRobotBudget = 5f;
            DevTuning.StartingRobots = 10f; // keeps the per-factory cap from ever being the binding constraint here

            // --- One below the ceiling: a normal cycle still doubles the robot and spends capacity. ---
            for (int i = 0; i < 3; i++) NewLiveRobot(new Vector3(1000f + i, 0f, 1000f));
            Replicator belowReplicator = NewReplicator(new Vector3(2000f, 0f, 2000f), capacity: 2);
            RobotEnemy belowRobot = NewLiveRobot(belowReplicator.transform.position + new Vector3(2f, 0f, 0f));

            Assert.AreEqual(4, RobotEnemy.ActiveCount, "setup failure: field must sit at Budget-1 (4 of 5) before this cycle");
            int activeBefore = RobotEnemy.ActiveCount;
            RunOneFullCycle(belowReplicator, belowReplicator.GetComponent<EnemySpawner>(), belowRobot);

            Assert.GreaterOrEqual(RobotEnemy.ActiveCount, activeBefore,
                "a Replicator must never reduce the field's population");
            Assert.AreEqual(5, RobotEnemy.ActiveCount,
                "one below the ceiling, the cycle must emit the full doubled pair (net +1)");
            Assert.AreEqual(1, belowReplicator.Capacity,
                "a cycle that emits the full pair must spend exactly one capacity");

            // --- At the ceiling: the box must still give back at least what it took - never a shredder. ---
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            for (int i = 0; i < 4; i++) NewLiveRobot(new Vector3(3000f + i, 0f, 3000f));
            Replicator atReplicator = NewReplicator(new Vector3(4000f, 0f, 4000f), capacity: 2);
            RobotEnemy atRobot = NewLiveRobot(atReplicator.transform.position + new Vector3(2f, 0f, 0f));

            Assert.AreEqual(5, RobotEnemy.ActiveCount, "setup failure: field must sit exactly at the ceiling (5 of 5) before this cycle");
            activeBefore = RobotEnemy.ActiveCount;
            RunOneFullCycle(atReplicator, atReplicator.GetComponent<EnemySpawner>(), atRobot);

            Assert.GreaterOrEqual(RobotEnemy.ActiveCount, activeBefore,
                "a Replicator must never reduce the field's population, even right at the ceiling");
            Assert.AreEqual(activeBefore, RobotEnemy.ActiveCount,
                "at the ceiling the box can give back at most a straight replacement - the field must neither shrink nor grow");
            Assert.AreEqual(2, atReplicator.Capacity,
                "a cycle that could only manage the guaranteed replacement (never 0) must not burn capacity");
        }
    }
}
