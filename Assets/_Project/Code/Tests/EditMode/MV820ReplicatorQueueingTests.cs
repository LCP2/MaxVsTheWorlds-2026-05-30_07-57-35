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
    /// MV-820 — Lee's rules (stated 2026-09-16): on area entry, the two robots nearest each live
    /// Replicator in that area stop what they're doing and queue at it; the instant a replication
    /// completes, the next nearest robot goes; everyone else attacks Max; a queued robot moves at
    /// attack speed and is never cancelled for proximity/sight/damage. Build 302e10e contradicted all
    /// of this: a 16 m radius, a 7 m melee exclusion, a Chase/Search-only state gate, registry-order
    /// (not nearest) selection, a 25% field-wide seeking ceiling, a one-robot-when-empty queue cap, and
    /// a 6 s give-up timeout all still stood, and Intake itself refused a robot outright once the field
    /// was at or over its budget — so this test fails on 302e10e: (a) a registry-order (not nearest)
    /// scan lures the wrong pair and, capped at one robot for an empty queue, never lures a second at
    /// all in a single pass; (b) placed 3 m from Max with sight, the melee-exclusion/lungeRange cancel
    /// fires and the robot leaves ReplicatorSeeking well before 5 s; (c) a Charger seeks at its ordinary
    /// chase speed (1.6 m/s), not its 9 m/s charge lungeSpeed, so displacement over 1 s falls far short
    /// of 8.5 m; (d) the empty-queue-cap (MV-811 change 4) means a promoted robot doesn't refill on the
    /// very tick that opened the slot; (e) at or over <c>GlobalMaxLiveEnemies</c>,
    /// <c>HasRoomForReplicatorIntake</c> refuses the robot at slot 0 outright and no twin ever emits.
    ///
    /// Tier 2 (resolved values) throughout: every assertion reads a robot's resolved
    /// <see cref="RobotEnemy.Current"/>, a resolved <see cref="Transform.position"/> displacement, or a
    /// resolved <see cref="EnemySpawner.LiveCountOf"/> — never an authored constant asserted back at
    /// itself, never a mere presence check.
    /// </summary>
    public sealed class MV820ReplicatorQueueingTests
    {
        // Same "distinctive far-off origin" idiom every other Replicator EditMode test in this suite
        // uses — EditMode tests share one physics scene for the whole cc-verify run with no per-test
        // reset. Each sub-scenario below gets its own origin, well clear of the others.
        private static readonly Vector3 RigOriginA = new Vector3(-33019f, 0f, 71442f);
        private static readonly Vector3 RigOriginB = RigOriginA + new Vector3(3000f, 0f, 0f);
        private static readonly Vector3 RigOriginC = RigOriginA + new Vector3(6000f, 0f, 0f);
        private static readonly Vector3 RigOriginD = RigOriginA + new Vector3(9000f, 0f, 0f);
        private static readonly Vector3 RigOriginE = RigOriginA + new Vector3(12000f, 0f, 0f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo TickReplicatorSeekingMethod =
            typeof(RobotEnemy).GetMethod("TickReplicatorSeeking", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private GameObject _playerGo;

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
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
        }

        private RobotEnemy NewRobot(in EnemyArchetype archetype, Vector3 position, int area)
        {
            var go = new GameObject($"MV820-{archetype.Kind}");
            _spawned.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            // OnEnable never runs as a side effect of AddComponent outside Play mode — same quirk every
            // other Replicator/RobotEnemy EditMode test in this suite works around.
            CcField.SetValue(e, cc);
            e.Apply(archetype);
            OnEnableMethod.Invoke(e, null);
            e.transform.position = position;
            e.SetAreaIndex(area);
            return e;
        }

        private Replicator NewBox(Vector3 position, int area, int capacity)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(go);
            go.transform.position = position;
            var box = go.AddComponent<Replicator>();
            box.Build(); // AddComponent's own Awake never runs outside Play mode
            box.Configure(capacity);
            box.SetAreaIndex(area);
            return box;
        }

        [Test]
        public void ReplicatorQueueing_AssignsNearestTwoOnAreaEntry_StaysCommitted_SeeksAtAttackSpeed_RefillsSameTick_AndIgnoresTheGlobalBudget()
        {
            LogAssert.ignoreFailingMessages = true; // same BuildBody collider-strip [Error] every Replicator test carries

            // === (a) On the area-entry signal, exactly the 2 nearest of 6 robots at known distances ===
            // === become ReplicatorSeeking; the other 4 do not. ===
            Replicator boxA = NewBox(RigOriginA, area: 1, capacity: 2);
            var robotsA = new RobotEnemy[6];
            for (int i = 0; i < 6; i++)
                robotsA[i] = NewRobot(EnemyArchetype.Rusher, RigOriginA + new Vector3(3f + i, 0f, 0f), area: 1);

            boxA.OnAreaEntered(1);

            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, robotsA[0].Current,
                "the nearest robot (3 m) must be assigned on area entry (MV-820 R1)");
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, robotsA[1].Current,
                "the second-nearest robot (4 m) must be assigned on area entry (MV-820 R1)");
            for (int i = 2; i < 6; i++)
                Assert.AreNotEqual(RobotEnemy.State.ReplicatorSeeking, robotsA[i].Current,
                    $"robot {i} ({3 + i} m) is not one of the 2 nearest and must not be assigned");

            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();

            // === (b) An assignee 3 m from Max, in sight, must stay ReplicatorSeeking for 5 s of ticks ===
            // === — no melee-exclusion or sight cancel, no timeout give-up (MV-820 R3). ===
            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = RigOriginB;

            Replicator boxB = NewBox(RigOriginB + new Vector3(0f, 0f, 20f), area: 1, capacity: 2);
            RobotEnemy assigneeB = NewRobot(EnemyArchetype.Rusher, RigOriginB + new Vector3(0f, 0f, 20f) + new Vector3(5f, 0f, 0f), area: 1);
            boxB.OnAreaEntered(1);
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, assigneeB.Current,
                "setup failure: this robot must be assigned before commitment can be tested");

            assigneeB.transform.position = RigOriginB + new Vector3(3f, 0f, 0f); // 3 m from Max
            const float dtB = 0.1f;
            for (int i = 0; i < 50; i++) // 50 x 0.1 s = 5 s
            {
                assigneeB.Sight.Tick(true, _playerGo.transform.position, dtB);
                TickReplicatorSeekingMethod.Invoke(assigneeB, new object[] { dtB });
                Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, assigneeB.Current,
                    $"an assignee 3 m from Max with sight must never cancel (MV-820 R3), step {i}");
            }

            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();

            // === (c) A Charger assignee's displacement while seeking must be >= 8.5 m over 1 real ===
            // === second (no sludge) — it seeks at its 9 m/s charge lungeSpeed, not its 1.6 m/s chase ===
            // === speed (MV-820 R4). ===
            RobotEnemy chargerC = NewRobot(EnemyArchetype.Charger, RigOriginC, area: 1);
            chargerC.SeekReplicator(RigOriginC + new Vector3(50f, 0f, 0f)); // far enough it never arrives mid-test
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, chargerC.Current,
                "setup failure: the Charger must be seeking before its seek speed can be measured");

            Vector3 startC = chargerC.transform.position;
            const float dtC = 0.05f;
            for (int i = 0; i < 20; i++) // 20 x 0.05 s = 1 s
                TickReplicatorSeekingMethod.Invoke(chargerC, new object[] { dtC });

            float displacementC = Vector3.Distance(chargerC.transform.position, startC);
            Assert.GreaterOrEqual(displacementC, 8.5f,
                $"a Charger assignee must cover at least 8.5 m in 1 s while seeking (its own 9 m/s charge " +
                $"lungeSpeed), got {displacementC:F2} m — it must not be seeking at its ordinary chase speed");

            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();

            // === (d) The instant the slot-0 robot is taken into Intake, the next nearest non-assigned ===
            // === robot becomes ReplicatorSeeking within the SAME TickConsumption tick (MV-820 R2). ===
            Replicator boxD = NewBox(RigOriginD, area: 1, capacity: 2);
            RobotEnemy slot0D = NewRobot(EnemyArchetype.Rusher, RigOriginD + new Vector3(3f, 0f, 0f), area: 1);
            RobotEnemy slot1D = NewRobot(EnemyArchetype.Rusher, RigOriginD + new Vector3(4f, 0f, 0f), area: 1);
            RobotEnemy refillD = NewRobot(EnemyArchetype.Rusher, RigOriginD + new Vector3(5f, 0f, 0f), area: 1);

            boxD.OnAreaEntered(1);
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, slot0D.Current, "setup failure: nearest must be assigned to slot 0");
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, slot1D.Current, "setup failure: second-nearest must be assigned to slot 1");
            Assert.AreNotEqual(RobotEnemy.State.ReplicatorSeeking, refillD.Current,
                "setup failure: with a full 2-deep queue, the third-nearest must not be assigned yet");

            slot0D.transform.position = slot0D.ReplicatorSeekTarget; // at slot 0 — Intake begins this tick
            // MV-823 rebuilt the draw-in as a walk-speed-derived AnimSequence rather than a flat
            // IntakeSeconds lerp, so this polls to the despawn rather than assuming a fixed duration.
            int guardD = 0;
            while (slot0D.IsAlive && guardD++ < 300) boxD.TickConsumption(0.02f);

            Assert.IsFalse(slot0D.IsAlive, "setup failure: slot 0 must have been taken into Intake and despawned");
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, refillD.Current,
                "the next-nearest non-assigned robot must be assigned within the SAME tick Intake takes the head (MV-820 R2)");

            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();

            // === (e) With RobotEnemy.ActiveCount above GlobalMaxLiveEnemies, the slot-0 robot is still ===
            // === taken into Intake and one twin still emits (MV-820 Change 6). ===
            DevTuning.GlobalRobotBudget = 3f;
            for (int i = 0; i < 4; i++)
                NewRobot(EnemyArchetype.Rusher, RigOriginE + new Vector3(0f, 0f, 500f + i), area: 0); // padding, unrelated area
            Assert.Greater(RobotEnemy.ActiveCount, EnemySpawner.GlobalMaxLiveEnemies,
                "setup failure: the field must already sit above the global budget before this box acts");

            Replicator boxE = NewBox(RigOriginE, area: 1, capacity: 1);
            RobotEnemy robotE = NewRobot(EnemyArchetype.Rusher, RigOriginE + new Vector3(3f, 0f, 0f), area: 1);
            boxE.OnAreaEntered(1);
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, robotE.Current, "setup failure: this robot must be assigned");

            robotE.transform.position = robotE.ReplicatorSeekTarget;
            EnemySpawner spawnerE = boxE.GetComponent<EnemySpawner>();
            // MV-823 rebuilt the draw-in as a walk-speed-derived AnimSequence rather than a flat
            // IntakeSeconds lerp, so this polls to the despawn rather than assuming a fixed duration.
            int guardE = 0;
            while (robotE.IsAlive && guardE++ < 300) boxE.TickConsumption(0.02f);
            Assert.IsFalse(robotE.IsAlive,
                "over budget, the slot-0 robot must still be taken into Intake and despawned (MV-820 Change 6)");

            // Polled rather than a fixed dt sum — MV-823 changed CycleSeconds 0.9 -> 2.0.
            guardE = 0;
            while (spawnerE.LiveCountOf(EnemyKind.Rusher) < 1 && guardE++ < 300) boxE.TickConsumption(0.02f);
            Assert.AreEqual(1, spawnerE.LiveCountOf(EnemyKind.Rusher),
                "over budget, the guaranteed first twin must still emit (MV-820 Change 6) — it must never be dropped or deferred");
        }
    }
}
