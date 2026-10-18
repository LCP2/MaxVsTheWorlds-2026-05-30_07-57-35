using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1228 AC1: a Breaker 6-12 m from Max with a clear line telegraphs, then rams him in a
    /// straight line — reusing the Cart Charger's (MV-707) own telegraph/charge mechanism — landing 34
    /// damage and a 4 m forced-move shove (<see cref="PlayerController.ApplyForcedMove"/>, MV-1222,
    /// whose first production caller this is); ramming a wall/cover instead stuns it for 1.5 s at
    /// +50% damage taken, same idiom as the Charger's own wall-stun but the Breaker's own, longer
    /// duration. All assertions read RESOLVED values (MV-465 Tier 2): Max's real
    /// <see cref="PlayerHealth.Current"/> before/after, his real post-knockback position (driven
    /// through <see cref="PlayerController"/>'s own private per-tick forced-move helper, same
    /// reflection idiom <c>MV1222ForcedMoveTests</c> already established), and
    /// <see cref="RobotEnemy.IsStunned"/>/<see cref="RobotEnemy.StunTimeRemaining"/>/
    /// <see cref="RobotEnemy.DamageTakenMultiplier"/> — never an authored constant read directly.
    ///
    /// Fails on 2d4caa0 (this ticket's own named base): neither <see cref="EnemyKind.Breaker"/> nor
    /// <see cref="EnemyArchetype.Breaker"/> exist there, so this test does not compile on that commit.
    ///
    /// EditMode only, reflection-driven (repo convention): <c>Update()</c> never runs outside Play
    /// mode, so the private Tick* methods, <c>HandleWallContact</c> and <c>PlayerController</c>'s own
    /// private <c>Awake</c>/<c>TickForcedMove</c> are invoked directly — the same idiom
    /// <c>MV707CartChargerTests</c>/<c>MV1222ForcedMoveTests</c>/<c>MV720BossContactDamageTests</c>
    /// already established.
    /// </summary>
    public sealed class MV1228BreakerRamTests
    {
        private const float Dt = 0.02f;

        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly FieldInfo StateTimerField =
            typeof(RobotEnemy).GetField("_stateTimer", NonPublicInstance);
        private static readonly FieldInfo BreakerIsRamField =
            typeof(RobotEnemy).GetField("_breakerIsRam", NonPublicInstance);
        private static readonly MethodInfo TickChaseMethod =
            typeof(RobotEnemy).GetMethod("TickChase", NonPublicInstance);
        private static readonly MethodInfo TickTelegraphMethod =
            typeof(RobotEnemy).GetMethod("TickTelegraph", NonPublicInstance);
        private static readonly MethodInfo TickLungeMethod =
            typeof(RobotEnemy).GetMethod("TickLunge", NonPublicInstance);
        private static readonly MethodInfo HandleWallContactMethod =
            typeof(RobotEnemy).GetMethod("HandleWallContact", NonPublicInstance);
        private static readonly MethodInfo PlayerAwakeMethod =
            typeof(PlayerController).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo TickForcedMoveMethod =
            typeof(PlayerController).GetMethod("TickForcedMove", NonPublicInstance);

        private static RobotEnemy NewBreaker(Vector3 position)
        {
            var go = new GameObject("Breaker");
            go.transform.position = position;
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            // EditMode never runs Awake/OnEnable, so _cc (normally seeded there) has to be stamped by
            // hand before any Tick* movement can call CharacterControllerMotion.SafeMove on it — same
            // idiom as MV707CartChargerTests.NewEnemy.
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Breaker); // stamps stats and re-runs ResetState, which finds the tagged Player
            return e;
        }

        private static GameObject NewMax(Vector3 position, out PlayerController player, out PlayerHealth health)
        {
            var go = new GameObject("Max", typeof(CharacterController), typeof(PlayerController)) { tag = "Player" };
            go.transform.position = position;
            player = go.GetComponent<PlayerController>();
            PlayerAwakeMethod.Invoke(player, null); // Awake is not a reliable AddComponent side effect outside Play mode
            health = go.AddComponent<PlayerHealth>();
            health.Initialize();
            return go;
        }

        /// <summary>Re-plants the robot facing dead at +X from the origin, same idiom
        /// <c>MV707CartChargerTests</c> uses — <c>TickChase</c>'s own approach-fan nudge is a separate,
        /// already-exercised concern, not what this AC is about.</summary>
        private static void FaceDeadAheadAlongX(RobotEnemy e)
        {
            var cc = (CharacterController)CcField.GetValue(e);
            cc.enabled = false;
            e.transform.position = Vector3.zero;
            e.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            cc.enabled = true;
            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide
        }

        private static void CommitTelegraphToLunge(RobotEnemy e)
        {
            StateTimerField.SetValue(e, 999f);
            TickTelegraphMethod.Invoke(e, new object[] { Dt });
        }

        private static void RunForcedMoveToCompletion(PlayerController player)
        {
            for (int i = 0; i < 100 && player.IsForcedMoveActive; i++)
                TickForcedMoveMethod.Invoke(player, new object[] { Dt });
        }

        /// <summary>Both halves of AC1 in one test (MV-465 Rule 1: at most one new test per ticket) —
        /// the ticket's own AC wording is already one scenario "repeated with a wall between", not two
        /// independent regressions.</summary>
        [Test]
        public void Ram_LandsDamageAndKnockbackInTheOpen_AndStopsStunnedAtAWall()
        {
            RamLandsDamageAndKnockbackInTheOpen();
            RamStopsAtAWallStunnedForItsOwnLongerDuration();
        }

        private void RamLandsDamageAndKnockbackInTheOpen()
        {
            GameObject maxGo = null, breakerGo = null;
            try
            {
                maxGo = NewMax(new Vector3(8f, 0f, 0f), out PlayerController player, out PlayerHealth health);
                RobotEnemy breaker = NewBreaker(Vector3.zero);
                breakerGo = breaker.gameObject;
                breaker.Sight.Tick(true, maxGo.transform.position, Dt);

                TickChaseMethod.Invoke(breaker, new object[] { Dt });
                Assert.AreEqual(RobotEnemy.State.Telegraph, breaker.Current,
                    "8 m, clear sight, inside the 6-12 m ram band — the Breaker must commit to the telegraph");
                Assert.IsTrue((bool)BreakerIsRamField.GetValue(breaker),
                    "at 8 m (outside the 2.2 m punch range) the committed attack must be the ram, not the punch");

                FaceDeadAheadAlongX(breaker);
                CommitTelegraphToLunge(breaker);
                Assert.AreEqual(RobotEnemy.State.Lunge, breaker.Current, "telegraph must commit to the ram");

                float healthBeforeContact = health.Current;

                // Drive the charge (TickLunge dispatches to TickCharge for _breakerIsRam) until it
                // either lands on Max (Current -> Recover) or caps its travel — bounded, same idiom
                // MV1222ForcedMoveTests.RunForcedMoveToCompletion uses for an analogous per-tick helper.
                for (int i = 0; i < 100 && breaker.Current == RobotEnemy.State.Lunge; i++)
                {
                    TickLungeMethod.Invoke(breaker, new object[] { Dt });
                    Physics.SyncTransforms();
                }

                Assert.AreEqual(RobotEnemy.State.Recover, breaker.Current,
                    "ramming Max in the open must land the hit and end the charge right there");
                Assert.AreEqual(healthBeforeContact - 34f, health.Current, 0.01f,
                    $"the ram must deal 34 damage, got {healthBeforeContact - health.Current:0.00}");

                Vector3 contactPoint = maxGo.transform.position;
                RunForcedMoveToCompletion(player);
                float knockedDistance = Vector3.Distance(maxGo.transform.position, contactPoint);
                Assert.GreaterOrEqual(knockedDistance, 3.5f,
                    $"the ram's knockback must move Max at least 3.5 m from the contact point, got {knockedDistance:0.00} m");
            }
            finally
            {
                if (breakerGo != null) Object.DestroyImmediate(breakerGo);
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                RobotEnemy.ResetRegistry();
            }
        }

        private void RamStopsAtAWallStunnedForItsOwnLongerDuration()
        {
            // A bare tagged "Player" GameObject — TickChase's own AcquireTarget needs one to find
            // (same minimal fixture MV707CartChargerTests uses); this scenario asserts nothing about
            // Max himself, only about the Breaker's own wall-ram reaction.
            GameObject breakerGo = null, coverGo = null, maxGo = null;
            try
            {
                maxGo = new GameObject("Player") { tag = "Player" };
                maxGo.transform.position = new Vector3(8f, 0f, 0f);

                RobotEnemy breaker = NewBreaker(Vector3.zero);
                breakerGo = breaker.gameObject;
                breaker.Sight.Tick(true, maxGo.transform.position, Dt);

                TickChaseMethod.Invoke(breaker, new object[] { Dt });
                Assert.AreEqual(RobotEnemy.State.Telegraph, breaker.Current, "must commit to the telegraph");

                FaceDeadAheadAlongX(breaker);
                CommitTelegraphToLunge(breaker);
                Assert.AreEqual(RobotEnemy.State.Lunge, breaker.Current, "telegraph must commit to the ram");

                // ControllerColliderHit has no public constructor, so the wall/cover hit is driven
                // directly through HandleWallContact — the same seam
                // MV707CartChargerTests/MV586ForceFieldRamTests already established for exactly this
                // reason.
                coverGo = new GameObject("Cover") { layer = 0 };
                var coverCollider = coverGo.AddComponent<BoxCollider>();
                coverGo.transform.position = new Vector3(3f, 0f, 0f);

                HandleWallContactMethod.Invoke(breaker, new object[] { coverCollider, Vector3.left });

                Assert.AreEqual(RobotEnemy.State.Recover, breaker.Current,
                    "ramming a wall/cover must end the charge into Recover, not leave it mid-Lunge");
                Assert.IsTrue(breaker.IsStunned, "ramming a wall/cover must stun the Breaker");
                Assert.AreEqual(1.5f, breaker.StunTimeRemaining, 0.05f,
                    $"the Breaker's own wall-ram stun should read ~1.5s remaining, got {breaker.StunTimeRemaining:0.000}s");
                Assert.AreEqual(1.5f, breaker.DamageTakenMultiplier, 0.01f,
                    $"a Breaker stunned by a wall-ram should take 1.5x damage, got {breaker.DamageTakenMultiplier:0.000}x");
            }
            finally
            {
                if (coverGo != null) Object.DestroyImmediate(coverGo);
                if (breakerGo != null) Object.DestroyImmediate(breakerGo);
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                RobotEnemy.ResetRegistry();
            }
        }
    }
}
