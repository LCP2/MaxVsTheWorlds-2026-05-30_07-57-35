using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1383 — ORBITAL is twice as strong: a spawned ORBITAL resolves to 90 max health, and one of its
    /// missiles hitting Max delivers 16 damage (was 45 / 8). Asserts resolved values (the archetype-applied
    /// robot health, the amount Max's IDamageable actually receives), never the tuning constants.
    /// </summary>
    public sealed class MV1383OrbitalDoubleTests
    {
        private sealed class StubMax : MonoBehaviour, IDamageable
        {
            public float LastAmount = -1f;
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info) => LastAmount = info.Amount;
        }

        private GameObject _robot;
        private GameObject _max;

        [TearDown]
        public void TearDown()
        {
            DevTuning.Reset();
            OrbitalMissile.DestroyPoolForTests();
            if (_robot != null) Object.DestroyImmediate(_robot);
            if (_max != null) Object.DestroyImmediate(_max);
        }

        [Test]
        public void SpawnedOrbital_HasNinetyMaxHealth_AndItsMissileHitsMaxForSixteen()
        {
            DevTuning.Reset();

            _robot = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var enemy = _robot.AddComponent<RobotEnemy>();
            enemy.Apply(EnemyArchetype.Orbital);
            Assert.That(enemy.MaxHealth, Is.EqualTo(90f), "resolved ORBITAL max health");

            _max = new GameObject("Max (stub)");
            var stub = _max.AddComponent<StubMax>();
            _max.transform.position = new Vector3(2f, 0f, 0f);

            OrbitalMissile m = OrbitalMissile.Spawn(Vector3.zero, Vector3.right, _max.transform, 0, straight: true);
            Assert.IsNotNull(m, "missile spawned");
            for (int i = 0; i < 200 && stub.LastAmount < 0f; i++) m.Tick(0.02f);

            Assert.That(stub.LastAmount, Is.EqualTo(16f), "damage Max actually takes from one ORBITAL missile");
        }
    }
}
