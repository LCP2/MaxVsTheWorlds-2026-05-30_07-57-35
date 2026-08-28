using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-611 — <c>DissolveVfx.SnapshotLiveEnemies</c> called <c>GetComponentInChildren&lt;MeshFilter&gt;()</c>
    /// for every live enemy every <c>LateUpdate</c>, even though a robot's body never changes shape once
    /// spawned (the spawner pools strictly by kind — <c>RobotEnemy.Kind</c>'s own doc comment). Fixed to
    /// cache the lookup per robot instance (<see cref="DissolveVfx"/>'s own doc comment).
    ///
    /// Same reflection idiom as <c>MV531DissolveSnapshotTests</c>: <c>RobotEnemy.Active</c> is only
    /// populated by <c>OnEnable</c>, which Unity does not reliably invoke for <c>AddComponent</c> outside
    /// Play mode, so it's invoked directly here.
    ///
    /// Must fail to COMPILE on the pre-fix commit: <c>DissolveVfx._meshFilterCacheMisses</c> did not
    /// exist before this ticket — the same "fails on the base commit" the project's testing policy
    /// accepts, per <c>ZoneRouteGridTests</c>' own doc comment precedent.
    /// </summary>
    public sealed class MV611DissolveMeshFilterCacheTests
    {
        private static readonly MethodInfo SnapshotMethod = typeof(DissolveVfx).GetMethod(
            "SnapshotLiveEnemies", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo MissesField = typeof(DissolveVfx).GetField(
            "_meshFilterCacheMisses", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod = typeof(RobotEnemy).GetMethod(
            "OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp() => RobotEnemy.ResetRegistry();

        [TearDown]
        public void TearDown() => RobotEnemy.ResetRegistry();

        /// <summary><c>CreatePrimitive(Capsule)</c> comes with its own MeshFilter — exactly what
        /// <c>SnapshotLiveEnemies</c> looks up.</summary>
        private static RobotEnemy NewRobot(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.position = position;
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            e.Apply(EnemyArchetype.Rusher);
            OnEnableMethod.Invoke(e, null);
            return e;
        }

        [Test]
        public void SnapshotLiveEnemies_CachesPerRobotMeshFilterLookup_NotRepeatedEveryFrame()
        {
            Assert.IsNotNull(MissesField,
                "DissolveVfx._meshFilterCacheMisses went missing — the instrumentation this test guards (MV-611)");

            RobotEnemy robot = NewRobot(new Vector3(2f, 0f, 2f));
            var vfxGo = new GameObject("MV611-dissolve-cache-test");
            var vfx = vfxGo.AddComponent<DissolveVfx>();

            try
            {
                SnapshotMethod.Invoke(vfx, null);
                SnapshotMethod.Invoke(vfx, null);
                SnapshotMethod.Invoke(vfx, null);

                int misses = (int)MissesField.GetValue(vfx);
                Assert.AreEqual(1, misses,
                    "SnapshotLiveEnemies must look up a robot's MeshFilter once and cache it, not " +
                    "repeat GetComponentInChildren every LateUpdate for every live enemy");
            }
            finally
            {
                Object.DestroyImmediate(vfxGo);
                Object.DestroyImmediate(robot.gameObject);
                RobotEnemy.ResetRegistry();
            }
        }
    }
}
