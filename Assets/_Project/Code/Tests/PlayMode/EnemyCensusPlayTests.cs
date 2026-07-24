using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The cross-factory live-robot budget (YT-186), driven through real spawners.
    ///
    /// Each <see cref="EnemySpawner"/> already caps its OWN concurrent count — but YT-185 gave the map
    /// a fourth factory without adding anything that capped the FIELD, so four independent caps could
    /// sum to twice the robots the frame budget was tuned against. These prove two spawners share one
    /// global ceiling, and that a spawner torn down with robots still alive (a scene teardown, not a
    /// clean kill) doesn't leak them into the count forever.
    /// </summary>
    public sealed class EnemyCensusPlayTests
    {
        private GameObject _hutchA;
        private GameObject _hutchB;
        private GameObject _ground;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            DifficultyDirector.Reset();
            EnemyCensus.Reset();

            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "Ground";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(400f, 1f, 400f);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in new[] { _hutchA, _hutchB, _ground })
                if (go != null) Object.Destroy(go);
            DevTuning.Reset();
            DifficultyDirector.Reset();
            EnemyCensus.Reset();
        }

        private static GameObject NewHutch(Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Mower Hutch";
            go.transform.position = at;
            go.transform.localScale = new Vector3(3f, 2f, 3f);
            go.AddComponent<MowerHutch>();
            return go;
        }

        private static void Set(object o, string field, object value) =>
            o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
             .SetValue(o, value);

        [UnityTest]
        public IEnumerator TwoUncappedSpawners_TogetherNeverExceedTheGlobalCap()
        {
            // Two factories, each individually willing to hold far more than the global budget — the
            // per-spawner cap must not be the only thing standing between the field and YT-185's 32.
            _hutchA = NewHutch(new Vector3(0f, 1f, 15f));
            _hutchB = NewHutch(new Vector3(30f, 1f, 15f));
            var a = _hutchA.GetComponent<EnemySpawner>();
            var b = _hutchB.GetComponent<EnemySpawner>();
            Set(a, "maxLiveEnemies", 100);
            Set(b, "maxLiveEnemies", 100);
            Set(a, "spawnIntervalStart", 0f);
            Set(a, "spawnIntervalMin", 0f);
            Set(b, "spawnIntervalStart", 0f);
            Set(b, "spawnIntervalMin", 0f);

            for (int i = 0; i < 120 && EnemyCensus.Live < EnemyCensus.GlobalMax; i++) yield return null;

            Assert.AreEqual(EnemyCensus.GlobalMax, EnemyCensus.Live,
                $"the field never reached its own cap (stuck at {EnemyCensus.Live}) — the two spawners " +
                "may not be sharing the same budget.");
            Assert.AreEqual(EnemyCensus.Live, a.LiveCount + b.LiveCount,
                "the global count has drifted from the sum of what the spawners actually have live");

            // One more spawn opportunity each — neither should be able to push past the shared ceiling.
            yield return null;
            yield return null;
            Assert.AreEqual(EnemyCensus.GlobalMax, EnemyCensus.Live,
                $"the field grew to {EnemyCensus.Live} past its own cap of {EnemyCensus.GlobalMax}");
        }

        [UnityTest]
        public IEnumerator DestroyingASpawner_WithRobotsStillLive_ForgetsThemFromTheGlobalCount()
        {
            // A raw teardown, not a clean kill through TakeDamage/OnEnemyDied — a level unload or a
            // test's own cleanup. Those robots never individually report themselves gone, so without
            // EnemySpawner.OnDestroy's safety net the global count would only ever climb.
            _hutchA = NewHutch(new Vector3(0f, 1f, 15f));
            var spawner = _hutchA.GetComponent<EnemySpawner>();
            Set(spawner, "spawnIntervalStart", 0f);
            Set(spawner, "spawnIntervalMin", 0f);
            Set(spawner, "maxLiveEnemies", 5);

            for (int i = 0; i < 60 && spawner.LiveCount < 5; i++) yield return null;
            Assert.Greater(EnemyCensus.Live, 0, "nothing spawned — the rest of this test proves nothing");

            Object.Destroy(_hutchA);
            _hutchA = null;
            yield return null; // let the deferred Destroy (and OnDestroy) actually run

            Assert.AreEqual(0, EnemyCensus.Live,
                "robots still alive when their factory was torn down were never forgotten by the " +
                "global census — the count leaked and will wrongly shrink every later run's field.");
        }
    }
}
