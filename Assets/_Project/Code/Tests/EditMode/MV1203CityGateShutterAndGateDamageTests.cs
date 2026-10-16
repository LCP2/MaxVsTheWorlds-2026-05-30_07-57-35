using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1203 (the one new test, Rule 1): Lee's 17 Oct TestFlight report at City a6 -- "I can't open
    /// the door to Area 7" and "they're not solid, you can walk straight through them" -- plus his
    /// correction that the shutter should come down from ~9 m to ~6 m. Builds a City-skinned 3 m-wide
    /// gate exactly as <c>MapRuntime.BuildAreaGate</c>/<see cref="AreaGate.ApplyCitySkin"/> assemble one,
    /// once on an N wall (no spin) and once on an E wall (90 deg yaw, <c>BuildAreaGate</c>'s own E/W
    /// case), and for EACH proves three RESOLVED values (MV-465 Tier 2):
    ///
    /// 1. the shutter's top sits 6.0 m +-0.3 above the FLOOR -- not the doorway's own (possibly
    ///    different) height, and not the old double-scale bug's ~width^2 x height^2 (~9 x 9 m for this
    ///    3 m door), built by rendering the shutter in the gate's already-scaled local space;
    /// 2. a RICOCHET disc fired square at the closed gate reduces its health -- the gate's threshold
    ///    collider (Cover layer, MV-386) carries no IDamageable of its own, so a disc whose contact
    ///    lands on it bounces without ever damaging the gate;
    /// 3. a 0.5 m capsule cannot sweep through the closed doorway -- the one assertion that should
    ///    already hold (the real collider was never the broken part) and must keep holding after the fix.
    ///
    /// Fails on base commit 24a90c7 (main HEAD before this ticket): assertion 1 measures the shutter's
    /// top at roughly 9 m above the floor on both walls, not 6 m, and assertion 2 finds the gate takes
    /// no damage from the disc on either wall.
    /// </summary>
    public sealed class MV1203CityGateShutterAndGateDamageTests
    {
        [TearDown]
        public void TearDown()
        {
            RicochetDisc.DestroyPoolForTests();
        }

        [Test]
        public void CityGateShutterTopIs6mAboveFloorAndRicochetDamagesTheClosedGateOnNAndEWalls()
        {
            if (!CoverLayer.Exists) Assert.Ignore("no Cover layer in this project");

            CheckOneOrientation(spinEWWall: false);
            CheckOneOrientation(spinEWWall: true);
        }

        private static void CheckOneOrientation(bool spinEWWall)
        {
            const float width = 3f;
            const float height = 3f;
            const float depth = 0.6f;
            const float expectedShutterHeight = 6f;

            string label = spinEWWall ? "E-wall" : "N-wall";

            var go = new GameObject("MV1203 City Gate " + label);
            GameObject thresholdBody = null;
            try
            {
                go.transform.position = new Vector3(0f, height * 0.5f, 0f);
                go.transform.localScale = new Vector3(width, height, depth);
                // BuildAreaGate's own E/W case: spun on Y so the doorway's span runs along Z instead of
                // X -- the orientation Lee's a6/a7 report actually hit.
                if (spinEWWall) go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

                var gate = go.AddComponent<AreaGate>();
                InvokeAwake(gate);
                thresholdBody = gate.ThresholdObject;
                Assert.IsNotNull(thresholdBody, label + ": gate built no ThresholdObject -- Awake didn't run");
                CoverLayer.Assign(thresholdBody);

                gate.ApplyCitySkin();

                // --- AC1: the shutter's top sits 6.0 m +-0.3 above the floor, regardless of the
                // doorway's own height and regardless of wall orientation.
                var dressingRoot = (GameObject)GetPrivate(gate, "_cityDressingRoot");
                Assert.IsNotNull(dressingRoot, label + ": ApplyCitySkin built no City dressing root");

                Bounds? shutterBounds = EncapsulateRenderers(dressingRoot.transform);
                Assert.IsTrue(shutterBounds.HasValue,
                    label + ": the City shutter must carry at least one renderer to measure");

                float floorWorldY = go.transform.position.y - height * 0.5f;
                float shutterTopAboveFloor = shutterBounds.Value.max.y - floorWorldY;
                Assert.That(shutterTopAboveFloor, Is.EqualTo(expectedShutterHeight).Within(0.3f),
                    $"{label}: the City shutter's top ({shutterTopAboveFloor:F2} m above the floor) must " +
                    $"be {expectedShutterHeight:F2} m +-0.3 -- ApplyCitySkin must build it at a fixed " +
                    "height, not as a product of the gate's own (double-applied) scale");

                // --- AC2: a RICOCHET disc fired square at the closed gate reduces its health.
                float healthBefore = gate.HealthCurrent;
                Assert.Greater(healthBefore, 0f, label + ": test precondition: the gate must have HP to lose");

                Physics.SyncTransforms();

                LogAssert.ignoreFailingMessages = true;
                RicochetDisc disc;
                try
                {
                    disc = RicochetDisc.Spawn(new Vector3(-3f, height * 0.5f, 0f), Vector3.right,
                        Ricochet.DefaultSpeed, bounces: 1, powerLevel: 1, aimLevel: 0,
                        damage: 20f, bankBonusPerBounce: 0f, turnRateDegPerSec: 0f, lookaheadMetres: 0f,
                        radius: Ricochet.DefaultRadius, lifetime: Ricochet.DefaultLifetimeSeconds);
                }
                finally { LogAssert.ignoreFailingMessages = false; }
                Assert.IsNotNull(disc, label + ": test precondition: the pool must have room for one disc");

                const float step = 1f / 60f;
                for (int i = 0; i < 60 && disc.IsActiveDisc; i++) disc.Tick(step);

                Assert.Less(gate.HealthCurrent, healthBefore,
                    label + ": a RICOCHET disc fired square at the closed gate must reduce its health -- " +
                    "its Cover-layer threshold must resolve back to the gate, not swallow the hit");

                // --- AC3: a 0.5 m capsule cannot pass the closed doorway (it already shouldn't; this
                // guards that fixing the shutter's visuals never touched the real collider).
                Vector3 p1 = new Vector3(-3f, height * 0.5f - 0.5f, 0f);
                Vector3 p2 = new Vector3(-3f, height * 0.5f + 0.5f, 0f);
                bool blocked = Physics.CapsuleCast(p1, p2, 0.5f, Vector3.right, out _, 6f);
                Assert.IsTrue(blocked,
                    label + ": a 0.5 m capsule swept across the closed doorway must hit something solid");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = true;
                try
                {
                    if (thresholdBody != null) Object.DestroyImmediate(thresholdBody);
                    Object.DestroyImmediate(go);
                }
                finally { LogAssert.ignoreFailingMessages = false; }
            }
        }

        private static void InvokeAwake(AreaGate gate)
        {
            typeof(AreaGate).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(gate, null);
        }

        private static object GetPrivate(object target, string fieldName) =>
            target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);

        private static Bounds? EncapsulateRenderers(Transform root)
        {
            Bounds? result = null;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (result.HasValue) { Bounds b = result.Value; b.Encapsulate(r.bounds); result = b; }
                else result = r.bounds;
            }
            return result;
        }
    }
}
