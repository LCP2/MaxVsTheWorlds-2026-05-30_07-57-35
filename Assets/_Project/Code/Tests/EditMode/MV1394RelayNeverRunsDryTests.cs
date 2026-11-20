using System.Collections;
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
    /// MV-1394 — relays never run dry. A relay authored with capacity 1 used to go spent after one
    /// full pair; now it keeps cycling forever (bounded only by the field-wide budget), and the second
    /// twin of each pair goes straight to the back of the relay's own queue. Fails on 227c289: after
    /// the first cycle the box is spent, so the second twin is NOT queued (0 assigned) and no further
    /// cycle ever completes. Tier 2: reads resolved spawner emission counts and each twin's resolved
    /// <see cref="RobotEnemy.IsAssignedToReplicator"/> / <see cref="RobotEnemy.NoReplicate"/>.
    /// </summary>
    public sealed class MV1394RelayNeverRunsDryTests
    {
        private static readonly Vector3 RigOrigin = new Vector3(-61877f, 0f, 29314f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
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

        [Test]
        public void RelayWithCapacity1_CyclesTenTimes_EmitsTwentyTwins_AndRequeuesTheSecondTwin()
        {
            LogAssert.ignoreFailingMessages = true; // same BuildBody collider-strip [Error] every Replicator test carries
            DevTuning.GlobalRobotBudget = 100f;     // the field is nowhere near its budget in this test

            var boxGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(boxGo);
            boxGo.transform.position = RigOrigin;
            var box = boxGo.AddComponent<Replicator>();
            box.Build();
            box.Configure(1);
            box.SetAreaIndex(1);

            var robotGo = new GameObject("MV1394-Rusher");
            _spawned.Add(robotGo);
            var cc = robotGo.AddComponent<CharacterController>();
            RobotEnemy head = robotGo.AddComponent<RobotEnemy>();
            CcField.SetValue(head, cc);
            head.Apply(EnemyArchetype.Rusher);
            OnEnableMethod.Invoke(head, null);
            head.transform.position = RigOrigin + new Vector3(3f, 0f, 0f);
            head.SetAreaIndex(1);

            EnemySpawner spawner = boxGo.GetComponent<EnemySpawner>();
            var live = (IList)LiveField.GetValue(spawner);

            box.OnAreaEntered(1); // Max is in the area: the lone robot is queued
            Assert.IsTrue(head.IsAssignedToReplicator, "setup failure: the lone robot must be queued");

            for (int cycle = 1; cycle <= 10; cycle++)
            {
                head.transform.position = head.ReplicatorSeekTarget; // arrives at slot 0 -> Intake
                int guard = 0;
                while (spawner.Emitted < cycle * 2 && guard++ < 2000) box.TickConsumption(0.02f);
                Assert.AreEqual(cycle * 2, spawner.Emitted, $"cycle {cycle}: the pair must emerge in full (box must not be spent)");

                RobotEnemy queued = null;
                int queuedCount = 0;
                foreach (RobotEnemy r in live)
                {
                    if (!r.IsAssignedToReplicator) continue;
                    queued = r; queuedCount++;
                }
                Assert.AreEqual(1, queuedCount, $"cycle {cycle}: exactly the second twin must be in the relay's queue");
                Assert.IsFalse(queued.NoReplicate, $"cycle {cycle}: a re-queued twin carries no NoReplicate tag");
                head = queued;
            }

            Assert.AreEqual(20, spawner.Emitted, "10 completed cycles must have emitted 20 twins");
        }
    }
}
