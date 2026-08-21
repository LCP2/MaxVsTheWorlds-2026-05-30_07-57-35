using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-531 — <c>DissolveVfx.SnapshotLiveEnemies</c> called <c>FindObjectsByType&lt;RobotEnemy&gt;()</c>
    /// every <c>LateUpdate</c>, the same per-frame scene-scan shape MV-527 removed from five other VFX
    /// directors. Fixed to read <see cref="RobotEnemy.Active"/> — the registry every other MV-527
    /// conversion already reads — instead.
    ///
    /// This does not merely reassert "same robots selected" in the ordinary case — both implementations
    /// already agree there, so a test that only checked that would pass unchanged before this fix and
    /// would not be evidence (CC_AUTONOMY's testing policy). It targets the one case where a raw scene
    /// scan and the registry provably diverge: <see cref="RobotEnemy.ResetRegistry"/>'s own doc comment
    /// names it "belt-and-braces against a robot whose OnDisable hasn't run yet" — i.e. a robot can still
    /// be active and alive in the scene after the registry has already forgotten it. A scene scan (the
    /// pre-fix code) still finds that stale robot; the registry (the fix) correctly does not.
    ///
    /// Must fail on 94d31da (MV-527, the commit before this fix), where SnapshotLiveEnemies scans the
    /// scene directly via FindObjectsByType and picks the stale robot up alongside the current one.
    /// </summary>
    public sealed class MV531DissolveSnapshotTests
    {
        private static readonly MethodInfo SnapshotMethod = typeof(DissolveVfx).GetMethod(
            "SnapshotLiveEnemies", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo SnapshotsField = typeof(DissolveVfx).GetField(
            "_snapshots", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PositionField = typeof(DissolveVfx)
            .GetNestedType("Snapshot", BindingFlags.NonPublic)
            .GetField("Position");

        [SetUp]
        public void SetUp() => RobotEnemy.ResetRegistry();

        [TearDown]
        public void TearDown() => RobotEnemy.ResetRegistry();

        private static readonly MethodInfo OnEnableMethod = typeof(RobotEnemy).GetMethod(
            "OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>RobotEnemy.Active is only populated by OnEnable, which Unity does not reliably
        /// invoke for AddComponent outside Play mode — so it's invoked directly here, the same
        /// reflection idiom SentinelPlacementTests.AimedDeployIsRejectedWhenThePointOverlapsALiveRobot
        /// already established for exactly this reason.</summary>
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
        public void SnapshotLiveEnemies_MatchesTheRegistry_NotAStaleRobotTheRegistryHasAlreadyForgotten()
        {
            var stale = NewRobot(new Vector3(1f, 0f, 1f));

            // Simulate the exact scenario RobotEnemy.ResetRegistry's own doc comment names: the
            // registry is cleared at a level/test boundary while `stale`'s OnDisable hasn't run yet —
            // it is still active and alive in the scene, just no longer in RobotEnemy.Active.
            RobotEnemy.ResetRegistry();

            var current = NewRobot(new Vector3(5f, 0f, 5f));

            var vfxGo = new GameObject("DissolveVfxUnderTest");
            var vfx = vfxGo.AddComponent<DissolveVfx>();

            try
            {
                SnapshotMethod.Invoke(vfx, null);
                var snapshots = (IList)SnapshotsField.GetValue(vfx);

                Assert.AreEqual(1, snapshots.Count,
                    "SnapshotLiveEnemies must match RobotEnemy.Active exactly - it must not also pick " +
                    "up a robot the registry has already forgotten, even though that robot is still " +
                    "active and alive in the scene.");

                var snapshotPosition = (Vector3)PositionField.GetValue(snapshots[0]);
                Assert.AreEqual(current.transform.position, snapshotPosition,
                    "The one snapshot present must be the robot RobotEnemy.Active still knows about, " +
                    "not the stale one the registry has already forgotten.");
            }
            finally
            {
                Object.DestroyImmediate(vfxGo);
                Object.DestroyImmediate(stale.gameObject);
                Object.DestroyImmediate(current.gameObject);
                RobotEnemy.ResetRegistry();
            }
        }
    }
}
