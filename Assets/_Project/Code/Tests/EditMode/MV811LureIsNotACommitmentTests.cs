using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-811 — a lure used to be a one-way commitment: <see cref="Replicator.TickLure"/> screened a
    /// candidate once, at selection, and <see cref="RobotEnemy"/>'s own seeking tick never re-checked
    /// distance to Max or gave up on a slot that never freed. A queued robot stood at its spot forever,
    /// out of the fight, even once Max walked right up to it — Lee, on build 9751529: "they... don't
    /// attack max and so just get destroyed". Fails on that base commit: a robot moved next to Max, or
    /// left queued indefinitely, never leaves State.ReplicatorSeeking on its own, and nothing bounds how
    /// many robots may be parked in it at once.
    ///
    /// Tier 2 (resolved values): every assertion reads a robot's resolved <see cref="RobotEnemy.Current"/>
    /// or the resolved <see cref="RobotEnemy.ReplicatorSeekingCount"/> — never an internal queue list,
    /// never an authored constant.
    /// </summary>
    public sealed class MV811LureIsNotACommitmentTests
    {
        private static readonly Vector3 RigOrigin = new Vector3(63102f, 0f, -18447f);
        private static readonly Vector3 CeilingRigOrigin = RigOrigin + new Vector3(1000f, 0f, 1000f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo TickReplicatorSeekingMethod =
            typeof(RobotEnemy).GetMethod("TickReplicatorSeeking", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo StateTimerField =
            typeof(RobotEnemy).GetField("_stateTimer", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private GameObject _playerGo;

        [SetUp]
        public void SetUp() => RobotEnemy.ResetRegistry();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            RobotEnemy.ResetRegistry();
        }

        private RobotEnemy NewRobot(Vector3 position)
        {
            var go = new GameObject("MV811-robot");
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
            box.Configure(1);
            return box;
        }

        [Test]
        public void MV_ALureIsReCheckedEveryTick_TimesOut_AndIsCappedFieldWide()
        {
            LogAssert.ignoreFailingMessages = true; // same BuildBody collider-strip [Error] every Replicator test carries

            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = RigOrigin + new Vector3(0f, 0f, 40f); // far from every candidate below

            Replicator replicator = NewBox(RigOrigin);

            // --- Change 1: re-checked every tick, not only at selection ---
            RobotEnemy lured = NewRobot(RigOrigin + new Vector3(5f, 0f, 0f)); // within lure radius, far from Max
            replicator.TickLure();
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, lured.Current,
                "setup failure: this robot must be lured before the melee re-check can be tested");

            lured.transform.position = _playerGo.transform.position + new Vector3(2f, 0f, 0f); // 2 m from Max
            TickReplicatorSeekingMethod.Invoke(lured, new object[] { 0.02f });
            Assert.AreEqual(RobotEnemy.State.Chase, lured.Current,
                "a lured robot that closes to within MaxMeleeExclusionRadius of Max must cancel the lure " +
                "and return to Chase the very next tick, not only if the box happens to re-screen it (MV-811)");

            replicator.TickConsumption(0.02f); // lets the box notice the cancel and drop it from its queue

            // --- Change 3: nobody waits forever ---
            RobotEnemy stuck = NewRobot(RigOrigin + new Vector3(-5f, 0f, 0f)); // within lure radius, far from Max too
            replicator.TickLure();
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, stuck.Current,
                "setup failure: this robot must be lured before the timeout can be tested");

            StateTimerField.SetValue(stuck, 6.0f); // MV-811's own 6 s floor
            TickReplicatorSeekingMethod.Invoke(stuck, new object[] { 0.02f });
            Assert.AreEqual(RobotEnemy.State.Chase, stuck.Current,
                "a robot queued for more than 6 s without being taken into Intake must give up and resume Chase (MV-811)");
            Assert.IsTrue(stuck.NoReplicate,
                "a robot that timed out waiting must refuse to immediately re-queue at the same box (MV-811)");

            replicator.TickConsumption(0.02f);
            replicator.TickLure();
            Assert.AreNotEqual(RobotEnemy.State.ReplicatorSeeking, stuck.Current,
                "a robot tagged NoReplicate by the timeout must not re-enter the queue on the very next TickLure (MV-811)");

            // --- Change 5: a field-wide ceiling on how many may be seeking at once ---
            RobotEnemy.ResetRegistry();
            _playerGo.transform.position = CeilingRigOrigin + new Vector3(0f, 0f, 400f); // clear of every candidate below

            var candidates = new RobotEnemy[20];
            for (int i = 0; i < 20; i++)
                candidates[i] = NewRobot(CeilingRigOrigin + new Vector3(0.5f * i, 0f, 5f)); // all within the 16 m lure radius

            var boxes = new Replicator[3];
            for (int b = 0; b < 3; b++)
                boxes[b] = NewBox(CeilingRigOrigin + new Vector3(5f * b, 0f, -2f));

            // Several passes, letting whichever robot is already queued "arrive" at its slot between
            // ticks — the queue-cap rule (change 4) only ever opens one more slot per box per tick, so
            // repeated passes are what actually exercises the ceiling rather than the per-box queue cap.
            for (int pass = 0; pass < 8; pass++)
            {
                foreach (RobotEnemy c in candidates)
                    if (c.Current == RobotEnemy.State.ReplicatorSeeking) c.transform.position = c.ReplicatorSeekTarget;
                foreach (Replicator box in boxes) box.TickLure();
            }

            Assert.LessOrEqual(RobotEnemy.ReplicatorSeekingCount, 5,
                "with 20 live robots, no more than 25% (5) may ever be in State.ReplicatorSeeking at once (MV-811)");
        }
    }
}
