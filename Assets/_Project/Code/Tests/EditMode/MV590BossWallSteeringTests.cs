using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-590: after MV-386 (SafeMove) and MV-542 (collider fit to render), the boss still walked
    /// straight through a wall standing between it and Max — because neither of those addressed
    /// obstacle STEERING, only tunneling and collider sizing. <see cref="BigBermudaBoss.Approach"/> was
    /// a raw beeline through <c>SafeMove</c> with none of the wall-latch/slide-around wiring
    /// <see cref="MaxWorlds.Enemies.RobotEnemy"/> already carries (YT-68/MV-447). This pins that a boss
    /// ticked toward a target behind a wall never has its body bounds intersect the wall's bounds, and
    /// actually finds its way around it rather than stalling pressed against it.
    ///
    /// <c>OnControllerColliderHit</c> is confirmed (by probe) NOT to fire when
    /// <see cref="CharacterController.Move"/> is driven directly from an EditMode test with no running
    /// player loop — same reason <c>MV586ForceFieldRamTests</c> drives <c>RobotEnemy.HandleWallContact</c>
    /// via reflection instead of a real <see cref="ControllerColliderHit"/> (which has no public
    /// constructor anyway). This test does the same: it detects geometric contact with the wall itself
    /// each tick and feeds <see cref="BigBermudaBoss.HandleWallContact"/> directly, so what's actually
    /// under test is the wall-latch/steer wiring and SafeMove's real collision response, not Unity's
    /// physics-message dispatch (which does run normally in real play).
    /// </summary>
    public sealed class MV590BossWallSteeringTests
    {
        // Awake isn't reliably invoked for AddComponent outside Play mode (same note MV548MobileShedTests
        // carries) — drive it directly so _cc/_wallLatch/_preferSign actually exist before TickApproach
        // is exercised.
        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private static readonly MethodInfo HandleWallContactMethod =
            typeof(BigBermudaBoss).GetMethod("HandleWallContact", BindingFlags.NonPublic | BindingFlags.Instance);

        private const float Dt = 1f / 60f;

        // EditMode tests share one physics scene for the whole cc-verify run with no per-test reset — a
        // distinctive, far-off origin sidesteps colliding with another fixture's leftover geometry (same
        // reasoning as MV548MobileShedTests.RigOrigin). Every assertion below is relative to this rig.
        private static readonly Vector3 RigOrigin = new Vector3(-73042f, 0f, 55631f);

        [Test]
        public void BossNeverPenetratesAWallAndRoutesAroundItTowardTheTarget()
        {
            GameObject bossGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject wallGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                bossGo.transform.position = RigOrigin + new Vector3(0f, 0f, -8f);
                var stray = bossGo.GetComponent<BoxCollider>();
                if (stray != null) Object.DestroyImmediate(stray);
                var boss = bossGo.AddComponent<BigBermudaBoss>();
                InvokeAwake(boss);
                var cc = bossGo.GetComponent<CharacterController>();

                // A wall standing directly between the boss and its target, narrow enough to have an
                // edge the boss can actually reach within this test's tick budget.
                wallGo.transform.position = RigOrigin;
                wallGo.transform.localScale = new Vector3(3f, 3f, 0.4f);
                var wallCollider = wallGo.GetComponent<BoxCollider>();
                // autoSyncTransforms is off project-wide (DynamicsManager.asset) -- make the freshly
                // placed wall visible to the boss's very first physics query against it.
                Physics.SyncTransforms();

                Vector3 target = RigOrigin + new Vector3(0f, 0f, 8f);

                // CharacterController.Move stops just short of true penetration (skin width), so an
                // exact-bounds overlap check would never see the boss as "touching" the wall at all.
                // Generous slack over skin width catches it while still contact-range, close enough to
                // production where OnControllerColliderHit fires every frame of continuous grinding.
                const float ContactSlack = 0.3f;

                for (int i = 0; i < 3600; i++) // 60 s at MoveSpeed 0.9 m/s -- ample for a 16 m detour
                {
                    // The TRUE nearest-surface normal, not an assumed face -- the boss can graze the
                    // wall's front face, side face or corner while rounding it, and each gives a
                    // different real contact normal. An earlier version of this harness hardcoded a
                    // single "front-on" normal and fed the WRONG one once the boss was beside the wall
                    // rounding the corner, which let it clip into the side face.
                    Vector3 bossCenter = cc.bounds.center;
                    Vector3 closest = Physics.ClosestPoint(bossCenter, wallCollider,
                        wallGo.transform.position, wallGo.transform.rotation);
                    Vector3 diff = bossCenter - closest;
                    if (diff.magnitude <= cc.radius + ContactSlack && diff.sqrMagnitude > 1e-8f)
                        HandleWallContactMethod.Invoke(boss, new object[] { wallCollider, diff.normalized });

                    boss.TickApproach(Dt, target);

                    Assert.IsFalse(wallCollider.bounds.Intersects(cc.bounds),
                        $"boss body bounds must never intersect the wall's bounds (tick {i})");
                }

                // Merely closing SOME distance is not "found a way around" -- pressing into the wall's
                // near face and stalling there already closes most of the 16 m gap without ever routing
                // past it. What only a genuine detour can achieve is closing to the boss's own Standoff
                // from the target, on the FAR side of the wall.
                float endDistance = Vector3.Distance(bossGo.transform.position, target);
                Assert.That(endDistance, Is.LessThanOrEqualTo(BossTuning.Standoff + 0.5f),
                    "a boss blocked by a wall on the way to its target must find a way around it and " +
                    "close all the way to its standoff distance, not stall pressed against the wall's near face");
            }
            finally
            {
                Object.DestroyImmediate(bossGo);
                Object.DestroyImmediate(wallGo);
            }
        }
    }
}
