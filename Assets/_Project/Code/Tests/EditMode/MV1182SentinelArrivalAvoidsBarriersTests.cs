using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1182 (Lee, device playtest 14 Oct): Sentinels arrived behind pipe runs and inside robot
    /// pens — stuck, with no way to recycle them. Base code's <c>TryBestArrivalCandidate</c> sampled a
    /// 2.5-4.5 m ring and kept whichever valid point sat FARTHEST from the nearest enemy, with nothing
    /// checking that Max himself could walk there in a straight line — so the search actively
    /// preferred the far side of a barrier. Lee's rule: a sentinel now arrives about one robot-length
    /// (1.5 m) behind Max, trying directly behind first and only widening the ANGLE, then the RADIUS
    /// (up to a 2.5 m hard max); there is no more farthest-from-enemy scoring.
    ///
    /// One test (MV-465 Rule 1), straight through the private search
    /// <see cref="PlayerAbilities.TryFindSentinelArrivalPoint"/> (reflective — same name/signature on
    /// both the base commit and this fix, so the only thing that can change is the point it resolves
    /// to). The public <c>TryDeploySentinelNearMax</c> path adds wallet/RIG/cooldown wiring this AC
    /// doesn't need to go through.
    ///
    /// Fixture: Max faces +X, open floor behind him (-X). A CoverLayer pen wall sits 1 m front-left —
    /// bait for the retired farthest-from-enemy scoring. An awake robot sits 6 m straight in front —
    /// something for that scoring to run away from. A second CoverLayer barrier plus the empty pocket
    /// 3.5 m beyond it, to Max's side, is exactly the shape of point the old algorithm would have
    /// picked (farthest from the front robot, line-of-sight passing clean over the barrier, across a
    /// span Max himself could never walk in a straight line) — all asserted against the REAL resolved
    /// point, never a constant.
    ///
    /// Must FAIL on base commit eaacbce: base's own <c>TryBestArrivalCandidate</c> never samples closer
    /// than <c>SentinelArrivalMinDistance</c> (2.5 m), so the resolved point is always >= 2.5 m from
    /// Max — outside the [1.4, 1.6] m band this test asserts. Exact failure output quoted in the fix
    /// comment.
    /// </summary>
    public sealed class MV1182SentinelArrivalAvoidsBarriersTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo BackyardPathMapField = typeof(BackyardPath).GetField("_map", NonPublicInstance);
        private static readonly FieldInfo PlayerControllerFacingField = typeof(PlayerController).GetField("_facing", NonPublicInstance);
        private static readonly MethodInfo TryFindArrivalPointMethod =
            typeof(PlayerAbilities).GetMethod("TryFindSentinelArrivalPoint", NonPublicInstance);
        private static readonly MethodInfo PlayerAbilitiesAwakeMethod = typeof(PlayerAbilities).GetMethod("Awake", NonPublicInstance);
        private static readonly FieldInfo RobotCcField = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly MethodInfo RobotOnEnableMethod = typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        private List<GameObject> _spawned;

        [SetUp]
        public void SetUp()
        {
            _spawned = new List<GameObject>();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            Sentinel.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            Sentinel.ResetRegistry();
        }

        private GameObject Track(GameObject go) { _spawned.Add(go); return go; }

        private GameObject NewCoverBox(Vector3 center, Vector3 size)
        {
            var go = Track(new GameObject("MV1182-Cover"));
            go.transform.position = center;
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            CoverLayer.Assign(go);
            return go;
        }

        /// <summary>Same construction idiom <c>MV1113SentinelButtonDeployTests.NewAwakeRobot</c> uses —
        /// Awake/OnEnable don't run reliably for a plain AddComponent outside Play mode.</summary>
        private RobotEnemy NewAwakeRobot(Vector3 position)
        {
            var go = Track(new GameObject("MV1182-Robot"));
            var cc = go.AddComponent<CharacterController>();
            var robot = go.AddComponent<RobotEnemy>();
            RobotCcField.SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            RobotOnEnableMethod.Invoke(robot, null);
            go.transform.position = position;
            return robot;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        [Test]
        public void ArrivalPointLandsOneRobotLengthDirectlyBehindMaxNotInTheBaitedSidePocket()
        {
            var origin = new Vector3(96234f, 0f, 66234f);

            // Open 40x40 floor so the direct-behind point, the front robot, the pen wall and the side
            // pocket are all on walkable ground inside Max's own area.
            var pathGo = Track(new GameObject("MV1182-backyard-path"));
            var path = pathGo.AddComponent<BackyardPath>();
            BackyardPathMapField.SetValue(path, new MapData { deckHeight = 2.5f, zones = new[]
            {
                new MapZone { id = "mv1182", x = origin.x, z = origin.z, width = 40f, depth = 40f, level = 0 },
            }});

            var maxGo = Track(new GameObject("MV1182-Max"));
            maxGo.transform.position = origin;
            var abilities = maxGo.AddComponent<PlayerAbilities>();
            var controller = maxGo.GetComponent<PlayerController>();
            PlayerControllerFacingField.SetValue(controller, new Vector3(1f, 0f, 0f)); // facing +X
            // Awake/OnEnable don't run reliably for a plain AddComponent outside Play mode (same idiom
            // MV1089CaptureConversionTests/MV1113SentinelButtonDeployTests already use elsewhere) — without
            // this, PlayerAbilities._playerController stays null and the arrival search silently falls
            // back to world -Z instead of reading the Facing just set above.
            PlayerAbilitiesAwakeMethod.Invoke(abilities, null);

            // Pen wall 1 m front-left of Max (front = +X, left = +Z here).
            NewCoverBox(origin + new Vector3(0.7f, 0f, 0.7f), new Vector3(1f, 1.6f, 1f));

            // An awake robot 6 m straight in front — bait for the retired farthest-from-enemy scoring.
            NewAwakeRobot(origin + new Vector3(6f, 0f, 0f));

            // A barrier 1.5 m to Max's side, with the empty pocket the old algorithm would have
            // preferred sitting 3.5 m beyond it (farthest from the front robot, LOS passing clean over
            // the barrier) — across a span Max himself could never walk in a straight line.
            NewCoverBox(origin + new Vector3(0f, 0f, -1.5f), new Vector3(2f, 1.6f, 0.4f));

            object[] args = { null };
            bool found = (bool)TryFindArrivalPointMethod.Invoke(abilities, args);
            Assert.That(found, Is.True, "an unobstructed direct-behind point must be found");
            Vector3 point = (Vector3)args[0];

            float distToMax = FlatDistance(point, origin);
            Assert.That(distToMax, Is.InRange(1.4f, 1.6f),
                $"must land about one robot-length (1.5 m) behind Max — was {distToMax}");

            var behindDir = new Vector3(-1f, 0f, 0f); // opposite of Facing (+X)
            Vector3 toPointDir = new Vector3(point.x - origin.x, 0f, point.z - origin.z).normalized;
            float angleFromBehind = Vector3.Angle(behindDir, toPointDir);
            Assert.That(angleFromBehind, Is.LessThanOrEqualTo(20f),
                $"must land within 20 degrees of directly behind Max — was {angleFromBehind} degrees");
        }
    }
}
