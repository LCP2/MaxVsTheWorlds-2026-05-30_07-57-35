using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1217 AC1 — the Dreadnought must never convert: both <see cref="RobotEnemy.TryConvert()"/>
    /// (Override) and <see cref="RobotEnemy.TryConvert(bool)"/> (TRAP, which bypasses the health gate)
    /// return false for it, and a refused conversion leaves <see cref="RobotEnemy.Team"/> at
    /// <see cref="Team.Enemy"/>. The robot is damaged below the Override health threshold first, so
    /// both assertions exercise the kind-based exclusion itself rather than passing vacuously because a
    /// full-health robot was never port-exposed in the first place.
    ///
    /// Spawned through the real production path (<see cref="EnemySpawner.SpawnExact"/>), the same
    /// pool/factory path every in-game robot comes from — not hand-built.
    ///
    /// Fails on 510350d (the commit before this ticket): <c>EnemyKind</c> has no <c>Dreadnought</c>
    /// member there, so this does not compile.
    /// </summary>
    public sealed class MV1217DreadnoughtNeverConvertsTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            RobotEnemy.ResetRegistry();
            RobotEnemy.ConversionCap = 1; // never leak one test's cap into another
        }

        [Test]
        public void Dreadnought_NeverConverts_ViaOverrideOrTrap_MV1217()
        {
            GameObject spawnerGo = null;
            var spawnedRobots = new List<GameObject>();
            try
            {
                spawnerGo = new GameObject("MV1217 Spawner");
                spawnerGo.transform.position = new Vector3(84210f, 0f, 63147f);
                var spawner = spawnerGo.AddComponent<EnemySpawner>();

                List<RobotEnemy> spawned = spawner.SpawnExact(EnemyKind.Dreadnought, 1);
                Assert.AreEqual(1, spawned.Count, "test setup: SpawnExact didn't spawn a Dreadnought");
                RobotEnemy dreadnought = spawned[0];
                spawnedRobots.Add(dreadnought.gameObject);

                // Below RobotEnemy.OverrideHealthThreshold (25%) so the Override path's own health gate
                // is satisfied — proves the refusal below is the Dreadnought-specific exclusion, not an
                // incidental "it was never port-exposed" pass.
                dreadnought.TakeDamage(new DamageInfo(900f, dreadnought.transform.position, Vector3.forward, Team.Player));
                Assert.IsTrue(dreadnought.IsAlive, "test setup: the damage dose killed it outright");
                Assert.Less(dreadnought.HealthNormalized, 0.25f, "test setup: not below the Override health threshold");

                Assert.IsFalse(dreadnought.TryConvert(), "Override's TryConvert() must refuse a Dreadnought");
                Assert.IsFalse(dreadnought.TryConvert(requirePortExposed: false),
                    "TRAP's TryConvert(false) must refuse a Dreadnought");
                Assert.AreEqual(Team.Enemy, dreadnought.Team, "a refused conversion must leave Team untouched");
                Assert.IsFalse(dreadnought.IsConverted);
            }
            finally
            {
                foreach (var go in spawnedRobots) if (go != null) Object.DestroyImmediate(go);
                if (spawnerGo != null) Object.DestroyImmediate(spawnerGo);
            }
        }
    }
}
