using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Drives a real spawner (YT-66): both robot kinds must actually reach the field, and each must
    /// be wearing its OWN body. The bug this exists to catch is pooling — recycle a dead bruiser
    /// into the next rusher and you get a rusher's stats inside a bruiser's box, which no unit test
    /// on the archetype data would ever see.
    /// </summary>
    public sealed class EnemyMixPlayTests
    {
        private GameObject _go;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            yield return null;
        }

        private static void Set(object o, string field, object value) =>
            o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
             .SetValue(o, value);

        private EnemySpawner NewSpawner()
        {
            _go = new GameObject("Factory");
            var s = _go.AddComponent<EnemySpawner>();
            Set(s, "spawnIntervalStart", 0f);   // spawn every frame — a test shouldn't wait 1.8 s
            Set(s, "spawnIntervalMin", 0f);
            Set(s, "maxLiveEnemies", 12);
            Set(s, "bruiserEvery", 4);
            Set(s, "firstBruiserAt", 3);
            return s;
        }

        private static IEnumerator RunUntilFull(EnemySpawner s)
        {
            for (int i = 0; i < 60 && s.LiveCount < 12; i++) yield return null;
        }

        /// <summary>Every live robot's BODY must match its KIND — the pooling invariant.</summary>
        private static void AssertBodiesMatchKinds(EnemySpawner s)
        {
            foreach (var e in s.GetComponentsInChildren<RobotEnemy>(false))
            {
                var mesh = e.GetComponent<MeshFilter>().sharedMesh;
                bool wearingABox = mesh.name.Contains("Cube");
                Assert.AreEqual(e.Kind == EnemyKind.Bruiser, wearingABox,
                    $"a {e.Kind} is wearing the wrong body ({mesh.name})");
            }
        }

        [UnityTest]
        public IEnumerator BothKindsReachTheField()
        {
            var s = NewSpawner();
            yield return RunUntilFull(s);

            Assert.Greater(s.LiveCountOf(EnemyKind.Rusher), 0, "no rushers");
            Assert.Greater(s.LiveCountOf(EnemyKind.Bruiser), 0, "no bruisers — the fight has no texture");
            Assert.Greater(s.LiveCountOf(EnemyKind.Rusher), s.LiveCountOf(EnemyKind.Bruiser),
                "bruisers should be punctuation, not the swarm");
        }

        [UnityTest]
        public IEnumerator EachKindWearsItsOwnBody()
        {
            var s = NewSpawner();
            yield return RunUntilFull(s);
            AssertBodiesMatchKinds(s);
        }

        [UnityTest]
        public IEnumerator RecycledRobotsKeepTheirOwnBodies()
        {
            var s = NewSpawner();
            yield return RunUntilFull(s);

            // Wipe the field, forcing every robot back into a pool. Check it emptied BEFORE yielding —
            // the spawner refills on the very next frame, so a yield here would race the assert.
            foreach (var e in s.GetComponentsInChildren<RobotEnemy>(false))
                e.TakeDamage(new DamageInfo(9999f, e.transform.position, Vector3.forward, Team.Player));
            Assert.AreEqual(0, s.LiveCount, "the field didn't clear");

            // …then refill it entirely from those pools.
            yield return RunUntilFull(s);

            Assert.Greater(s.LiveCountOf(EnemyKind.Bruiser), 0, "no bruisers after recycling");
            AssertBodiesMatchKinds(s);   // a bruiser must not come back as a rusher, or vice versa
        }

        [UnityTest]
        public IEnumerator ABruiserIsTougherThanARusher_InTheActualGame()
        {
            var s = NewSpawner();
            yield return RunUntilFull(s);

            // One rusher's worth of damage kills a rusher outright and barely dents a bruiser.
            float shot = EnemyArchetype.Rusher.MaxHealth;

            foreach (var e in s.GetComponentsInChildren<RobotEnemy>(false))
            {
                bool bruiser = e.Kind == EnemyKind.Bruiser;
                e.TakeDamage(new DamageInfo(shot, e.transform.position, Vector3.forward, Team.Player));
                Assert.AreEqual(bruiser, e.IsAlive,
                    bruiser ? "a bruiser died to one rusher's worth of damage"
                            : "a rusher survived a full-health shot");
            }
        }
    }
}
