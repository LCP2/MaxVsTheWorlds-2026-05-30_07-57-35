using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1396 — the robot a relay ADDS (second twin) never drops; the first twin keeps the input
    /// robot's eligibility. One relay cycle on a drop-eligible Heavy; both twins are killed and the
    /// resolved PowerCell pickups each death spawned are counted (Tier 2). Fails on b697b16: the
    /// second twin's death spawns the same Power Cells as the first.
    /// </summary>
    public sealed class MV1396RelayAddedRobotDropsNothingTests
    {
        private static readonly Vector3 RigOrigin = new Vector3(-61877f, 0f, 29314f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo LiveField =
            typeof(EnemySpawner).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private GameObject _directorGo;
        private PickupDirector _director;

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
            PickupWallet.Reset();
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            _directorGo = new GameObject("PickupDirector");
            _director = _directorGo.AddComponent<PickupDirector>();
            // OnEnable's own subscription is not reliable off a bare AddComponent in EditMode, so route
            // the real DropSignals.RobotDied announcement to the real OnRobotDied handler explicitly.
            _onDied = (pos, kind) => typeof(PickupDirector).GetMethod("OnRobotDied",
                BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_director, new object[] { pos, kind });
            DropSignals.RobotDied += _onDied;
        }

        private System.Action<Vector3, EnemyKind> _onDied;

        [TearDown]
        public void TearDown()
        {
            if (_onDied != null) DropSignals.RobotDied -= _onDied;
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
            PickupWallet.Reset();
        }

        private int PowerCellCount() =>
            ((List<Pickup>)typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_director)).Count(p => p != null && p.Kind == PickupKind.PowerCell);

        [Test]
        public void RelayCycleOnDropEligibleInput_FirstTwinDropsPowerCells_SecondTwinDropsNone()
        {
            LogAssert.ignoreFailingMessages = true;
            DevTuning.GlobalRobotBudget = 100f;
            DevTuning.CellsPerLargeKill = 1f;
            DevTuning.PowerCellDropRatio = 0f;

            var boxGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(boxGo);
            boxGo.transform.position = RigOrigin;
            var box = boxGo.AddComponent<Replicator>();
            box.Build();
            box.Configure(1);
            box.SetAreaIndex(1);

            var robotGo = new GameObject("MV1396-Heavy");
            _spawned.Add(robotGo);
            var cc = robotGo.AddComponent<CharacterController>();
            RobotEnemy head = robotGo.AddComponent<RobotEnemy>();
            CcField.SetValue(head, cc);
            head.Apply(EnemyArchetype.Heavy);
            OnEnableMethod.Invoke(head, null);
            head.transform.position = RigOrigin + new Vector3(3f, 0f, 0f);
            head.SetAreaIndex(1);

            EnemySpawner spawner = boxGo.GetComponent<EnemySpawner>();
            var live = (IList)LiveField.GetValue(spawner);

            box.OnAreaEntered(1);
            Assert.IsTrue(head.IsAssignedToReplicator, "setup failure: the lone robot must be queued");
            head.transform.position = head.ReplicatorSeekTarget;
            int guard = 0;
            while (spawner.Emitted < 2 && guard++ < 2000) box.TickConsumption(0.02f);
            Assert.AreEqual(2, spawner.Emitted, "setup failure: the pair must emerge");

            RobotEnemy first = (RobotEnemy)live[0];
            RobotEnemy second = (RobotEnemy)live[1];

            // Hold the director's own pickup-collection out of the way: Max is nowhere near.
            first.TakeDamage(new DamageInfo(1_000_000f, first.transform.position, Vector3.forward, Team.Player));
            int afterFirst = PowerCellCount();
            second.TakeDamage(new DamageInfo(1_000_000f, second.transform.position, Vector3.forward, Team.Player));
            int afterSecond = PowerCellCount();

            Assert.Greater(afterFirst, 0, "the first twin keeps its input's drop eligibility: it drops Power Cells");
            Assert.AreEqual(afterFirst, afterSecond, "the second (added) twin's death must spawn no Power Cells");
        }
    }
}
