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
    /// MV-807 — every lured robot used to be steered at the SAME point (the hatch face), so a "queue"
    /// never formed: robots pressed against the box and each other, unable to close the remaining
    /// distance to <see cref="Replicator.ArriveTolerance"/>. Fails on base commit f89c4e7: with four
    /// eligible robots inside the lure radius, <see cref="Replicator.TickLure"/> lures all four (not
    /// two) and gives every one of them the identical <see cref="Replicator.HatchPosition"/> as its
    /// <see cref="RobotEnemy.ReplicatorSeekTarget"/>. Quoted in this ticket's fix comment.
    ///
    /// Tier 2 (resolved values): every assertion reads a robot's resolved <see cref="RobotEnemy.Current"/>
    /// state or its resolved <see cref="RobotEnemy.ReplicatorSeekTarget"/> — never an internal list
    /// count, never an authored constant asserted back at itself.
    /// </summary>
    public sealed class MV807ReplicatorQueueTests
    {
        // Same "distinctive far-off origin" idiom every other Replicator EditMode test uses — EditMode
        // tests share one physics scene for the whole cc-verify run with no per-test reset.
        private static readonly Vector3 RigOrigin = new Vector3(19099f, 0f, -30871f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private GameObject _playerGo;
        private GameObject _replicatorGo;

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
            if (_replicatorGo != null) Object.DestroyImmediate(_replicatorGo);
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
        }

        private RobotEnemy NewRobot(Vector3 position)
        {
            var go = new GameObject("MV807-robot");
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

        private static int CountSeeking(IReadOnlyList<RobotEnemy> robots)
        {
            int n = 0;
            foreach (RobotEnemy r in robots) if (r.Current == RobotEnemy.State.ReplicatorSeeking) n++;
            return n;
        }

        [Test]
        public void MV_QueueTakesExactlyTwoRobotsAndPromotesSlotOneWhenSlotZeroIsConsumed()
        {
            LogAssert.ignoreFailingMessages = true; // BuildBody's collider-strip [Error], every Replicator test carries this

            DevTuning.GlobalRobotBudget = 20f; // plenty of headroom so HasRoomForReplicatorIntake never blocks this test

            _replicatorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _replicatorGo.name = "Replicator";
            _replicatorGo.transform.position = RigOrigin;
            var replicator = _replicatorGo.AddComponent<Replicator>();
            replicator.Build(); // AddComponent's own Awake never runs outside Play mode
            replicator.Configure(1); // capacity > 0

            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = RigOrigin + new Vector3(0f, 0f, 50f); // well outside the 7 m melee exclusion

            var robots = new RobotEnemy[4];
            for (int i = 0; i < 4; i++)
                robots[i] = NewRobot(RigOrigin + new Vector3(4f + i * 2f, 0f, 6f)); // all within the 16 m lure radius

            replicator.TickLure();

            Assert.AreEqual(2, CountSeeking(robots),
                "a queue two deep must lure exactly two of the four eligible robots, never all four");

            RobotEnemy slot0 = robots[0], slot1 = robots[1];
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, slot0.Current,
                "the first eligible robot scanned must take the queue's first slot");
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, slot1.Current,
                "the second eligible robot scanned must take the queue's second slot");
            Assert.AreNotEqual(RobotEnemy.State.ReplicatorSeeking, robots[2].Current,
                "a full two-deep queue must lure nobody else");
            Assert.AreNotEqual(RobotEnemy.State.ReplicatorSeeking, robots[3].Current,
                "a full two-deep queue must lure nobody else");

            Assert.GreaterOrEqual(Vector3.Distance(slot0.ReplicatorSeekTarget, slot1.ReplicatorSeekTarget), 1.0f,
                "two queued robots must never be steered at the same point — their slots must be at least 1 m apart");
            Assert.AreNotEqual(replicator.HatchPosition, slot0.ReplicatorSeekTarget,
                "a queued robot's target must be its own slot, never the hatch itself");
            Assert.AreNotEqual(replicator.HatchPosition, slot1.ReplicatorSeekTarget,
                "a queued robot's target must be its own slot, never the hatch itself");

            // --- Consume the slot-0 robot fully (so "seeking" below can only mean "still queued"),
            // then re-run the lure: the queue must close up AND refill. ---
            Vector3 slot0Target = slot0.ReplicatorSeekTarget;
            slot0.transform.position = slot0Target;
            replicator.TickConsumption(Replicator.IntakeSeconds + 0.01f);
            Assert.IsFalse(slot0.IsAlive,
                "setup failure: the slot-0 robot must be despawned into the Cycle beat before the re-lure below");

            replicator.TickLure();

            Assert.AreEqual(2, CountSeeking(robots),
                "once slot 0 empties, the lure must refill it — exactly two robots must again be queued");
            Assert.AreEqual(slot0Target, slot1.ReplicatorSeekTarget,
                "the robot formerly at slot 1 must be promoted onto slot 0's own position, re-targeted the same tick it advances");
        }
    }
}
