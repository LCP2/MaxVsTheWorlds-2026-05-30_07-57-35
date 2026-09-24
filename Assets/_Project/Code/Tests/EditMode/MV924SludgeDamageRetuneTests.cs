using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-924: the Sludge Drone's death puddle now hits Max and a deployed Sentinel at the retuned
    /// 7.5 HP/s (was 6) and NEVER a robot (was: hit both Max and every robot). Testing policy Rule 1 —
    /// one new test, pinning all three receivers in a single puddle tick.
    ///
    /// Fails on the base commit (6d4301e): at that commit <c>SludgePuddle.DamagePerSecond</c> is still
    /// 6, <c>ApplyDamageTick</c> still loops <c>RobotEnemy.Active</c> (so the Rusher takes 12, not 0),
    /// and it never reads <c>Sentinel.Active</c> at all (so the Sentinel takes 0, not 15) — three
    /// independent assertion failures, not one.
    /// </summary>
    public sealed class MV924SludgeDamageRetuneTests
    {
        private GameObject _playerGo;
        private GameObject _rusherGo;
        private GameObject _sentinelGo;
        private PlayerHealth _playerHealth;
        private RobotEnemy _rusher;
        private Sentinel _sentinel;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            SludgePuddle.ResetRegistry();
            RobotEnemy.ResetRegistry();
            Sentinel.ResetRegistry();

            foreach (var stray in Object.FindObjectsByType<SludgePuddle>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            _playerGo = new GameObject("Player", typeof(CharacterController)) { tag = "Player" };
            _playerGo.AddComponent<PlayerController>();
            _playerHealth = _playerGo.AddComponent<PlayerHealth>();
            _playerHealth.Initialize(); // MV-464: exposed publicly so an EditMode test can invoke it directly

            _rusherGo = new GameObject("Rusher");
            _rusher = _rusherGo.AddComponent<RobotEnemy>();
            _rusher.Apply(EnemyArchetype.Rusher);

            _sentinelGo = new GameObject("Sentinel");
            _sentinel = _sentinelGo.AddComponent<Sentinel>();
            _sentinel.Init(Vector3.zero, maxHp: 100f, range: 7f, fireInterval: 0.6f,
                moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
        }

        [TearDown]
        public void TearDown()
        {
            SludgePuddle.ResetRegistry();
            RobotEnemy.ResetRegistry();
            Sentinel.ResetRegistry();
            DevTuning.Reset();
            foreach (var p in Object.FindObjectsByType<SludgePuddle>(FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_rusherGo != null) Object.DestroyImmediate(_rusherGo);
            if (_sentinelGo != null) Object.DestroyImmediate(_sentinelGo);
        }

        [Test]
        public void PuddleTick_DamagesMaxAndASentinelAt7_5HpPerSecond_ButNeverARobot()
        {
            // All three receivers at floor level (y 0, no map loaded — no deck to be above), all well
            // inside the puddle's radius.
            _playerGo.transform.position = Vector3.zero;
            _rusherGo.transform.position = new Vector3(0.5f, 0f, 0f);
            _sentinelGo.transform.position = new Vector3(-0.5f, 0f, 0f);

            SludgePuddle puddle = SludgePuddle.Spawn(Vector3.zero, radius: 2f, duration: 10f);
            try
            {
                float playerBefore = _playerHealth.Current;
                float rusherBefore = _rusher.HealthCurrent;
                float sentinelBefore = _sentinel.HealthCurrent;

                puddle.Tick(2f); // simulated 2 seconds standing in the puddle

                Assert.AreEqual(15f, playerBefore - _playerHealth.Current, 0.01f,
                    "MV-924: Max standing in the puddle for 2s at the retuned 7.5 dmg/s must lose 15 HP");
                Assert.AreEqual(15f, sentinelBefore - _sentinel.HealthCurrent, 0.01f,
                    "MV-924: a floor-level Sentinel standing in the puddle for 2s must also lose 15 HP");
                Assert.AreEqual(rusherBefore, _rusher.HealthCurrent, 0.001f,
                    "MV-924: a robot standing in the puddle for its full life must take no damage from it");
            }
            finally
            {
                Object.DestroyImmediate(puddle.gameObject);
            }
        }
    }
}
