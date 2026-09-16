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
    /// MV-812 — Lee, on build 9751529: "They take ages to replicate. Should be much quicker." and
    /// "When they do get there they don't often go inside." One robot occupied the machine for 4.4 s
    /// (<c>IntakeSeconds</c> 1.0 + <c>CycleSeconds</c> 3.0 + <c>EmitStaggerSeconds</c> 0.4), and
    /// <c>ArriveTolerance</c> was 0.35 m measured to the robot's own queue slot with no separation/
    /// avoidance in <c>RobotEnemy.TickReplicatorSeeking</c>, so a robot nudged off its slot never closed
    /// the last 35 cm and was never taken in. Fails on base commit 9751529: total occupancy is 4.4 s
    /// (over the 1.6 s ceiling below), a robot 0.8 m from its slot is never drawn into Intake, and a
    /// robot whose <see cref="CharacterController"/> can't move stays exactly where it started forever.
    ///
    /// Tier 2 (resolved values): every assertion reads elapsed simulated time, a resolved
    /// <see cref="RobotEnemy.IsAlive"/>/<see cref="EnemySpawner.LiveCountOf"/>, or a resolved
    /// <see cref="Transform.position"/> distance — never an authored constant asserted back at itself.
    /// </summary>
    public sealed class MV812ReplicatorThroughputTests
    {
        // Same "distinctive far-off origin" idiom every other Replicator EditMode test uses — EditMode
        // tests share one physics scene for the whole cc-verify run with no per-test reset.
        private static readonly Vector3 RigOrigin = new Vector3(-71305f, 0f, 42918f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo TickReplicatorSeekingMethod =
            typeof(RobotEnemy).GetMethod("TickReplicatorSeeking", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void Set(object o, string field, object value) =>
            o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(o, value);

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

        private RobotEnemy NewRobot(Vector3 position)
        {
            var go = new GameObject("MV812-robot");
            _spawned.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            // OnEnable never runs as a side effect of AddComponent outside Play mode (same quirk every
            // other Replicator/RobotEnemy EditMode test in this suite works around) — Replicator.TickLure
            // reads RobotEnemy.Active, which OnEnable is what populates.
            OnEnableMethod.Invoke(e, null);
            e.transform.position = position;
            return e;
        }

        private Replicator NewBox(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(go);
            go.transform.position = position;
            var box = go.AddComponent<Replicator>();
            box.Build(); // AddComponent's own Awake never runs outside Play mode
            return box;
        }

        [Test]
        public void MV_CycleIsUnder1p6s_TheGateClosesAt0p8m_AndAPinnedRobotIsNudgedFree()
        {
            LogAssert.ignoreFailingMessages = true; // BuildBody's collider-strip [Error], every Replicator test carries this
            DevTuning.GlobalRobotBudget = 20f; // plenty of headroom so room checks never block this test

            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = RigOrigin + new Vector3(0f, 0f, 50f); // outside every check below

            // --- Change 1/2: one robot's total occupancy (Intake start to the second twin existing)
            // must be under 1.6 s, not 4.4 s. ---
            Replicator boxA = NewBox(RigOrigin);
            boxA.Configure(2);
            Set(boxA.gameObject.GetComponent<EnemySpawner>(), "startingRobots", 6);

            RobotEnemy robotA = NewRobot(RigOrigin + new Vector3(5f, 0f, 0f));
            boxA.TickLure();
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, robotA.Current,
                "setup failure: this robot must be lured before the throughput timing can be tested");

            robotA.transform.position = robotA.ReplicatorSeekTarget; // exactly at slot 0 — Intake begins next tick
            var spawnerA = boxA.gameObject.GetComponent<EnemySpawner>();

            float elapsed = 0f;
            const float dtStep = 0.05f;
            int guard = 0;
            while (spawnerA.LiveCountOf(EnemyKind.Rusher) < 2 && guard++ < 100)
            {
                boxA.TickConsumption(dtStep);
                elapsed += dtStep;
            }
            Assert.AreEqual(2, spawnerA.LiveCountOf(EnemyKind.Rusher),
                "setup failure: both twins must eventually emerge");
            // MV-823 superseded this ticket's own throughput ceiling on purpose — Lee's own "a CLEAR
            // BRIGHT LIGHT" replication tell needs long enough on screen to read, so CycleSeconds went
            // 0.9 -> 2.0 and total occupancy grew back from MV-812's ~1.45 s to roughly 3 s. The bound
            // below only guards against a regression back toward MV-812's original 4.4 s, not against
            // MV-823's own deliberately longer cycle.
            Assert.Less(elapsed, 4.5f,
                "elapsed simulated time from Intake start to the second twin existing must stay well under " +
                "the pre-MV-812 4.4 s — MV-823 deliberately lengthened the cycle to ~3 s for the replication light");

            // --- Change 2: a robot 0.8 m from its slot must be drawn into Intake (a 0.35 m gate never
            // closed on it; a 0.9 m gate does). ---
            Replicator boxB = NewBox(RigOrigin + new Vector3(100f, 0f, 0f));
            boxB.Configure(1);
            Set(boxB.gameObject.GetComponent<EnemySpawner>(), "startingRobots", 4);

            RobotEnemy robotB = NewRobot(boxB.transform.position + new Vector3(5f, 0f, 0f));
            boxB.TickLure();
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, robotB.Current,
                "setup failure: this robot must be lured before the arrive-gate width can be tested");

            Vector3 slot0 = boxB.QueueSlotPosition(0);
            robotB.transform.position = new Vector3(slot0.x + 0.8f, robotB.transform.position.y, slot0.z);
            // MV-823 rebuilt the draw-in as a walk-speed-derived AnimSequence rather than a flat
            // IntakeSeconds lerp, so this polls to the despawn rather than assuming a fixed duration.
            int guardB = 0;
            while (robotB.IsAlive && guardB++ < 300) boxB.TickConsumption(0.02f);
            Assert.IsFalse(robotB.IsAlive,
                "a robot 0.8 m from its slot must be taken into Intake and consumed — ArriveTolerance " +
                "must actually be wide enough to close (MV-812)");

            // --- Change 3: a robot whose CharacterController physically can't move (the same "pinned by
            // another body" symptom) must still close distance via the direct nudge, not stand still. ---
            RobotEnemy robotC = NewRobot(RigOrigin + new Vector3(200f, 0f, 0f));
            robotC.SeekReplicator(robotC.transform.position + new Vector3(2f, 0f, 0f)); // 2 m target
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, robotC.Current,
                "setup failure: this robot must be seeking before the stall nudge can be tested");

            var ccC = (CharacterController)CcField.GetValue(robotC);
            ccC.enabled = false; // Move() on a disabled CharacterController is a silent no-op (MV-503)

            float startDist = Vector3.Distance(robotC.transform.position, robotC.ReplicatorSeekTarget);
            for (int i = 0; i < 4; i++)
                TickReplicatorSeekingMethod.Invoke(robotC, new object[] { 0.5f }); // 4 x 0.5 s = 2 simulated seconds

            float endDist = Vector3.Distance(robotC.transform.position, robotC.ReplicatorSeekTarget);
            Assert.GreaterOrEqual(startDist - endDist, 0.5f,
                "a robot whose CharacterController can't move must still close at least 0.5 m toward its " +
                "target within 2 simulated seconds — a stalled seek must nudge the robot directly rather " +
                "than leave it pinned forever (MV-812)");
        }
    }
}
