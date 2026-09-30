using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1043: Lee's TestFlight telemetry (World 1 a23, v0.11.2) showed a mobile shed's own transform
    /// going non-finite — it vanished, its EnemySpawner then refused five NaN-position spawns (MV-1021's
    /// guard), and 17 Bolters/Blinkers in the same area became invincible to the primary spray
    /// (Physics.OverlapSphere silently stopped returning their colliders — PhysX's broadphase does not
    /// tolerate a NaN actor, was this ticket's own leading theory).
    ///
    /// Every one of the ticket's own candidate mechanisms for how the shed's transform actually goes
    /// non-finite was investigated and NONE reproduced in EditMode (full write-up in the fix comment, not
    /// re-derived here): <see cref="MaxWorlds.Factories.MowerHutch"/>'s own Pursuit arithmetic is
    /// "accidentally safe" against a non-finite targetPosition (<c>Vector3.normalized</c> returns
    /// <c>Vector3.zero</c> for a non-finite input, and <c>Mathf.Min(finite, NaN)</c> returns the NaN
    /// operand, so TickPursuit's own <c>if (step &gt; 0f)</c> is false and MoveBody is never reached); a
    /// non-finite displacement fed straight into <c>CharacterController.Move</c> — this ticket's own
    /// leading hypothesis — does not corrupt the transform (confirmed both in isolation and via a 30 s
    /// coincident-capsule spawn at both 1/60 and 1/20 dt: PhysX silently no-ops it); and a direct
    /// <c>transform.position = nonFiniteVector</c> write is refused by Unity's own Transform setter,
    /// which logs "transform.position assign attempt ... is not valid" and leaves the previous value
    /// untouched. In Unity 6000.4.9f1, none of this project's own code paths can actually put a live
    /// GameObject's transform into a non-finite state through the public API surface this game uses.
    ///
    /// What IS real and testable is the ticket's own AC2 evidence requirement: a non-finite
    /// <c>targetPosition</c> must be refused AND leave one <c>nan-move</c> row — base <c>TickMobility</c>
    /// has no such call site at all, so the expected log below is never emitted. Fails on main (commit
    /// before this ticket) on the SECOND assertion below (<c>LogAssert.Expect</c> going unsatisfied) —
    /// quote the failure in the fix comment.
    /// </summary>
    public sealed class MV1043NanFirewallTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo MobilityStateField =
            typeof(MowerHutch).GetField("_mobilityState", NonPublicInstance);
        private static readonly MethodInfo CreateInstanceMethod =
            typeof(EnemySpawner).GetMethod("CreateInstance", NonPublicInstance);
        private static readonly MethodInfo FireTickMethod =
            typeof(WaterBlaster).GetMethod("FireTick", NonPublicInstance);
        private static readonly MethodInfo BlasterAwakeMethod =
            typeof(WaterBlaster).GetMethod("Awake", NonPublicInstance);

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            NanMoveLog.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            NanMoveLog.Reset();
        }

        // Guards MV-1043
        [Test]
        public void NonFiniteTarget_LogsNanMoveAndLeavesShedFinite_AndANearbyRobotStillTakesSprayDamage()
        {
            // MowerHutch.Build's collider strip logs edit-mode DestroyImmediate noise (MV1022MobileShedStandoffTests
            // / EnemySpawnerTests precedent).
            LogAssert.ignoreFailingMessages = true;

            GameObject shedGo = null;
            RobotEnemy robot = null;
            GameObject blasterGo = null;
            try
            {
                shedGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shedGo.name = "MV-1043 Shed";
                shedGo.transform.position = new Vector3(0f, 1f, 0f);
                shedGo.AddComponent<CharacterController>();
                MowerHutch hutch = shedGo.AddComponent<MowerHutch>();
                hutch.Build();
                hutch.ConfigureMobility(true);
                MobilityStateField.SetValue(hutch, MowerHutch.ShedMobility.Pursuit);

                Vector3 before = shedGo.transform.position;
                Assert.IsTrue(CharacterControllerSafety.IsFinite(before), "test precondition: shed starts finite");

                // AC2's evidence requirement: a non-finite targetPosition must leave one nan-move row.
                // Fails on main: base TickMobility has no such call site, so this expected message is
                // never emitted and LogAssert's own end-of-test check reports it unsatisfied.
                LogAssert.ignoreFailingMessages = false;
                LogAssert.Expect(LogType.Warning, new Regex(@"\[NanMoveLog\].*nonFiniteTarget"));
                hutch.TickMobility(1f / 60f, new Vector3(float.NaN, 0f, float.NaN), null);

                Vector3 afterRestore = shedGo.transform.position;
                Assert.IsTrue(CharacterControllerSafety.IsFinite(afterRestore),
                    $"MV-1043: the shed went non-finite off a single NaN-target tick: {afterRestore}");
                Assert.AreEqual(before, afterRestore,
                    "MV-1043: a refused NaN-target tick must leave the shed exactly where it was");

                LogAssert.ignoreFailingMessages = true;

                // Step 4's broadphase question: with the shed's own transform never allowed to go
                // non-finite, a nearby robot must still be a valid OverlapSphere hit for the spray.
                robot = SpawnRobotViaPool(EnemyArchetype.Bolter, afterRestore + new Vector3(3f, 0f, 0f));
                float healthBefore = robot.HealthCurrent;
                Assert.Greater(healthBefore, 0f, "setup failure: robot must start alive");

                Vector3 targetPos = robot.transform.position;
                Vector3 blasterPos = targetPos + new Vector3(0f, 0f, -2f);
                blasterGo = new GameObject("MV-1043 Water Blaster (Max)");
                blasterGo.transform.position = blasterPos;
                blasterGo.transform.rotation =
                    Quaternion.LookRotation((targetPos - blasterPos).normalized, Vector3.up);
                var blaster = blasterGo.AddComponent<WaterBlaster>();
                BlasterAwakeMethod.Invoke(blaster, null); // AddComponent doesn't reliably run Awake outside Play mode
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                for (int i = 0; i < 5; i++) FireTickMethod.Invoke(blaster, null);

                Assert.Less(robot.HealthCurrent, healthBefore,
                    "MV-1043: the water blaster failed to damage a robot 3m from Max near the shed");
            }
            finally
            {
                if (blasterGo != null) Object.DestroyImmediate(blasterGo);
                if (robot != null) Object.DestroyImmediate(robot.gameObject);
                if (shedGo != null) Object.DestroyImmediate(shedGo);
                LogAssert.ignoreFailingMessages = false;
            }
        }

        private static RobotEnemy SpawnRobotViaPool(EnemyArchetype archetype, Vector3 startPos)
        {
            var spawnerGo = new GameObject("MV1043 test spawner");
            try
            {
                var spawner = spawnerGo.AddComponent<EnemySpawner>();
                LogAssert.ignoreFailingMessages = true;
                RobotEnemy e;
                try { e = (RobotEnemy)CreateInstanceMethod.Invoke(spawner, new object[] { archetype }); }
                finally { LogAssert.ignoreFailingMessages = false; }

                // Detach before the spawner is torn down, or DestroyImmediate(spawnerGo) cascades and
                // destroys the very GameObject this method hands back (MV984/MV746 precedent).
                e.transform.SetParent(null, worldPositionStays: false);
                e.transform.position = startPos;
                e.gameObject.SetActive(true);
                Physics.SyncTransforms();
                return e;
            }
            finally
            {
                Object.DestroyImmediate(spawnerGo);
            }
        }
    }
}
