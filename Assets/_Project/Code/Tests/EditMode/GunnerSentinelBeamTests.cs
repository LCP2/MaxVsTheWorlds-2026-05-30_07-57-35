using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-395: the sentinel was damaging robots with no visible beam/stream at all — the targeting and
    /// damage logic (<see cref="Sentinel"/>'s Update) never drew anything, it only called
    /// <c>TakeDamage</c>. This proves a shot now actually fires a visible from the turret to the robot
    /// it just hit, not just that the robot takes damage (already covered by <see cref="SentinelTests"/>).
    /// MV-616 replaced the original bare LineRenderer tracer with a reused, scaled-down
    /// <see cref="WaterVfx"/> — this test was updated to match; see <see cref="SentinelBeamVfxTests"/>
    /// for the new VFX-specific acceptance criteria (particle presence, cleanup, particle budget).
    /// </summary>
    public sealed class GunnerSentinelBeamTests
    {
        [SetUp]
        [TearDown]
        public void Clear() => Sentinel.ResetRegistry();

        private static void InvokeUpdate(Sentinel sentinel)
        {
            typeof(Sentinel).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(sentinel, null);
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
        public void FiringAtARobotBuildsTheWaterVfxAtTheTurretsMuzzleFacingTheHit()
        {
            var sentinelGo = new GameObject("Sentinel");
            var sentinel = sentinelGo.AddComponent<Sentinel>();
            RobotEnemy target = null;
            try
            {
                sentinel.Init(Vector3.zero, 60f, 7f, 0.6f, moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
                target = NewTarget(new Vector3(2f, 0f, 0f));

                // autoSyncTransforms is off project-wide — every transform set above needs an explicit
                // sync before the physics overlap query in Sentinel.Update runs, same as
                // WaterBlasterGateDamageTests/GateSolidityTests.
                Physics.SyncTransforms();

                InvokeUpdate(sentinel);

                var originGo = sentinel.transform.Find("BeamOrigin");
                Assert.IsNotNull(originGo, "no BeamOrigin child was built — the shot is still invisible");

                var vfx = originGo.GetComponent<WaterVfx>();
                Assert.IsNotNull(vfx, "BeamOrigin must carry the reused WaterVfx, not a hand-authored effect");

                // The turret's own root rotation (set in Update, right before FireBeam) is what aims the
                // water — BeamOrigin sits at identity local rotation, so its world forward already
                // points at the hit.
                Vector3 toTarget = target.transform.position - sentinel.transform.position;
                toTarget.y = 0f;
                float angle = Vector3.Angle(sentinel.transform.forward, toTarget.normalized);
                Assert.That(angle, Is.LessThan(1f), "the turret must be aimed at the robot it just hit");
            }
            finally
            {
                Object.DestroyImmediate(sentinelGo);
                if (target != null) Object.DestroyImmediate(target.gameObject);
            }
        }
    }
}
