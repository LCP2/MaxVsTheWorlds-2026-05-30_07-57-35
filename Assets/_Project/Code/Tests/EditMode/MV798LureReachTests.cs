using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-798 — <see cref="Replicator.LureRadius"/> 8f -&gt; 16f and
    /// <see cref="Replicator.MaxMeleeExclusionRadius"/> 4f -&gt; 7f: the old 8 m lure disc covered as
    /// little as 5% of an authored World 2 area, so most robots on a beeline at Max never passed
    /// close enough to a box to be lured at all. Fails on base commit f89c4e7 — a robot placed 14 m
    /// from the hatch and 12 m from Max sits outside the old 8 m radius and is never lured. Tier 2
    /// (resolved values): asserts each robot's resolved <see cref="RobotEnemy.Current"/> state after
    /// one <see cref="Replicator.TickLure"/>, never an authored constant.
    /// </summary>
    public sealed class MV798LureReachTests
    {
        // Same "distinctive far-off origin" idiom ReplicatorTests uses — EditMode tests share one
        // physics scene for the whole cc-verify run with no per-test reset.
        private static readonly Vector3 RigOrigin = new Vector3(-77321f, 0f, 55901f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _playerGo;
        private GameObject _replicatorGo;
        private GameObject _farRobotGo;
        private GameObject _nearRobotGo;

        [SetUp]
        public void SetUp() => RobotEnemy.ResetRegistry();

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_replicatorGo != null) Object.DestroyImmediate(_replicatorGo);
            if (_farRobotGo != null) Object.DestroyImmediate(_farRobotGo);
            if (_nearRobotGo != null) Object.DestroyImmediate(_nearRobotGo);
        }

        private RobotEnemy NewRobot(GameObject go, Vector3 position)
        {
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            // OnEnable never runs as a side effect of AddComponent outside Play mode (same quirk
            // ReplicatorTests.NewRusher's own doc comment names) — Replicator.TickLure reads
            // RobotEnemy.Active, which OnEnable is what populates, so it's invoked directly here.
            OnEnableMethod.Invoke(e, null);
            e.transform.position = position;
            return e;
        }

        [Test]
        public void MV_ARobot14mFromTheHatchIsLured_ARobot10mAwayBut6mFromMaxIsNot()
        {
            LogAssert.ignoreFailingMessages = true; // same BuildBody collider-strip [Error] every Replicator test carries

            _replicatorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _replicatorGo.name = "Replicator";
            _replicatorGo.transform.position = RigOrigin;
            var replicator = _replicatorGo.AddComponent<Replicator>();
            replicator.Build(); // AddComponent's own Awake never runs outside Play mode
            replicator.Configure(1); // capacity > 0

            Vector3 hatch = replicator.HatchPosition;

            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = hatch + new Vector3(10f, 0f, 0f); // 10 m from the hatch

            // Solved by law of cosines against the 10 m hatch/player gap so that, exactly:
            // |farRobot - hatch| = 14, |farRobot - player| = 12.
            _farRobotGo = new GameObject("FarRobot");
            RobotEnemy farRobot = NewRobot(_farRobotGo, hatch + new Vector3(7.6f, 0f, 11.757551f));

            // Solved the same way so, exactly: |nearRobot - hatch| = 10, |nearRobot - player| = 6.
            _nearRobotGo = new GameObject("NearRobot");
            RobotEnemy nearRobot = NewRobot(_nearRobotGo, hatch + new Vector3(8.2f, 0f, 5.723635f));

            replicator.TickLure();

            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, farRobot.Current,
                "14 m from the hatch and 12 m from Max is within the 16 m LureRadius and clear of the " +
                "7 m MaxMeleeExclusionRadius — this robot must be lured (MV-798)");
            Assert.AreNotEqual(RobotEnemy.State.ReplicatorSeeking, nearRobot.Current,
                "10 m from the hatch but only 6 m from Max is inside the 7 m MaxMeleeExclusionRadius — " +
                "this robot must not be pulled out of a fight it's already in (MV-798)");
        }
    }
}
