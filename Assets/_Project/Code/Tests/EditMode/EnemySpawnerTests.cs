using System.Reflection;
using NUnit.Framework;
using UnityEngine;
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

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            DevTuning.Reset();
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
    }
}
