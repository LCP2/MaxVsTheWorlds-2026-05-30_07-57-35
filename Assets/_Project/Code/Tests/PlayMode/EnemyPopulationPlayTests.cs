using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The field-wide spawn budget (YT-186). <see cref="EnemySpawner"/>'s <c>maxLiveEnemies</c> only
    /// ever capped ONE factory's own live count; nothing capped the sum across factories. A level
    /// with several sources escalating on the same shared DifficultyDirector clock (YT-181) could put
    /// far more robots on the field at once than any single factory's cap suggests — exactly what
    /// YT-185's fourth factory did (24 -> 32 worst-case concurrent robots), and what
    /// <see cref="EnemySpawner.GlobalMaxLiveEnemies"/> now stops regardless of factory count.
    /// </summary>
    public sealed class EnemyPopulationPlayTests
    {
        private readonly List<GameObject> _hutches = new List<GameObject>();
        private readonly List<GameObject> _dummies = new List<GameObject>();
        private GameObject _ground;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();

            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "Ground";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(400f, 1f, 400f);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var h in _hutches) if (h != null) Object.Destroy(h);
            _hutches.Clear();
            foreach (var d in _dummies) if (d != null) Object.Destroy(d);
            _dummies.Clear();
            if (_ground != null) Object.Destroy(_ground);

            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
        }

        private GameObject NewHutch(Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Hutch";
            go.transform.position = at;
            go.transform.localScale = new Vector3(3f, 2f, 3f);
            go.AddComponent<MowerHutch>();
            _hutches.Add(go);
            return go;
        }

        /// <summary>A bare robot with no factory behind it — just enough to occupy a seat in the
        /// field-wide registry, so a test can fill the budget without needing real spawners to do it.</summary>
        private GameObject NewDummyRobot(Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "DummyRobot";
            go.transform.position = at;
            go.AddComponent<RobotEnemy>(); // RequireComponent pulls in a CharacterController
            _dummies.Add(go);
            return go;
        }

        private static IEnumerator Run(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; yield return null; }
        }

        [UnityTest]
        public IEnumerator SeveralFactoriesTogetherNeverExceedTheGlobalBudget()
        {
            // Four sources — the shipped Backyard map since YT-185 — each individually capable of
            // reaching its own maxLiveEnemies (8): 32 in total if nothing capped the sum.
            for (int i = 0; i < 4; i++) NewHutch(new Vector3(i * 30f, 1f, 15f));

            DevTuning.SpawnInterval = 0.05f; // fast enough every factory saturates its own cap
            yield return Run(3.0f);

            Assert.LessOrEqual(RobotEnemy.ActiveCount, EnemySpawner.GlobalMaxLiveEnemies,
                $"{RobotEnemy.ActiveCount} robots were alive at once against a field-wide budget of " +
                $"{EnemySpawner.GlobalMaxLiveEnemies} robots — a global cap must hold even when every " +
                "factory is independently still under its own per-factory cap.");
        }

        [UnityTest]
        public IEnumerator AFactoryWithRoomOfItsOwnStillWaits_WhenTheFieldIsFull()
        {
            // Fill the field-wide budget with robots that have nothing to do with this factory.
            for (int i = 0; i < EnemySpawner.GlobalMaxLiveEnemies; i++)
                NewDummyRobot(new Vector3(500f + i, 1f, 500f));

            Assert.AreEqual(EnemySpawner.GlobalMaxLiveEnemies, RobotEnemy.ActiveCount,
                "test setup did not actually fill the field-wide budget");

            var hutch = NewHutch(new Vector3(0f, 1f, 15f));
            var spawner = hutch.GetComponent<EnemySpawner>();
            DevTuning.SpawnInterval = 0.05f; // plenty of chances to spawn if nothing stopped it

            yield return Run(1.0f);

            Assert.AreEqual(0, spawner.Emitted,
                "a factory with plenty of room under its OWN cap still emitted while the field-wide " +
                "budget was full — the global cap is not being respected.");
        }
    }
}
