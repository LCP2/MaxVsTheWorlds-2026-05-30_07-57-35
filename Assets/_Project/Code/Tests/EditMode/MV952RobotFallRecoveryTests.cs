using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-952 (Lee, TestFlight 0.9.9): "Max can teleport out of the playable area... Every nearby robot
    /// and all Sentinels fell with him and kept falling; no recovery." MV-946 gave Max and every Sentinel
    /// a <see cref="FallRecoveryState"/> — <see cref="RobotEnemy"/> never got one, so a robot that ends
    /// up out of play (whatever the cause — a gate threshold, a knockback shove, a pull) just keeps
    /// falling forever while everything else around it recovers. This ticket's own AC2: "a robot pushed
    /// below the floor is recovered within 1 s".
    ///
    /// Must FAIL on the pre-fix commit: base <c>RobotEnemy</c> carries no <c>_fallRecovery</c> field and
    /// never calls it from <c>Tick</c>, so a robot forced below the floor and ticked past the 1 s window
    /// stays exactly where it was pushed — this test's own probe never recovers because there is nothing
    /// wired up to do so.
    ///
    /// Tier 2 (resolved value): asserts the robot's actual, physics-resolved Y position after ticking —
    /// never an authored constant, never a presence check. Same reflection idiom as
    /// <c>MV870DormantFarTickThrottleTests</c> (<c>RobotEnemy.Tick</c> never runs outside Play mode, so
    /// it's invoked directly with an explicit dt) since driving a live game loop here is out of bounds
    /// (CC_AUTONOMY.md forbids PlayMode/live builds).
    /// </summary>
    public sealed class MV952RobotFallRecoveryTests
    {
        private GameObject _pathGo;
        private GameObject _floorGo;
        private RobotEnemy _robot;

        private static readonly MethodInfo TickMethod =
            typeof(RobotEnemy).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo TargetField =
            typeof(RobotEnemy).GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PlayerTargetField =
            typeof(RobotEnemy).GetField("_playerTarget", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
            if (_robot != null) Object.DestroyImmediate(_robot.gameObject);
            if (_floorGo != null) Object.DestroyImmediate(_floorGo);
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
        }

        private void InstallMap(MapData map)
        {
            _pathGo = new GameObject("MV952-robot-fall-recovery-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            FieldInfo mapField = typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            mapField.SetValue(path, map);
        }

        private static void TickOnce(RobotEnemy e, float dt) => TickMethod.Invoke(e, new object[] { dt });

        [Test]
        public void RobotPushedBelowTheFloor_IsRecoveredWithinOneSecond()
        {
            // Well off the origin -- several other EditMode tests build throwaway geometry at/near (0,0,0)
            // without necessarily tearing it down before this one runs in the same shared batch-mode
            // scene, and a stray leftover collider under the robot would silently corrupt the "resting
            // height" this test measures. A remote coordinate makes that class of cross-test collision
            // structurally impossible instead of hoping every other test cleans up.
            const float ox = 91234f, oz = -84521f;

            var map = new MapData
            {
                zones = new[] { new MapZone { id = "area1", x = ox, z = oz, width = 20f, depth = 20f, level = 0 } },
            };
            InstallMap(map);

            // A real physical floor, so the robot's own CharacterController.isGrounded genuinely reads
            // true while standing on it and false once teleported below it (Rule 2/Tier 2 — a resolved
            // value from real physics, not an asserted bool).
            _floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floorGo.transform.position = new Vector3(ox, -0.05f, oz);
            _floorGo.transform.localScale = new Vector3(20f, 0.1f, 20f);
            Physics.SyncTransforms();

            var go = new GameObject("MV952-robot");
            go.transform.position = new Vector3(ox, 0f, oz);
            var cc = go.AddComponent<CharacterController>();
            // Centered above the transform's own origin (RobotEnemy.BuildBody's own convention, e.g.
            // "cc.center = Vector3.zero" against a body whose transform already sits at GroundedCenter)
            // so a grounded robot's transform.position.y itself reads near floor height (0), matching
            // FallSafetyNet.IsOutOfPlay's own "position.y" convention — not Unity's raw CharacterController
            // default (center at the transform's own origin), which would rest at y = height/2.
            cc.height = 1.8f;
            cc.radius = 0.5f;
            cc.center = Vector3.up * (cc.height * 0.5f);
            _robot = go.AddComponent<RobotEnemy>();
            CcField.SetValue(_robot, cc);
            _robot.ResetState(); // EditMode has no Awake/OnEnable lifecycle — init explicitly.
            // No "Player" tag exists anywhere in this test — leave target/_playerTarget null so
            // TickChase's own "acquire and return" path contributes no steering, same as
            // MV870DormantFarTickThrottleTests' "chasing" robot.
            TargetField.SetValue(_robot, null);
            PlayerTargetField.SetValue(_robot, null);

            const float dt = 1f / 60f;

            // Grounded ticks first, until it actually settles, to bank the safe position (matches
            // MV946FallRecoveryTests' own "one grounded tick banks it" setup) -- a fixed tick count would
            // assume a specific settle speed the CharacterController's own skin-width resolution doesn't
            // promise.
            for (int i = 0; i < 30 && !(cc.isGrounded && Mathf.Abs(go.transform.position.y) <= 0.2f); i++)
                TickOnce(_robot, dt);
            Assert.IsTrue(cc.isGrounded, "sanity: the robot must actually be resting on the real floor collider");
            Assert.LessOrEqual(Mathf.Abs(go.transform.position.y), 0.2f, "sanity: the robot must be at floor height before the push");

            // Push it below the floor, the way a Change #2 scenario in the ticket describes (a knockback,
            // a gate-threshold gap, a pull — the mechanism doesn't matter, only that it ends up out of
            // play). Disable/re-enable around the direct position write, same idiom every other teleport
            // in this codebase uses so the CharacterController's cached internal state doesn't fight it.
            cc.enabled = false;
            go.transform.position = new Vector3(ox, -5f, oz);
            cc.enabled = true;
            Physics.SyncTransforms();

            // Up to 1 s of ticking (well past MV-946's own 0.5 s grace window) -- the robot must recover
            // to floor height at some point in this window, not still be falling at the end of it.
            const int framesInOneSecond = 60;
            bool recoveredWithinOneSecond = false;
            for (int i = 0; i < framesInOneSecond; i++)
            {
                TickOnce(_robot, dt);
                if (Mathf.Abs(go.transform.position.y) <= 0.2f)
                {
                    recoveredWithinOneSecond = true;
                    break;
                }
            }

            Assert.IsTrue(recoveredWithinOneSecond,
                $"MV-952: a robot pushed below the floor must be recovered within 1 s; after {framesInOneSecond} " +
                $"ticks it sat at y={go.transform.position.y:F2}");
        }
    }
}
