using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-996 — Lee's TestFlight v0.11.0 report (World 1, FACTORIES 12/17, 2026-09-28): his hose spray
    /// passed straight through two Bolters standing near a west-fence planting strip; a crash + RESUME
    /// made the same robots hittable again. v0.11.0 already contains MV-983 (Dormant keeps its
    /// CharacterController enabled) and MV-984 (automatic physics simulation restored), so this drives a
    /// single real Bolter (<see cref="EnemyArchetype.Bolter"/> — the same archetype table every
    /// world1_config.json garrison "bolter" entry resolves to) through every OTHER state transition the
    /// ticket names as a suspect (H1): dormant-wake-by-damage, park/unpark (MV-966), zone-gate hide/show
    /// (MV-972/981), the Blinker-style teleport reposition (MV-293), fall recovery (MV-952) and the
    /// body-separation clamp (MV-434) — firing the REAL <see cref="WaterBlaster"/> hit path
    /// (<c>FireTick</c>) after each one and asserting damage still lands. The teleport/fall-recovery/
    /// separation-clamp mechanisms are Kind-agnostic (their own method bodies carry no Kind check — only
    /// their PUBLIC trigger conditions are Blinker/gravity/proximity gated), so driving them directly via
    /// reflection on a Bolter instance exercises the exact shared disable-CC/reposition/enable-CC code
    /// H1 suspects, without borrowing a different kind's identity.
    ///
    /// None of the six transitions fails here — every one already disables the CharacterController
    /// before moving the transform and re-enables it after (the MV-293/MV-952/MV-434 code each leaves a
    /// "same idiom" comment on). Per the ticket's own AC1 fallback ("If none fail, say so and make item
    /// 1's MVHIT logging the deliverable"), this test stands as that negative result, and
    /// <c>WaterBlaster</c>'s new MVHIT session-CSV logging (<c>MeasureMvHitCandidates</c>/<c>LogMvHit</c>)
    /// is the ticket's actual deliverable — the next field report carries a named rejecting filter
    /// instead of only "it happened".
    /// </summary>
    public sealed class MV996SprayHitPathThroughRobotTransitionsTests
    {
        // A distinctive, far-off origin (same reasoning as MV917CoverKindTests/MV863PipeCoverTests'
        // own RigOrigin) so nothing this test builds or moves can collide with another fixture's
        // leftover geometry in the shared EditMode physics scene.
        private static readonly Vector3 RigOrigin = new Vector3(-92460f, 0f, 61130f);

        private static readonly MethodInfo CreateInstanceMethod = typeof(EnemySpawner).GetMethod(
            "CreateInstance", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo FireTickMethod =
            typeof(WaterBlaster).GetMethod("FireTick", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo BlasterAwakeMethod =
            typeof(WaterBlaster).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo TickTeleportMethod =
            typeof(RobotEnemy).GetMethod("TickTeleport", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo RecoverFromFallMethod =
            typeof(RobotEnemy).GetMethod("RecoverFromFall", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo ClampBodySeparationMethod =
            typeof(RobotEnemy).GetMethod("ClampBodySeparation", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo StateTimerField =
            typeof(RobotEnemy).GetField("_stateTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo TeleportTargetField =
            typeof(RobotEnemy).GetField("_teleportTarget", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PlayerTargetField =
            typeof(RobotEnemy).GetField("_playerTarget", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp() => RobotEnemy.ResetRegistry();

        [TearDown]
        public void TearDown() => RobotEnemy.ResetRegistry();

        [Test]
        public void BolterStaysHittableAfterEveryNamedStateTransition()
        {
            Assert.IsNotNull(TickTeleportMethod, "RobotEnemy.TickTeleport went missing");
            Assert.IsNotNull(RecoverFromFallMethod, "RobotEnemy.RecoverFromFall went missing");
            Assert.IsNotNull(ClampBodySeparationMethod, "RobotEnemy.ClampBodySeparation went missing");
            Assert.IsNotNull(StateTimerField, "RobotEnemy._stateTimer went missing");
            Assert.IsNotNull(TeleportTargetField, "RobotEnemy._teleportTarget went missing");
            Assert.IsNotNull(PlayerTargetField, "RobotEnemy._playerTarget went missing");

            RobotEnemy bolter = SpawnViaPool(EnemyArchetype.Bolter, RigOrigin + new Vector3(0f, 1f, 0f));
            var playerStandIn = new GameObject("MV996 player stand-in");
            try
            {
                PlayerTargetField.SetValue(bolter, playerStandIn.transform);

                // 1. Dormant -> wake by damage (MV-980/983): BeginDormant leaves the CharacterController
                // enabled; the spray's own TakeDamage call is what actually wakes it (MV-980's own rule:
                // "a hit wakes a sleeping garrison robot immediately").
                bolter.BeginDormant();
                Assert.IsTrue(bolter.IsDormant, "setup failure: BeginDormant did not leave the robot Dormant");
                AssertSprayStillLands(bolter, "dormant-wake-by-damage");
                Assert.IsFalse(bolter.IsDormant, "setup failure: taking spray damage while Dormant must wake it (MV-980)");

                // 2. Park -> unpark (MV-966): SetParked(true) takes the GameObject off the field;
                // SetParked(false) must bring it back exactly as it was, not through a fresh ResetState.
                bolter.SetParked(true);
                Assert.IsFalse(bolter.gameObject.activeSelf, "setup failure: SetParked(true) did not deactivate");
                bolter.SetParked(false);
                Assert.IsTrue(bolter.gameObject.activeSelf, "setup failure: SetParked(false) did not reactivate");
                AssertSprayStillLands(bolter, "park-unpark");

                // 3. Zone-gate hide -> show (MV-972/981): only ever touches renderers, never the collider.
                var gated = (IZoneGatedActor)bolter;
                gated.SetZoneGateVisible(false);
                gated.SetZoneGateVisible(true);
                AssertSprayStillLands(bolter, "zone-gate-hide-show");

                // 4. Teleport reposition (MV-293): the Blinker flank-blink's own disable/set/enable CC
                // idiom, driven directly — TickTeleport itself carries no Kind check, only its
                // Blinker-only public triggers do — so a Bolter exercises the identical shared mechanism.
                Vector3 teleportDestination = bolter.transform.position + new Vector3(6f, 0f, 6f);
                TeleportTargetField.SetValue(bolter, teleportDestination);
                StateTimerField.SetValue(bolter, 999f); // clear TickTeleport's own telegraphTime gate
                TickTeleportMethod.Invoke(bolter, new object[] { 0.016f });
                Assert.AreEqual(teleportDestination.x, bolter.transform.position.x, 0.01f,
                    "setup failure: TickTeleport did not reposition the robot");
                AssertSprayStillLands(bolter, "teleport-reposition");

                // 5. Fall recovery (MV-952): same disable/set/enable idiom, for a robot that fell out of
                // the world and is being warped back to solid ground.
                Vector3 recoveryPos = bolter.transform.position + new Vector3(-4f, 0f, 3f);
                RecoverFromFallMethod.Invoke(bolter, new object[] { recoveryPos });
                Assert.AreEqual(recoveryPos.x, bolter.transform.position.x, 0.01f,
                    "setup failure: RecoverFromFall did not reposition the robot");
                AssertSprayStillLands(bolter, "fall-recovery");

                // 6. Separation clamp (MV-434): same idiom again, correcting a robot pushed too close to
                // Max. The player stand-in is put almost on top of the robot's CURRENT (post-recovery)
                // position so the clamp actually fires rather than early-returning as a no-op.
                playerStandIn.transform.position = bolter.transform.position + new Vector3(0.05f, 0f, 0f);
                ClampBodySeparationMethod.Invoke(bolter, null);
                AssertSprayStillLands(bolter, "separation-clamp");
            }
            finally
            {
                Object.DestroyImmediate(bolter.gameObject);
                Object.DestroyImmediate(playerStandIn);
            }
        }

        /// <summary>Full-health top-up, then the REAL <see cref="WaterBlaster"/> hit path
        /// (<c>FireTick</c>, private — same reflection idiom <c>MV984AwakeRobotsTakeDamageTests</c> uses)
        /// for 0.5s of stream at the authored 0.1s tick interval, 3 m dead ahead on open ground. Fails
        /// naming which transition it ran after, so a red run points straight at the transition that
        /// broke the collider instead of needing to be re-derived.</summary>
        private static void AssertSprayStillLands(RobotEnemy robot, string afterTransition)
        {
            robot.SetHealthFraction(1f);
            float healthBefore = robot.HealthCurrent;

            Vector3 targetPos = robot.transform.position;
            Vector3 blasterPos = targetPos + new Vector3(0f, 0f, -3f);
            var blasterGo = new GameObject($"MV996 Water Blaster (after {afterTransition})");
            try
            {
                blasterGo.transform.position = blasterPos;
                blasterGo.transform.rotation =
                    Quaternion.LookRotation((targetPos - blasterPos).normalized, Vector3.up);
                var blaster = blasterGo.AddComponent<WaterBlaster>();
                BlasterAwakeMethod.Invoke(blaster, null); // AddComponent doesn't reliably run Awake outside Play mode
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                for (int i = 0; i < 5; i++) FireTickMethod.Invoke(blaster, null);
            }
            finally
            {
                Object.DestroyImmediate(blasterGo);
            }

            Assert.Less(robot.HealthCurrent, healthBefore,
                $"MV-996: the water blaster passed straight through the Bolter after '{afterTransition}'");
        }

        /// <summary>Drives the REAL, private <c>EnemySpawner.CreateInstance(in EnemyArchetype)</c> via
        /// reflection — the normal pool/factory path every in-game robot is built through (same
        /// technique <c>MV984AwakeRobotsTakeDamageTests</c>/<c>MV535RobotBodyOrderingTests</c> use).
        /// <c>CreateInstance</c> leaves the instance inactive (pooling contract), so this activates it at
        /// the given world position, running the real <c>OnEnable</c>/<c>ResetState</c>.</summary>
        private static RobotEnemy SpawnViaPool(EnemyArchetype archetype, Vector3 startPos)
        {
            var spawnerGo = new GameObject("MV996 test spawner");
            try
            {
                var spawner = spawnerGo.AddComponent<EnemySpawner>();
                Assert.IsNotNull(CreateInstanceMethod, "EnemySpawner.CreateInstance went missing");

                LogAssert.ignoreFailingMessages = true;
                RobotEnemy e;
                try { e = (RobotEnemy)CreateInstanceMethod.Invoke(spawner, new object[] { archetype }); }
                finally { LogAssert.ignoreFailingMessages = false; }

                // Detach before the spawner is torn down below, or DestroyImmediate(spawnerGo) cascades
                // and destroys the very GameObject this method hands back (MV746ReefBodyTests' own note).
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
