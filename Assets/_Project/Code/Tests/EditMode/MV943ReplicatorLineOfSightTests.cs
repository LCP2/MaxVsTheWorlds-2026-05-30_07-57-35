using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-943 — Lee's play of TestFlight 0.9.9 (a13, 2026-09-25): robots sitting between Replicators
    /// never moved toward Max, even standing in a dead straight line to one, as if the box were a
    /// wall. Root cause: <c>Replicator.Build()</c> put the box on the <c>Cover</c> layer (the same call
    /// <c>MowerHutch.Build()</c> makes), and <c>LineOfSight</c> — what <c>AmbushWake</c> gates a
    /// Dormant robot's wake on — casts against exactly that layer. Fails on the commit before this
    /// ticket: <c>LineOfSight.Between</c> reads blocked with a Replicator sitting on the line between
    /// robot and Max, so the wake check below never fires and the robot stays Dormant. Fix mirrors the
    /// existing hedge/pipe/see-through-cover precedent (<c>MapRuntime.BuildCover</c>, MV-400/MV-863/
    /// MV-917): the box keeps its collider (still blocks a footstep, still takes Water Blaster
    /// damage — <c>hitMask</c> defaults to <c>~0</c>) but comes off the Cover layer, so a sight-line
    /// passes straight through it. Tier 2 (resolved values): asserts the resolved
    /// <c>LineOfSight.Between</c> boolean, the resolved wake-state transition, and the resolved
    /// lure-assignment target — never an authored constant, never a rendered pixel.
    /// </summary>
    public sealed class MV943ReplicatorLineOfSightTests
    {
        // Same "distinctive far-off origin" idiom ReplicatorTests/MV548MobileShedTests use — EditMode
        // tests share one physics scene for the whole cc-verify run with no per-test reset.
        private static readonly Vector3 RigOrigin = new Vector3(-77341f, 0f, 63118f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo TickDormantMethod =
            typeof(RobotEnemy).GetMethod("TickDormant", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _maxGo;
        private GameObject _replicatorGo;
        private GameObject _robotGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            // MV-478 fail-open: with no Camera.main, IsOnScreen() reads true, same idiom
            // MV363DormantRobotTests uses to make the wake check deterministic here.
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            if (_maxGo != null) Object.DestroyImmediate(_maxGo);
            if (_replicatorGo != null) Object.DestroyImmediate(_replicatorGo);
            if (_robotGo != null) Object.DestroyImmediate(_robotGo);
        }

        [Test]
        public void RobotSeeingMaxOnlyThroughAReplicator_WakesAndTargetsItsInLane()
        {
            LogAssert.ignoreFailingMessages = true; // same BuildBody collider-strip [Error] ReplicatorTests already ignores

            _maxGo = new GameObject("Max") { tag = "Player" };
            _maxGo.transform.position = RigOrigin + new Vector3(0f, 0f, 10f);

            // The box sits exactly on the line between the robot and Max — the only path a sight-line
            // raycast between them can take.
            _replicatorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _replicatorGo.name = "Replicator";
            _replicatorGo.transform.position = RigOrigin + new Vector3(0f, 0f, 5f);
            _replicatorGo.transform.localScale = new Vector3(2f, 1.5f, 2f); // the ticket's authored 2x1.5x2 footprint
            var replicator = _replicatorGo.AddComponent<Replicator>();
            replicator.Build();
            replicator.Configure(1);

            _robotGo = new GameObject("Rusher");
            _robotGo.AddComponent<CharacterController>();
            var robot = _robotGo.AddComponent<RobotEnemy>();
            CcField.SetValue(robot, _robotGo.GetComponent<CharacterController>());
            robot.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            OnEnableMethod.Invoke(robot, null); // populates RobotEnemy.Active, which TickLure reads
            robot.transform.position = RigOrigin;
            robot.BeginDormant();

            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset) — see SightlineTests

            Assert.IsTrue(LineOfSight.Between(robot.transform, _maxGo.transform),
                "MV-943: a Replicator's own box must not block a robot's sight-line to Max — it must " +
                "read see-through exactly like a hedge or a pipe run, not like the Mower Hutch");

            robot.Sight.Tick(true, _maxGo.transform.position, 0.1f);
            TickDormantMethod.Invoke(robot, null);
            Assert.AreEqual(RobotEnemy.State.Alert, robot.Current,
                "a clear sight-line through the Replicator must wake the robot, same as any other clear line");

            replicator.SetAreaIndex(1);
            robot.SetAreaIndex(1);
            replicator.TickLure();
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, robot.Current,
                "once eligible and in the box's own area, the robot must route to the Replicator's IN lane");
            Assert.AreEqual(replicator.QueueSlotPosition(0), robot.ReplicatorSeekTarget,
                "the seek target must be the hatch's own IN-lane queue slot, not some other point");
        }
    }
}
