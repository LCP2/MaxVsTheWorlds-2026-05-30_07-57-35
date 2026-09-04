using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-667: <see cref="MV590BossWallSteeringTests"/> already pins that <see cref="WallLatch"/> alone
    /// escapes a single FLAT wall — the boss reaches the wall's open end and rounds it. What it cannot
    /// do is escape CONCAVE geometry: a "U" of three walls (back + two flanking sides) that forms an
    /// interior corner. Sliding into that corner combines both walls' normals into one that points
    /// back out into the pocket, not around it, so a pure slide oscillates against the corner instead
    /// of finding the pocket's own mouth — there is no route, only a direction and a slide (see the
    /// ticket's own diagnosis, and <see cref="BigBermudaBoss.TickApproach"/>'s doc comment).
    ///
    /// This proves the fix: routing <see cref="BigBermudaBoss.TickApproach"/>'s bearing through
    /// <see cref="EnemyNavigation.Waypoint"/> (in-room A* around the pocket's own cover, exactly the
    /// grid <see cref="MaxWorlds.Enemies.RobotEnemy"/>'s melee Chase already uses) gets the boss OUT the
    /// mouth and around to the target, ending up close to its standoff — while the identical geometry,
    /// identical start/target and identical WallLatch/SafeMove code, with no map registered (so
    /// <see cref="EnemyNavigation.Waypoint"/> falls back to the raw bearing — the exact behaviour this
    /// ticket replaces), never gets meaningfully closer at all.
    ///
    /// Before this ticket, <see cref="BigBermudaBoss.TickApproach"/> never consults
    /// <see cref="EnemyNavigation"/> at all, so registering a map changes nothing — the "routed" and
    /// "beeline" runs below execute byte-identical code against byte-identical geometry and produce the
    /// same distance, and <c>Assert.Less</c> of a value against itself fails deterministically. Quoted
    /// failure output (base commit fd6bfc7, before this ticket's change):
    ///   routed ended 6.50m from target, no closer than the beeline's 6.50m — the boss never found a
    ///   way out of the pocket at all
    ///   Expected: less than 6.5d
    ///   But was:  6.5d
    /// </summary>
    public sealed class MV667BossConcaveRoutingTests
    {
        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private static readonly MethodInfo HandleWallContactMethod =
            typeof(BigBermudaBoss).GetMethod("HandleWallContact", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo MapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        private const float Dt = 1f / 60f;
        private const int Ticks = 3600; // 60 s at MoveSpeed 0.9 m/s -- ample for the ~20 m detour out and around

        // EditMode tests share one physics scene for the whole cc-verify run with no per-test reset —
        // a distinctive, far-off origin sidesteps colliding with another fixture's leftover geometry
        // (same reasoning as MV590BossWallSteeringTests.RigOrigin).
        private static readonly Vector3 RigOrigin = new Vector3(91042f, 0f, -63021f);

        // Reset on both sides: EditMode tests share one static EnemyNavigation cache for the whole
        // cc-verify run with no per-test reset, so a stale _map/_looked left by an earlier test (or by
        // this one, on the second run below) must never leak in either direction.
        [SetUp]
        public void SetUp() => EnemyNavigation.Reset();

        [TearDown]
        public void TearDown() => EnemyNavigation.Reset();

        /// <summary>A "U" pocket open only to the south: a back wall and two flanking side walls whose
        /// inner faces meet the back wall's ends at a concave (interior) corner. The boss starts deep
        /// inside it; the target sits due north, beyond the back wall — reachable only by walking out
        /// the mouth (south, AWAY from the target) and around one of the flanks.
        ///
        /// <see cref="MapZone"/>/<see cref="MapEntity"/> coordinates are plain world-space numbers, not
        /// relative to anything — so they are authored directly in <paramref name="origin"/>-space to
        /// land exactly under the physical wall colliders <see cref="BuildWall"/> builds at that same
        /// origin, which is what lets <see cref="MapData.ZoneAt"/> actually recognise the boss's real
        /// <c>transform.position</c> as standing inside this zone.</summary>
        private static MapData PocketMap(Vector3 origin, out Rect backFp, out Rect leftFp, out Rect rightFp)
        {
            var room = new MapZone { id = "pocket", x = origin.x, z = origin.z, width = 20f, depth = 20f };
            var map = new MapData
            {
                zones = new[] { room },
                entities = new[]
                {
                    new MapEntity { id = "back", kind = "cover", dressing = "hedge",
                        x = origin.x, z = origin.z + 3f, width = 8f, height = 2f, depth = 1f },
                    new MapEntity { id = "left", kind = "cover", dressing = "hedge",
                        x = origin.x - 4.5f, z = origin.z + 1f, width = 1f, height = 2f, depth = 5f },
                    new MapEntity { id = "right", kind = "cover", dressing = "hedge",
                        x = origin.x + 4.5f, z = origin.z + 1f, width = 1f, height = 2f, depth = 5f },
                },
            };
            backFp = new Rect(origin.x - 4f, origin.z + 2.5f, 8f, 1f);
            leftFp = new Rect(origin.x - 5f, origin.z - 1.5f, 1f, 5f);
            rightFp = new Rect(origin.x + 4f, origin.z - 1.5f, 1f, 5f);
            return map;
        }

        private static GameObject BuildWall(Rect footprintXz, float height)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.position = new Vector3(footprintXz.center.x, height * 0.5f, footprintXz.center.y);
            go.transform.localScale = new Vector3(footprintXz.width, height, footprintXz.height);
            return go;
        }

        private static BigBermudaBoss BuildBoss(Vector3 position, out GameObject go, out CharacterController cc)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.position = position;
            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            var boss = go.AddComponent<BigBermudaBoss>();
            InvokeAwake(boss);
            cc = go.GetComponent<CharacterController>();
            return boss;
        }

        /// <summary>Ticks <paramref name="boss"/> toward <paramref name="target"/> for <see cref="Ticks"/>
        /// steps, feeding real geometric wall contact into <see cref="BigBermudaBoss.HandleWallContact"/>
        /// each tick (the same reflection-driven probe <see cref="MV590BossWallSteeringTests"/> uses,
        /// since <c>OnControllerColliderHit</c> does not fire from a bare <c>CharacterController.Move</c>
        /// outside a running player loop) against every collider in <paramref name="walls"/>.</summary>
        private static void RunApproach(BigBermudaBoss boss, CharacterController cc, Vector3 target,
                                         BoxCollider[] walls)
        {
            const float ContactSlack = 0.3f;

            for (int i = 0; i < Ticks; i++)
            {
                Vector3 bossCenter = cc.bounds.center;
                foreach (BoxCollider wall in walls)
                {
                    Vector3 closest = Physics.ClosestPoint(bossCenter, wall,
                        wall.transform.position, wall.transform.rotation);
                    Vector3 diff = bossCenter - closest;
                    if (diff.magnitude <= cc.radius + ContactSlack && diff.sqrMagnitude > 1e-8f)
                        HandleWallContactMethod.Invoke(boss, new object[] { wall, diff.normalized });
                }

                boss.TickApproach(Dt, target);
            }
        }

        [Test]
        public void TickApproach_RoutedAroundTheConcavePocket_EndsCloserThanTheUnroutedSteerDoes()
        {
            MapData map = PocketMap(RigOrigin, out Rect backFp, out Rect leftFp, out Rect rightFp);

            GameObject pathGo = null, wallBack = null, wallLeft = null, wallRight = null;
            GameObject routedBossGo = null, beelineBossGo = null;
            try
            {
                // ---- geometry: identical for both runs -----------------------------------------
                wallBack = BuildWall(backFp, 2f);
                wallLeft = BuildWall(leftFp, 2f);
                wallRight = BuildWall(rightFp, 2f);
                var walls = new[]
                {
                    wallBack.GetComponent<BoxCollider>(),
                    wallLeft.GetComponent<BoxCollider>(),
                    wallRight.GetComponent<BoxCollider>(),
                };
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide

                Vector3 start = RigOrigin + new Vector3(0f, 0f, 1.5f);   // deep inside the pocket
                Vector3 target = RigOrigin + new Vector3(0f, 0f, 8f);    // beyond the back wall

                // ---- run 1: a live BackyardPath carries the pocket map -> EnemyNavigation routes it
                pathGo = new GameObject("BackyardPath (MV-667 test)");
                var path = pathGo.AddComponent<BackyardPath>();
                MapField.SetValue(path, map);

                var routedBoss = BuildBoss(start, out routedBossGo, out CharacterController routedCc);
                RunApproach(routedBoss, routedCc, target, walls);
                float routedDistance = Vector3.Distance(routedBossGo.transform.position, target);

                // ---- run 2: no map registered -> EnemyNavigation.Waypoint falls back to the raw
                // bearing, the exact pre-ticket behaviour (WallLatch/ObstacleSteering alone).
                Object.DestroyImmediate(pathGo);
                pathGo = null;
                EnemyNavigation.Reset();

                var beelineBoss = BuildBoss(start, out beelineBossGo, out CharacterController beelineCc);
                RunApproach(beelineBoss, beelineCc, target, walls);
                float beelineDistance = Vector3.Distance(beelineBossGo.transform.position, target);

                Assert.That(beelineDistance, Is.GreaterThan(BossTuning.Standoff + 2f),
                    $"the un-routed control (WallLatch/ObstacleSteering alone, no map) ended " +
                    $"{beelineDistance:F2}m from target — this fixture is supposed to be a genuine " +
                    "concave trap for it; if it also escapes, the comparison below proves nothing");

                Assert.Less(routedDistance, beelineDistance,
                    $"routed ended {routedDistance:F2}m from target, no closer than the beeline's " +
                    $"{beelineDistance:F2}m — the boss never found a way out of the pocket at all");

                Assert.That(routedDistance, Is.LessThanOrEqualTo(BossTuning.Standoff + 0.5f),
                    "the routed boss must actually close all the way to its standoff distance on the " +
                    "far side of the pocket, not merely drift a little closer than the trapped run");
            }
            finally
            {
                if (pathGo != null) Object.DestroyImmediate(pathGo);
                if (wallBack != null) Object.DestroyImmediate(wallBack);
                if (wallLeft != null) Object.DestroyImmediate(wallLeft);
                if (wallRight != null) Object.DestroyImmediate(wallRight);
                if (routedBossGo != null) Object.DestroyImmediate(routedBossGo);
                if (beelineBossGo != null) Object.DestroyImmediate(beelineBossGo);
                EnemyNavigation.Reset();
            }
        }
    }
}
