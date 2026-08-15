using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-395: the Gunner Sentinel was damaging robots with no visible beam/stream at all — the
    /// targeting and damage logic (<see cref="GunnerSentinel"/>'s Update) never drew anything, it only
    /// called <c>TakeDamage</c>. This proves a shot now actually draws a tracer from the turret to the
    /// robot it just hit, not just that the robot takes damage (already covered by <see
    /// cref="SentinelTests"/>).
    /// </summary>
    public sealed class GunnerSentinelBeamTests
    {
        [SetUp]
        [TearDown]
        public void Clear() => Sentinel.ResetRegistry();

        private static void InvokeUpdate(GunnerSentinel gunner)
        {
            typeof(GunnerSentinel).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(gunner, null);
        }

        private static RobotEnemy NewTarget(Vector3 position)
        {
            var go = new GameObject("Target Robot");
            go.transform.position = position;
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            e.ResetState(); // EditMode has no Awake/OnEnable lifecycle — init explicitly
            return e;
        }

        [Test]
        public void FiringAtARobotDrawsAVisibleTracerFromTheTurretToTheHit()
        {
            var gunnerGo = new GameObject("Gunner Sentinel");
            var gunner = gunnerGo.AddComponent<GunnerSentinel>();
            RobotEnemy target = null;
            try
            {
                gunner.Init(Vector3.zero, 60f, 7f, 0.6f);
                target = NewTarget(new Vector3(2f, 0f, 0f));

                // autoSyncTransforms is off project-wide — every transform set above needs an explicit
                // sync before the physics overlap query in GunnerSentinel.Update runs, same as
                // WaterBlasterGateDamageTests/GateSolidityTests.
                Physics.SyncTransforms();

                InvokeUpdate(gunner);

                var beamGo = gunner.transform.Find("Beam");
                Assert.IsNotNull(beamGo, "no Beam child was built — the shot is still invisible");

                var beam = beamGo.GetComponent<LineRenderer>();
                Assert.IsNotNull(beam);
                Assert.IsTrue(beam.enabled, "the tracer must be visible the instant a shot lands");

                Vector3 start = beam.GetPosition(0);
                Vector3 end = beam.GetPosition(1);
                Assert.That(start.x, Is.EqualTo(0f).Within(1e-3f), "the tracer must originate at the turret");
                Assert.That(start.z, Is.EqualTo(0f).Within(1e-3f));
                Assert.That(end.x, Is.EqualTo(2f).Within(1e-3f), "the tracer must end at the target it hit");
                Assert.That(end.z, Is.EqualTo(0f).Within(1e-3f));
            }
            finally
            {
                Object.DestroyImmediate(gunnerGo);
                if (target != null) Object.DestroyImmediate(target.gameObject);
            }
        }
    }
}
