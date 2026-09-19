using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-842 — the Shoulder Rack rocket used to fly level from its ~1m spawn height, aimed flat at
    /// the target (<see cref="PlayerRocket.Fire"/>), while its hit test was a 3D distance against the
    /// target's own transform (<see cref="HomingSteering.TurnToward"/> only ever turns it, never climbs
    /// or dives it). It could never close inside the old 0.4m ContactRadius, so it circled at its
    /// minimum turn radius (14 m/s / 120 deg/s ~ 6.7m) until its 4s fuel ran out, instead of striking
    /// the nearby robot it had locked onto.
    ///
    /// Proven to fail on the pre-fix code (HEAD at the time this test was written, unchanged since
    /// 10fce02 for anything rocket-related — <c>git diff 10fce02 HEAD --stat</c> on PlayerRocket.cs/
    /// ShoulderRack.cs/HomingSteering.cs is empty): this test doesn't compile against that commit —
    /// <c>PlayerRocket.Fire</c> had no <c>launchYawRight</c> parameter and no public <c>Tick</c> existed
    /// to drive the flight deterministically (CS1739/CS1061) — see the fix comment for the captured
    /// compiler output.
    /// </summary>
    public sealed class MV842RocketHomingTests
    {
        private const float Dt = 1f / 60f;
        private const float RocketSpeed = 14f;

        [TearDown]
        public void TearDown() => PlayerRocket.DestroyAllActive();

        [Test]
        public void ARocketFiredNinetyDegreesOffFacing_DetonatesNearTheRobotWithinOnePointFiveSeconds()
        {
            var robotGo = new GameObject("Rusher");
            robotGo.transform.position = new Vector3(5f, 0f, 0f);
            robotGo.AddComponent<CharacterController>();
            RobotEnemy robot = robotGo.AddComponent<RobotEnemy>();
            robot.Apply(EnemyArchetype.Rusher);

            // Max faces +Z (forward); the robot sits on +X, 90 degrees off that facing.
            PlayerRocket rocket = PlayerRocket.Fire(new Vector3(0f, 1f, 0f), robot.transform,
                RocketSpeed, damage: 10f, splashRadius: 1.5f, cluster: false, launchYawRight: true);

            try
            {
                Physics.SyncTransforms();

                bool detonated = false;
                Vector3 posBeforeFinalTick = rocket.transform.position;
                for (int i = 0; i < 90; i++)   // 90 * 1/60f = 1.5s simulated
                {
                    posBeforeFinalTick = rocket.transform.position;
                    rocket.Tick(Dt);
                    if (rocket == null) { detonated = true; break; }
                }

                Assert.IsTrue(detonated,
                    "the rocket never detonated within 1.5s of simulated flight against a robot 5m " +
                    "away and 90 degrees off Max's facing — it is still in flight (orbiting) instead " +
                    "of homing in and striking it.");

                Vector3 delta = posBeforeFinalTick - robotGo.transform.position;
                delta.y = 0f;
                // A generous bound on top of the 0.8m contact radius: the tick that triggers
                // detonation can travel up to speed*Dt (~0.23m) past the position sampled just before
                // it, and this is sampled BEFORE that final tick's own movement.
                Assert.LessOrEqual(delta.magnitude, 0.8f + RocketSpeed * Dt,
                    $"the rocket detonated {delta.magnitude:0.00}m horizontally from the robot it was " +
                    "homing on — expected to close inside its contact radius, not detonate off in an " +
                    "orbit or on unrelated geometry.");
            }
            finally
            {
                PlayerRocket.DestroyAllActive();
                Object.DestroyImmediate(robotGo);
            }
        }
    }
}
