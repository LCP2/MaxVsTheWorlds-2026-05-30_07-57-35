using System.Collections.Generic;
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
    /// MV-1488 - ONE test (MV-465 Rule 1), Tier 2 resolved values: the City's resolved aliveCap is 24; a
    /// relay whose area is above the cap keeps no queue (every robot released to a combat state); it
    /// re-lures within 1.5 s of room appearing and takes a robot in; and with cover across the straight
    /// queue line every slot is on open floor clear of cover and the head is still taken in.
    ///
    /// Fails on the base commit 6bcf297: the City's aliveCap resolves to 12, not 24.
    /// </summary>
    public sealed class MV1488RelayQueueHoldTests
    {
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);

        private const float Dt = 0.02f;
        private static readonly Vector3 RelayOrigin = new Vector3(91234f, 0f, -48765f);

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            DevTuning.Reset();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DifficultyDirector.Reset();
            CharacterControllerMotion.HasWarnedOversizedMove = true;
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            EnemyNavigation.Reset();
            DifficultyDirector.Reset();
            DevTuning.Reset();
        }

        private RobotEnemy NewRobot(Vector3 position)
        {
            var go = new GameObject("MV1488-robot");
            _spawned.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Rusher);
            OnEnableMethod.Invoke(e, null);
            e.transform.position = position;
            e.SetAreaIndex(3);
            return e;
        }

        private static bool InCombatState(RobotEnemy r) =>
            r.Current == RobotEnemy.State.Chase || r.Current == RobotEnemy.State.Telegraph
            || r.Current == RobotEnemy.State.Lunge || r.Current == RobotEnemy.State.Recover
            || r.Current == RobotEnemy.State.Search || r.Current == RobotEnemy.State.Alert;

        /// <summary>Ticks the relay, walking its queue head onto slot 0 each tick (robots are not ticked);
        /// true once any robot has been drawn in.</summary>
        private static bool RunUntilTaken(Replicator relay, List<RobotEnemy> robots, float seconds)
        {
            for (int i = 0; i < (int)(seconds / Dt); i++)
            {
                foreach (RobotEnemy r in robots)
                {
                    if (r == null || !r.IsAlive) return true;   // despawned into the box
                    if (r.IsBeingDrawnIn) return true;
                    if (r.Current == RobotEnemy.State.ReplicatorSeeking
                        && Vector3.Distance(r.ReplicatorSeekTarget, relay.QueueSlotPosition(0)) < 0.01f)
                        r.transform.position = r.ReplicatorSeekTarget;
                }
                relay.TickConsumption(Dt);
            }
            return false;
        }

        [Test]
        public void RelayHoldsNoQueueAtTheCap_ReLuresWithRoom_AndBendsAroundCover()
        {
            WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(city, "city_config must load");

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(ground);
            ground.transform.position = new Vector3(RelayOrigin.x, -1.5f, RelayOrigin.z);
            ground.transform.localScale = new Vector3(100f, 1f, 100f);

            var relayGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(relayGo);
            relayGo.transform.position = RelayOrigin;
            relayGo.transform.localScale = new Vector3(2f, 1.5f, 2f);
            var relay = relayGo.AddComponent<Replicator>();
            relay.Build();
            relay.Configure(2);
            relay.SetAreaIndex(3);
            EnemySpawner spawner = relay.GetComponent<EnemySpawner>();
            spawner.ConfigureWorldConfig(city);

            Assert.AreEqual(24, spawner.AliveCap, "the City's resolved aliveCap");

            // ---- 26 alive (above the cap) with Max in the area: nobody queues, everybody fights ----
            var robots = new List<RobotEnemy>();
            for (int i = 0; i < 26; i++) robots.Add(NewRobot(RelayOrigin + new Vector3(6f + i * 0.5f, 0f, 3f)));
            relay.OnAreaEntered(3);
            for (int i = 0; i < 150; i++) relay.TickConsumption(Dt);   // 3 s
            Assert.AreEqual(0, relay.QueueCount, "held at the cap: no queue");
            foreach (RobotEnemy r in robots)
            {
                Assert.IsFalse(r.IsAssignedToReplicator, "held at the cap: no robot assigned");
                Assert.IsTrue(InCombatState(r), $"held at the cap: robot must be in a combat state, was {r.Current}");
            }

            // ---- kill down to 22: a queue again within 1.5 s, and a robot taken in within 10 s ----
            for (int i = 0; i < 4; i++) { robots[i].Despawn(); }
            robots.RemoveRange(0, 4);
            Assert.AreEqual(22, relay.AliveInArea(), "setup failure: 22 alive");
            int t = 0;
            while (relay.QueueCount == 0 && t < 75) { relay.TickConsumption(Dt); t++; }
            Assert.Greater(relay.QueueCount, 0, "with room under the cap the relay must queue again within 1.5 s");
            Assert.IsTrue(RunUntilTaken(relay, robots, 10f), "and must take a robot in within 10 s");

            // ---- cover across the straight queue line: every slot on open floor, the head still goes in ----
            relay.OnAreaEntered(99);   // let go of everything, then start over
            relay.OnAreaEntered(3);
            Vector3 straight0 = relay.HatchPosition + (-relayGo.transform.forward).normalized * Replicator.QueueSlotSpacing;
            var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(cover);
            cover.transform.position = new Vector3(straight0.x, 0f, straight0.z);
            cover.transform.localScale = new Vector3(3f, 2f, 0.6f);
            CoverLayer.Assign(cover);
            Physics.SyncTransforms();

            Vector3 prev = relay.HatchPosition;
            for (int s = 0; s < Replicator.MaxQueueSlots; s++)
            {
                Vector3 p = relay.QueueSlotPosition(s);
                Assert.IsFalse(Physics.CheckSphere(new Vector3(p.x, 0.6f, p.z), 0.55f + 0.1f, CoverLayer.Mask, QueryTriggerInteraction.Ignore),
                    $"slot {s} must be clear of cover");
                Vector3 d = p - prev; d.y = 0f;
                Assert.AreEqual(Replicator.QueueSlotSpacing, d.magnitude, 0.05f, $"slot {s} spacing from the one before");
                prev = p;
            }
            // the head must be walked in: re-lure around the new slot positions
            relay.OnAreaEntered(99);
            relay.OnAreaEntered(3);
            Assert.IsTrue(RunUntilTaken(relay, robots, 10f), "with cover across the line the head must still be taken in within 10 s");
        }
    }
}
