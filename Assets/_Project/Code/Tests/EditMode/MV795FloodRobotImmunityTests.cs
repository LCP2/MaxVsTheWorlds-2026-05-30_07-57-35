using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-795: <see cref="StormdrainFloodRunner"/> applied its flood damage-over-time to every live
    /// <see cref="RobotEnemy"/> standing in flooded ground as well as the player — the world emptied
    /// itself of its own population and, through the resulting damage/kill trauma, pinned the camera
    /// shake on permanently. MV-774's own summary is that the flood is player pressure, never robot
    /// pressure. Fails on the pre-fix commit f89c4e7, where the runner's private robot loop still
    /// applies a <see cref="FloodDamageTicker"/> to every <see cref="RobotEnemy.Active"/> robot: the
    /// robot standing in the same flooded ground as the player takes damage there too, so this test's
    /// robot-health assertion fails.
    ///
    /// Drives the real <see cref="StormdrainFloodRunner.TickFlood"/> path end to end, not
    /// <see cref="FloodDamageTicker"/> directly (that piece, and its "an IDamageable in flooded ground
    /// loses health" contract, is already covered by <c>MV774FloodTests</c>) — what regressed here is
    /// the runner's OWN loop, which only an integration test through the runner can see.
    /// </summary>
    public sealed class MV795FloodRobotImmunityTests
    {
        private sealed class FakeReceiver : MonoBehaviour, IDamageable
        {
            public float TotalDamageTaken;
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info) => TotalDamageTaken += info.Amount;
        }

        // Same reflection idiom ReplicatorTests/MV733AutoAimEngageableAreaFilterTests already use to
        // reach into a private field/method a test fixture has no other way to drive.
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _pathGo;
        private GameObject _runnerGo;
        private GameObject _playerGo;
        private GameObject _robotGo;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            StormdrainFlood.Reset();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            StormdrainFlood.Reset();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
            if (_runnerGo != null) Object.DestroyImmediate(_runnerGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_robotGo != null) Object.DestroyImmediate(_robotGo);
        }

        /// <summary>One 40x40 m room at the origin, authored as "area9" of 9 — band 1 (closest to the
        /// Wet Well) under <see cref="StormdrainFlood.BandForAreaIndex"/>'s "last third of the route"
        /// rule — covering both the player and the robot this test places at <see cref="Vector3.zero"/>.
        /// Same "seed <see cref="EnemyNavigation.Map"/> through a bare <see cref="BackyardPath"/>" idiom
        /// <c>MV733AutoAimEngageableAreaFilterTests.InstallMap</c> already uses.</summary>
        private void InstallFloodedZone()
        {
            var zone = new MapZone { id = "area9", x = 0f, z = 0f, width = 40f, depth = 40f };
            var map = new MapData { zones = new[] { zone } };

            _pathGo = new GameObject("MV795-flood-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            FieldInfo mapField = typeof(BackyardPath).GetField("_map",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            mapField.SetValue(path, map);
        }

        [Test]
        public void TickFlood_DamagesThePlayer_ButLeavesALiveRobotUntouched()
        {
            InstallFloodedZone();
            StormdrainFlood.Configure(9);

            // Cross band 1's threshold in one tick (a dt this large also pushes elapsed seconds past
            // the opening-minute cap, so the level lands where the maths says rather than clamped to
            // 0.15), then let the full 3 s warning elapse — the same two-phase shape MV774FloodTests's
            // own DriveTo + warning-tick pair uses. Driven directly through the static
            // StormdrainFlood.Tick, NOT the runner: routing this warm-up through
            // StormdrainFloodRunner.TickFlood would hand its very first call a multi-second dt that only
            // turns flooded partway through, and the ticker would (correctly, per its own per-tick
            // contract) charge the player for the WHOLE of that dt the instant it becomes flooded —
            // double-counting the measured 3 s below.
            float rate = StormdrainFlood.UnhurriedFillAtRunLength / DifficultyDirector.RunLengthSeconds;
            StormdrainFlood.Tick((StormdrainFlood.Band1Threshold + 0.01f) / rate, liveReplicators: 0, livePumpHousings: 0);
            StormdrainFlood.Tick(StormdrainFlood.WarningSeconds + 0.1f, liveReplicators: 0, livePumpHousings: 0);

            Assert.IsTrue(StormdrainFlood.IsFlooded(Vector3.zero),
                "the shared origin must read flooded before the damage phase starts, or this test proves nothing");

            _runnerGo = new GameObject("MV795-flood-runner");
            var runner = _runnerGo.AddComponent<StormdrainFloodRunner>();

            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = Vector3.zero;
            var receiver = _playerGo.AddComponent<FakeReceiver>();

            _robotGo = new GameObject("Robot");
            var cc = _robotGo.AddComponent<CharacterController>();
            var robot = _robotGo.AddComponent<RobotEnemy>();
            CcField.SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            OnEnableMethod.Invoke(robot, null);
            _robotGo.transform.position = Vector3.zero;
            float robotHealthBefore = robot.HealthCurrent;

            // Measured phase: the flood is already established above, so every one of these small,
            // equal steps sees isFlooded=true from the start — no warm-up dt left to double-count.
            const float simulatedSeconds = 3f;
            const float step = 0.1f;
            for (float t = 0f; t < simulatedSeconds; t += step)
                runner.TickFlood(step);

            Assert.AreEqual(StormdrainFlood.DamagePerSecond * simulatedSeconds, receiver.TotalDamageTaken, 0.5f,
                "the player standing in flooded ground for 3s must take exactly the authored per-second rate x 3");
            Assert.AreEqual(robotHealthBefore, robot.HealthCurrent, 0.001f,
                "a live RobotEnemy standing in the same flooded ground must take no flood damage — the flood is player pressure only");
        }
    }
}
