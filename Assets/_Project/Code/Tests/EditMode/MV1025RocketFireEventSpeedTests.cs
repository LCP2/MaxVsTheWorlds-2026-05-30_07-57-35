using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1025 — Lee, TestFlight: "Speed rockets back up a little bit but not back to their original
    /// speed. I'm trying to make them a visual event, as at the moment they are hard to see." The
    /// Shoulder Rack's own private <c>RocketSpeed</c> constant goes from 10 (MV-1003) to 12 m/s.
    ///
    /// Reads that constant by reflection rather than duplicating its literal here — a hardcoded 12
    /// passed straight into <see cref="PlayerRocket.Fire"/> would pass identically whatever
    /// <see cref="ShoulderRack"/>'s own tuning says, which is not a guard on the tuning at all. Fires
    /// directly through <see cref="PlayerRocket.Fire"/> (not the rack's own Tick, per the ticket's own
    /// AC) at a target 30m away so it never gets anywhere near <c>ContactRadius</c> or fuel-out inside
    /// the measured window. Ticks the 0.25s launch arc first, then measures net displacement across
    /// exactly one further second (60 ticks at 1/60s) — a resolved value, following the same
    /// resolved-distance shape <c>MV1003ShoulderRackRocketSpeedTests</c> already established for this
    /// flight path.
    ///
    /// Proven to fail on `main` @ 967d1be (RocketSpeed still 10 there — reverted locally, ran, then
    /// restored to capture this): "expected ~12m of resolved net displacement in the 1s window
    /// following the launch arc, at the tuned 10m/s rocket speed, measured 10.07m. Expected: 12.0f
    /// +/- 0.5f But was: 10.0651703f".
    /// </summary>
    public sealed class MV1025RocketFireEventSpeedTests
    {
        private const float Dt = 1f / 60f;
        private const float LaunchDurationSeconds = 0.25f;

        private static readonly FieldInfo RocketSpeedField =
            typeof(ShoulderRack).GetField("RocketSpeed", BindingFlags.NonPublic | BindingFlags.Static);

        [TearDown]
        public void TearDown() => PlayerRocket.DestroyAllActive();

        // Guards MV-1025
        [Test]
        public void FiredRocket_CoversTheTunedSpeedInTheSecondAfterLaunch()
        {
            float rocketSpeed = (float)RocketSpeedField.GetValue(null);

            var robotGo = new GameObject("Rusher");
            robotGo.transform.position = new Vector3(0f, 0f, 30f);
            robotGo.AddComponent<CharacterController>();
            RobotEnemy robot = robotGo.AddComponent<RobotEnemy>();
            robot.Apply(EnemyArchetype.Rusher);

            PlayerRocket rocket = PlayerRocket.Fire(Vector3.zero, robot.transform, rocketSpeed,
                damage: 10f, splashRadius: 1.5f, cluster: false, launchYawRight: true);

            try
            {
                Physics.SyncTransforms();

                int launchTicks = Mathf.CeilToInt(LaunchDurationSeconds / Dt);
                for (int i = 0; i < launchTicks; i++) rocket.Tick(Dt);

                Assert.That(rocket, Is.Not.Null,
                    "the rocket must survive its 0.25s launch arc before the measured window starts");

                Vector3 start = rocket.transform.position;
                for (int i = 0; i < 60; i++) rocket.Tick(Dt);   // 60 * 1/60f = 1.0s simulated flight

                Assert.That(rocket, Is.Not.Null,
                    "the rocket must still be in flight - a 30m target is far outside both the 0.8m " +
                    "contact radius and the 4s fuel budget after just 1.25s total");

                float distance = Vector3.Distance(start, rocket.transform.position);
                Assert.That(distance, Is.EqualTo(12f).Within(0.5f),
                    $"expected ~12m of resolved net displacement in the 1s window following the launch " +
                    $"arc, at the tuned {rocketSpeed:0}m/s rocket speed, measured {distance:0.00}m");
            }
            finally
            {
                PlayerRocket.DestroyAllActive();
                Object.DestroyImmediate(robotGo);
            }
        }
    }
}
