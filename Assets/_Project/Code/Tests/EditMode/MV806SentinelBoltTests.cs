using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-806: the Sentinel deliberately fired the SAME water VFX Max's own RCDA fires — a friendly
    /// turret spraying a bigger, brighter jet than the player's own weapon upstaged him (Lee, device,
    /// 2026-09-15). This proves the fix in one test (four facets of the same regression — the turret's
    /// shot identity — not independent regressions, per the testing policy's "one new test per ticket"
    /// rule): (1) the bolt renderer's resolved tint is the world's hazard red, and the sentinel's own
    /// eye matches it; (2) the bolt's resolved world bounds are between 0.6x and 0.8x Max's own LPPE
    /// bolt's, measured by building one of each in this same test; (3) sampled across its flight, the
    /// bolt's position never strays more than 0.02m from the straight line muzzle -> impact; (4) no
    /// <see cref="WaterVfx"/> component exists anywhere under the sentinel after firing.
    ///
    /// Fails on base commit f89c4e7 (today): firing builds a "BeamOrigin" child carrying a
    /// <see cref="WaterVfx"/> (a cyan cone), not a <see cref="SentinelBolt"/> at all — every assertion
    /// below is unsatisfiable against that commit (no SentinelBolt type even exists yet).
    /// </summary>
    public sealed class MV806SentinelBoltTests
    {
        [SetUp]
        [TearDown]
        public void Clear() => Sentinel.ResetRegistry();

        private static void InvokeUpdate(Sentinel sentinel)
        {
            typeof(Sentinel).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(sentinel, null);
        }

        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private static RobotEnemy NewTarget(Vector3 position)
        {
            var go = new GameObject("Target Robot");
            go.transform.position = position;
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            // MV-832: NearestRobotInRange now reads RobotEnemy.Active, not a physics query — OnEnable
            // (not just ResetState) is what registers a robot into that list, same idiom
            // MV795FloodRobotImmunityTests already uses.
            RobotOnEnableMethod.Invoke(e, null);
            return e;
        }

        private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float t = Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude);
            t = Mathf.Clamp01(t);
            return a + ab * t;
        }

        // Well clear of the origin/small coordinates other fixtures in this shared 1877-test EditMode
        // run use for their own sentinel/robot pairs — same reason SentinelBeamVfxTests spreads ITS
        // OWN pairs 40m apart: a physics OverlapSphere range query (NearestRobotInRange) can otherwise
        // pick up another fixture's leftover collider as "nearest" instead of this test's own target.
        private static readonly Vector3 ScenarioOrigin = new Vector3(4000f, 0f, 4000f);

        [Test]
        public void SentinelFiresARedBoltSmallerThanMaxsOnAStraightLine_NeverWater()
        {
            var sentinelGo = new GameObject("Sentinel");
            var sentinel = sentinelGo.AddComponent<Sentinel>();
            RobotEnemy target = null;
            SentinelBolt bolt = null;
            SeekerPulse lppeBolt = null;
            try
            {
                sentinel.Init(ScenarioOrigin, 60f, range: 7f, fireInterval: 0.6f,
                    moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
                target = NewTarget(ScenarioOrigin + new Vector3(2f, 0f, 0f));

                // autoSyncTransforms is off project-wide — see GateSolidityTests/WaterBlasterGateDamageTests.
                Physics.SyncTransforms();
                InvokeUpdate(sentinel);

                bolt = Object.FindAnyObjectByType<SentinelBolt>();
                Assert.IsNotNull(bolt, "firing the sentinel did not spawn a SentinelBolt");
                Vector3 muzzle = bolt.transform.position; // captured before any Tick moves it

                // --- (1) colour: bolt + eye both read as the world's hazard red ---
                Transform boltMesh = bolt.transform.Find("Bolt");
                Assert.IsNotNull(boltMesh, "test precondition: SentinelBolt must build a child named 'Bolt'");
                Color boltTint = boltMesh.GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor");
                Assert.That(boltTint.r, Is.GreaterThanOrEqualTo(0.9f),
                    $"bolt red channel too low ({boltTint.r:0.00}) to read as hazard red");
                Assert.That(boltTint.g, Is.LessThanOrEqualTo(0.25f),
                    $"bolt green channel too high ({boltTint.g:0.00}) to read as hazard red");
                Assert.That(boltTint.b, Is.LessThanOrEqualTo(0.25f),
                    $"bolt blue channel too high ({boltTint.b:0.00}) to read as hazard red");

                FieldInfo bodyField = typeof(Sentinel).GetField("_body", BindingFlags.NonPublic | BindingFlags.Instance);
                var body = (RobotBodies.Body)bodyField.GetValue(sentinel);
                var eyeMpb = new MaterialPropertyBlock();
                body.Eyes[0].GetPropertyBlock(eyeMpb);
                Color eyeColor = eyeMpb.GetColor("_BaseColor");
                Assert.That(Mathf.Abs(eyeColor.r - boltTint.r), Is.LessThanOrEqualTo(0.02f),
                    $"eye red ({eyeColor.r:0.000}) doesn't match the bolt's ({boltTint.r:0.000})");
                Assert.That(Mathf.Abs(eyeColor.g - boltTint.g), Is.LessThanOrEqualTo(0.02f),
                    $"eye green ({eyeColor.g:0.000}) doesn't match the bolt's ({boltTint.g:0.000})");
                Assert.That(Mathf.Abs(eyeColor.b - boltTint.b), Is.LessThanOrEqualTo(0.02f),
                    $"eye blue ({eyeColor.b:0.000}) doesn't match the bolt's ({boltTint.b:0.000})");

                // --- (2) size: 0.6x-0.8x Max's own LPPE bolt, built fresh in this same test ---
                Bounds boltBounds = boltMesh.GetComponent<MeshRenderer>().bounds;
                float boltLongAxis = Mathf.Max(boltBounds.size.x, boltBounds.size.y, boltBounds.size.z);

                lppeBolt = SeekerPulse.Fire(Vector3.zero, Vector3.forward, speed: 18f,
                    turnRateDegPerSec: 360f, lifetime: 0.01f, damage: 9f, lockRange: 14f, lockHalfAngleDeg: 35f);
                Transform lppeMesh = lppeBolt.transform.Find("Bolt");
                Bounds lppeBounds = lppeMesh.GetComponent<MeshRenderer>().bounds;
                float lppeLongAxis = Mathf.Max(lppeBounds.size.x, lppeBounds.size.y, lppeBounds.size.z);

                Assert.That(boltLongAxis, Is.GreaterThanOrEqualTo(lppeLongAxis * 0.6f),
                    $"sentinel bolt's long axis ({boltLongAxis:0.000}m) is smaller than 0.6x the LPPE " +
                    $"bolt's ({lppeLongAxis:0.000}m)");
                Assert.That(boltLongAxis, Is.LessThanOrEqualTo(lppeLongAxis * 0.8f),
                    $"sentinel bolt's long axis ({boltLongAxis:0.000}m) is bigger than 0.8x the LPPE " +
                    $"bolt's ({lppeLongAxis:0.000}m)");

                // --- (3) straight-line flight: sample 10 points across the flight, all on the line ---
                Vector3 impact = new Vector3(target.transform.position.x, muzzle.y, target.transform.position.z);
                for (int i = 1; i <= 10; i++)
                {
                    bolt.Tick(0.01f);
                    Vector3 pos = bolt.transform.position;
                    Vector3 closest = ClosestPointOnSegment(muzzle, impact, pos);
                    float strayDistance = Vector3.Distance(pos, closest);
                    Assert.That(strayDistance, Is.LessThanOrEqualTo(0.02f),
                        $"sample {i}: bolt strayed {strayDistance:0.0000}m off the straight muzzle->impact line");
                }

                // --- (4) never water ---
                Assert.IsNull(sentinel.GetComponentInChildren<WaterVfx>(true),
                    "a WaterVfx component still exists under the sentinel after firing");
            }
            finally
            {
                if (bolt != null) Object.DestroyImmediate(bolt.gameObject);
                if (lppeBolt != null) lppeBolt.Tick(1f); // forces Retire() -> destroys bolt+trail+glow
                Object.DestroyImmediate(sentinelGo);
                if (target != null) Object.DestroyImmediate(target.gameObject);
            }
        }
    }
}
