using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-464: moved from PlayMode (<c>SwarmPacingPlayTests.ProductionPerMinute_ConvertsToTheEquivalentSecondsInterval</c>).
    /// <see cref="EnemySpawner"/> has no Awake/OnEnable of its own — <see cref="EnemySpawner.CurrentInterval"/>
    /// is a plain computed property, so the single settle frame the old PlayMode fixture waited on
    /// bought nothing. <see cref="MowerHutch.Build"/> is called directly instead of relying on its own
    /// Awake, which never runs from AddComponent outside Play mode.
    /// </summary>
    public sealed class EnemySpawnerTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp() => RobotEnemy.ResetRegistry();

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
        }

        private static void Set(object o, string field, object value) =>
            o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
             .SetValue(o, value);

        [Test]
        public void ProductionPerMinute_ConvertsToTheEquivalentSecondsInterval()
        {
            _go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _go.name = "Mower Hutch";
            _go.transform.position = new Vector3(0f, 1f, 15f);
            _go.AddComponent<MowerHutch>().Build();   // RequireComponent brings EnemySpawner
            var spawner = _go.GetComponent<EnemySpawner>();
            Set(spawner, "rampSeconds", 0f);   // collapse the ramp so the steady-state value applies now

            DevTuning.RobotProductionPerMinute = 30f;   // 30/min == one every 2 seconds

            Assert.AreEqual(2.0f, spawner.CurrentInterval, 0.01f,
                "30 robots/minute must convert to a 2-second interval (60 / 30)");
        }

        /// <summary>
        /// MV-643 (AC1+AC2): a shed may only ever emit a kind its own area's authored composition
        /// contains a non-zero count of, and over a long run its output approaches the authored
        /// proportions. Uses a7's real world1 composition — 2 Bruisers, 5 Blinkers, no Rushers — as
        /// the synthetic <see cref="WorldArea"/>'s composition. On base commit 5159d43 this fails
        /// immediately: <c>EnemySpawner.SpawnOne</c> picked purely off <c>EnemyMix.KindFor</c>'s
        /// global cadence, so the first four releases from ANY shed were Rushers regardless of
        /// composition, and <c>EnemySpawner</c> had no <c>ConfigureAreaComposition</c> method for
        /// this test to even call.
        /// </summary>
        [Test]
        public void SpawnOne_OnlyEmitsTheAreasAuthoredKinds_InAuthoredProportion()
        {
            _go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _go.name = "Mower Hutch";
            _go.transform.position = new Vector3(0f, 1f, 15f);
            _go.AddComponent<MowerHutch>().Build();   // RequireComponent brings EnemySpawner
            var spawner = _go.GetComponent<EnemySpawner>();

            var area = new WorldArea { composition = new WorldComposition { bruiser = 2, blinker = 5 } };
            spawner.ConfigureAreaComposition(area.composition);

            MethodInfo spawnOne = typeof(EnemySpawner).GetMethod("SpawnOne", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < 100; i++) spawnOne.Invoke(spawner, null);

            Assert.AreEqual(0, spawner.LiveCountOf(EnemyKind.Rusher),
                "a7 authors no Rushers — none of the 100 releases may be one");

            int bruisers = spawner.LiveCountOf(EnemyKind.Bruiser);
            int blinkers = spawner.LiveCountOf(EnemyKind.Blinker);
            Assert.AreEqual(100, bruisers + blinkers, "every release must be a Bruiser or a Blinker");

            float ratio = (float)bruisers / blinkers;
            Assert.AreEqual(2f / 5f, ratio, 0.1f * (2f / 5f),
                $"bruiser:blinker must stay within 10% of the authored 2:5, got {bruisers}:{blinkers}");
        }
    }
}
