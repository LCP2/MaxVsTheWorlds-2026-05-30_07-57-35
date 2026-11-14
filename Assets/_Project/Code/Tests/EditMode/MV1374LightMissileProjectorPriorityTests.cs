using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1374 (the one new test, Rule 1; Tier 2 resolved health): four Rushers stand inside a live Shield
    /// Projector dome directly between Max and the Projector, which sits 8 m from Max. One real
    /// <see cref="LightMissile"/> volley is fired (the production FireVolley). Within 3 s the Projector's
    /// resolved <see cref="RobotEnemy.HealthCurrent"/> must have dropped, and no protected Rusher's health
    /// may have changed.
    ///
    /// Fails on base commit ea74fe6 (main HEAD before this ticket): every ball locks the nearest-angle
    /// protected Rusher, which takes 0 (dome x0), so the Projector's health never moves -- the first
    /// assertion fails (Projector health before == after).
    /// </summary>
    public sealed class MV1374LightMissileProjectorPriorityTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo CcField = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly FieldInfo HealthField = typeof(RobotEnemy).GetField("_health", NonPublicInstance);
        private static readonly MethodInfo OnEnable = typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);
        private static readonly MethodInfo FireVolley = typeof(LightMissile).GetMethod("FireVolley", NonPublicInstance);

        private static readonly Vector3 Origin = new Vector3(71234f, 0f, -45678f);

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp() => RobotEnemy.ResetRegistry();

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            LightMissileBall.DestroyPoolForTests();
            foreach (var d in Object.FindObjectsByType<ProjectorDome>(FindObjectsInactive.Include))
                Object.DestroyImmediate(d.gameObject);
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
        }

        [Test]
        public void VolleyReachesProjectorBehindFourDomeProtectedRobots_MV1374()
        {
            var player = new GameObject("MV1374 Max") { tag = "Player" };
            player.transform.position = Origin;
            _spawned.Add(player);

            RobotEnemy projector = NewRobot(EnemyKind.Projector, Origin + new Vector3(0f, 0f, 8f));
            var shielded = new List<RobotEnemy>
            {
                NewRobot(EnemyKind.Rusher, Origin + new Vector3(-0.3f, 0f, 4f)),
                NewRobot(EnemyKind.Rusher, Origin + new Vector3(0.3f, 0f, 4.7f)),
                NewRobot(EnemyKind.Rusher, Origin + new Vector3(-0.3f, 0f, 5.4f)),
                NewRobot(EnemyKind.Rusher, Origin + new Vector3(0.3f, 0f, 6.1f)),
            };
            Physics.SyncTransforms();

            projector.Tick(0.05f); // first tick recomputes the dome centroid over the four Rushers
            Assert.IsTrue(projector.HasDome, "test setup: the Projector must be projecting a dome");

            float projectorBefore = projector.HealthCurrent;
            var shieldedBefore = new List<float>();
            foreach (var r in shielded) shieldedBefore.Add(r.HealthCurrent);

            var maxGo = new GameObject("MV1374 Launcher");
            maxGo.transform.position = Origin;
            maxGo.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            _spawned.Add(maxGo);
            var missile = maxGo.AddComponent<LightMissile>();
            FireVolley.Invoke(missile, null);

            const float step = 1f / 60f;
            for (int i = 0; i < Mathf.CeilToInt(3f / step); i++)
            {
                bool any = false;
                var pool = LightMissileBall.Pool;
                for (int b = 0; b < pool.Count; b++)
                {
                    if (!pool[b].IsActiveBall) continue;
                    any = true;
                    pool[b].Tick(step);
                }
                if (!any) break;
            }

            Assert.Less(projector.HealthCurrent, projectorBefore,
                "the Projector's resolved health must have dropped within 3 s of one volley");
            for (int i = 0; i < shielded.Count; i++)
                Assert.AreEqual(shieldedBefore[i], shielded[i].HealthCurrent, 1e-4f,
                    $"dome-protected Rusher {i}'s health must not change");
        }

        private RobotEnemy NewRobot(EnemyKind kind, Vector3 position)
        {
            var go = new GameObject("MV1374 " + kind);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            go.transform.position = position;
            e.Apply(EnemyArchetype.Of(kind));
            OnEnable.Invoke(e, null);
            HealthField.SetValue(e, 1000f);
            _spawned.Add(go);
            return e;
        }
    }
}
