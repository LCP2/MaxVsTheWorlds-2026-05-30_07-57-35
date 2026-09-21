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
    /// MV-872 — <see cref="Replicator.MaxQueueSlots"/> was hard-capped at 2 (MV-807): however many
    /// eligible robots stood at a box's own IN face, at most two of them ever animated toward it —
    /// the rest just stood there looking broken. Fails on base commit 68d962b, where MaxQueueSlots
    /// is still 2: with capacity 6 and eight eligible walking robots in the box's own area, driving
    /// <see cref="Replicator.OnAreaEntered"/> assigns only 2, not 6. Quoted in this ticket's fix
    /// comment.
    ///
    /// Tier 2 (resolved values) throughout: every assertion reads a robot's own resolved
    /// <see cref="RobotEnemy.IsAssignedToReplicator"/> and resolved <see cref="RobotEnemy.ReplicatorSeekTarget"/>
    /// — never <see cref="Replicator.MaxQueueSlots"/> itself, never a mere presence check.
    /// </summary>
    public sealed class MV872ReplicatorQueueDepthTests
    {
        // Same "distinctive far-off origin" idiom every other Replicator EditMode test uses — EditMode
        // tests share one physics scene for the whole cc-verify run with no per-test reset.
        private static readonly Vector3 RigOriginA = new Vector3(-46512f, 0f, 18823f);
        private static readonly Vector3 RigOriginB = RigOriginA + new Vector3(3000f, 0f, 0f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

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

        private RobotEnemy NewRobot(Vector3 position, int area)
        {
            var go = new GameObject("MV872-Rusher");
            _spawned.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            // OnEnable never runs as a side effect of AddComponent outside Play mode — same quirk every
            // other Replicator/RobotEnemy EditMode test in this suite works around.
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Rusher);
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
        public void ReplicatorQueueing_WithCapacity6AndEightEligible_AssignsExactlySixToDistinctSlots_CappedLowerByRemainingCapacity()
        {
            LogAssert.ignoreFailingMessages = true; // same BuildBody collider-strip [Error] every Replicator test carries

            // === capacity 6, eight eligible walking robots at known distinct distances: exactly the ===
            // === six nearest must be assigned, each to its own distinct QueueSlotPosition(0..5). ===
            Replicator boxA = NewBox(RigOriginA, area: 1, capacity: 6);
            var robotsA = new RobotEnemy[8];
            for (int i = 0; i < 8; i++)
                robotsA[i] = NewRobot(RigOriginA + new Vector3(3f + i, 0f, 0f), area: 1); // 3..10 m, nearest-first

            boxA.OnAreaEntered(1);

            var expectedSlots = new HashSet<Vector3>();
            for (int slot = 0; slot < 6; slot++) expectedSlots.Add(boxA.QueueSlotPosition(slot));

            var seenTargets = new HashSet<Vector3>();
            int assignedCountA = 0;
            for (int i = 0; i < 8; i++)
            {
                if (!robotsA[i].IsAssignedToReplicator) continue;
                assignedCountA++;
                Assert.IsTrue(expectedSlots.Contains(robotsA[i].ReplicatorSeekTarget),
                    $"robot {i}'s resolved seek target must be one of QueueSlotPosition(0..5), got {robotsA[i].ReplicatorSeekTarget}");
                Assert.IsTrue(seenTargets.Add(robotsA[i].ReplicatorSeekTarget),
                    $"robot {i} was steered to a slot another assigned robot already occupies — all six must be distinct");
            }
            Assert.AreEqual(6, assignedCountA,
                "with capacity 6 and eight eligible robots, exactly six must be assigned (MaxQueueSlots raised 2 -> 6)");

            for (int i = 0; i < 6; i++)
                Assert.IsTrue(robotsA[i].IsAssignedToReplicator, $"robot {i} ({3 + i} m) is among the 6 nearest and must be assigned");
            for (int i = 6; i < 8; i++)
                Assert.IsFalse(robotsA[i].IsAssignedToReplicator, $"robot {i} ({3 + i} m) is not among the 6 nearest and must not be assigned");

            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();

            // === Same eight-robot field, but capacity 3: the queue must never exceed the box's own ===
            // === remaining capacity, even though MaxQueueSlots (6) would otherwise allow more. ===
            Replicator boxB = NewBox(RigOriginB, area: 1, capacity: 3);
            var robotsB = new RobotEnemy[8];
            for (int i = 0; i < 8; i++)
                robotsB[i] = NewRobot(RigOriginB + new Vector3(3f + i, 0f, 0f), area: 1);

            boxB.OnAreaEntered(1);

            int assignedCountB = 0;
            for (int i = 0; i < 8; i++)
                if (robotsB[i].IsAssignedToReplicator) assignedCountB++;

            Assert.AreEqual(3, assignedCountB,
                "with capacity 3 (below MaxQueueSlots), the queue must be capped at the box's own remaining capacity, not MaxQueueSlots");
        }
    }
}
