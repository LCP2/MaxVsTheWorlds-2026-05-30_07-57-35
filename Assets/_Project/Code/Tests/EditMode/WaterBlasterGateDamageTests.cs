using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Combat;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-386's second regression: PR #329 split a closed gate's single collider into a world-fixed
    /// <c>ThresholdObject</c> (on the Cover layer, drops on <see cref="AreaGate.Open"/>) and the gate's
    /// own leaf collider (never on Cover, so an opened door's swing can't re-block a sight-line it just
    /// gave up — see <c>MapRuntime.BuildAreaGate</c>). But <see cref="WaterBlaster.FireTick"/>'s
    /// line-of-sight check asked permission for the LEAF's transform while its Cover-masked ray actually
    /// lands on the THRESHOLD sitting across the same closed doorway — a different Transform, so
    /// <see cref="MaxWorlds.Arena.LineOfSight.Clear"/> read every shot as blocked. The gate stayed
    /// correctly solid (it really did stop the shot) while never receiving the damage that shot carried:
    /// Lee's 15 Aug live playtest of PR #329/commit 20ff430 found gate HP never moved at all.
    /// </summary>
    public sealed class WaterBlasterGateDamageTests
    {
        // Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode (confirmed
        // empirically for AreaGate, see AreaGateTests's MV-386 note) — drive Awake directly instead,
        // the same workaround RobotSkinDiagnosticsTests/RuntimeSurfaceDirectorTests already rely on.
        private static void InvokeAwake(Object component)
        {
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
        }

        private static void InvokeFireTick(WaterBlaster blaster)
        {
            typeof(WaterBlaster).GetMethod("FireTick", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(blaster, null);
        }

        [Test]
        public void AClosedAliveGate_TakesDamageFromPrimaryFire_DespiteItsCoverLayerThreshold()
        {
            if (!CoverLayer.Exists) Assert.Ignore("no Cover layer in this project");

            var gateBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var blasterBody = new GameObject("wb_gate_damage_test");
            GameObject thresholdBody = null;
            try
            {
                // A gate exactly as MapRuntime.BuildAreaGate assembles one: build the AreaGate (which
                // builds its own leaf collider + a sibling ThresholdObject in Awake), then put ONLY the
                // threshold on the Cover layer -- the same split that shipped the regression.
                gateBody.transform.position = new Vector3(0f, 0f, 3f);
                gateBody.transform.localScale = new Vector3(4f, 3f, 0.6f);
                var gate = gateBody.AddComponent<AreaGate>();
                InvokeAwake(gate);
                Assert.IsNotNull(gate.ThresholdObject, "gate built no ThresholdObject -- Awake didn't run");
                thresholdBody = gate.ThresholdObject;
                CoverLayer.Assign(thresholdBody);

                float maxHp = gate.MaxHp;
                Assert.Greater(maxHp, 0f, "precondition: the gate has HP to lose");

                // Max standing well clear of the doorway, aimed dead-on at the gate -- angle 0 always
                // clears the spray cone regardless of its authored half-angle.
                blasterBody.transform.position = new Vector3(0f, 0f, 0f);
                blasterBody.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                var blaster = blasterBody.AddComponent<WaterBlaster>();

                // autoSyncTransforms is off project-wide (DynamicsManager.asset) -- every transform set
                // above (gate, threshold, blaster) needs an explicit sync before the physics queries
                // FireTick runs, same as GateSolidityTests/EveryAreaGateInTheLiveWorldConfig... do.
                Physics.SyncTransforms();
                InvokeFireTick(blaster);

                Assert.Less(gate.HealthCurrent, maxHp,
                    "a closed, alive gate took no damage from primary fire -- its own Cover-layer " +
                    "threshold is blocking the line-of-sight check that gates whether TakeDamage fires " +
                    "at all, exactly the MV-386 regression Lee's 15 Aug playtest found (PR #329, gate " +
                    "solid but unbreakable).");
                Assert.IsTrue(gate.IsAlive, "precondition drifted: one tick alone broke the gate");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = true;
                try
                {
                    Object.DestroyImmediate(blasterBody);
                    // thresholdBody is a SIBLING of the gate, not a child (BuildThresholdCollider
                    // parents it under transform.parent) -- destroying gateBody alone would not take
                    // it with it even if AreaGate.OnDestroy didn't already clean it up.
                    if (thresholdBody != null) Object.DestroyImmediate(thresholdBody);
                    Object.DestroyImmediate(gateBody);
                }
                finally { LogAssert.ignoreFailingMessages = false; }
            }
        }
    }
}
