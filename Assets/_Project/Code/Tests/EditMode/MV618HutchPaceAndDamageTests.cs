using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-618 (Lee's playtest read, 26 Aug 2026): a pursuing mobile hutch was 2.4x a Brute's speed,
    /// dealt no contact damage at all, and several converging on Max stacked instead of spreading.
    /// This pins the fix's two EditMode-provable claims: pursuit closes at Brute pace (0.75 m/s, fixed —
    /// never derived from <c>PlayerController.WalkSpeed</c>, so a Speed upgrade on Max can never speed
    /// the sheds up too) and deals Brute contact damage (38/s) only while parked at its standoff ring.
    /// Separation between multiple pursuing hutches (AC3) needs several live hutches moving over several
    /// seconds of real time to observe — PlayMode territory, which this worker never authors
    /// (CC_AUTONOMY.md) — so it is exercised by <see cref="MaxWorlds.Weapons.AbilityTuning.SentinelSeparationStep"/>'s
    /// own coverage instead: <see cref="MowerHutch"/> reuses that exact function, unmodified, for hutch-
    /// to-hutch separation (see the fix comment for why that's sufficient).
    /// </summary>
    public sealed class MV618HutchPaceAndDamageTests
    {
        private const float BodyWidth = 2.25f;
        private const float BodyHeight = 1.5f;
        private const float Dt = 1f / 60f;

        // Same distinctive far-off origin MV548MobileShedTests uses, for the same reason: EditMode
        // tests share one physics scene for the whole cc-verify run with no per-test reset, so a rig
        // built near the world origin can start out overlapping a previous test's leftover collider.
        private static readonly Vector3 RigOrigin = new Vector3(-77451f, 0f, 63008f);

        private static (GameObject go, MowerHutch hutch) BuildMobileShed(Vector3 groundedCenter)
        {
            var go = new GameObject("Mobile Hutch");
            go.transform.position = groundedCenter;
            go.transform.localScale = new Vector3(BodyWidth, BodyHeight, BodyWidth);

            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            var cc = go.AddComponent<CharacterController>();
            cc.center = Vector3.zero;
            cc.height = 1f;
            cc.radius = 0.5f;

            var hutch = go.AddComponent<MowerHutch>();
            typeof(MowerHutch).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(hutch, null);
            hutch.ConfigureMobility(true);

            Physics.SyncTransforms();
            return (go, hutch);
        }

        /// <summary>Minimal stub so contact damage can be observed without a live PlayerController —
        /// same "hand the pure function a fake" idiom the rest of the suite uses for IDamageable.</summary>
        private sealed class DamageRecorder : IDamageable
        {
            public float TotalDamage;
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info) => TotalDamage += info.Amount;
        }

        [Test]
        public void PursuingHutch_MovesAtBrutePace_AndDealsContactDamageOnlyAtStandoff()
        {
            // MowerHutch.BuildCore destroys the primitive core's stock collider via Object.Destroy,
            // which is edit-mode-illegal and logs an [Error] regardless of who calls Awake — same shape
            // MV548MobileShedTests carries.
            LogAssert.ignoreFailingMessages = true;

            Vector3 groundedCenter = RigOrigin + new Vector3(0f, BodyHeight * 0.5f, 0f);
            (GameObject go, MowerHutch hutch) = BuildMobileShed(groundedCenter);
            var recorder = new DamageRecorder();
            try
            {
                // Force straight to Pursuit via first damage, same trigger MV548's wall scenario uses.
                // The trigger and the lift-off climb are two separate ticks (MV548 precedent) — the
                // tick that flips Grounded->LiftOff does not also advance the lift timer.
                hutch.TakeDamage(new DamageInfo(1f, Vector3.zero, Vector3.forward, Team.Player));
                Vector3 farMax = groundedCenter + Vector3.forward * 50f; // stay well outside standoff
                hutch.TickMobility(0f, farMax);
                Assert.AreEqual(MowerHutch.ShedMobility.LiftOff, hutch.MobilityState,
                    "first damage must trigger lift-off");
                for (float elapsed = 0f; elapsed < 2.5f; elapsed += Dt)
                    hutch.TickMobility(Dt, farMax);
                Assert.AreEqual(MowerHutch.ShedMobility.Pursuit, hutch.MobilityState,
                    "2.5 s of lift-off ticks must complete into Pursuit");

                // A pursuing hutch must close at a FIXED Brute pace (0.75 m/s) — resolved by measuring
                // actual displacement over exactly one second, not by reading the authored constant, and
                // never fed a walk speed to derive it from (TickMobility takes no such parameter at all).
                Vector3 before = go.transform.position;
                for (int i = 0; i < 60; i++) hutch.TickMobility(Dt, farMax, recorder); // 1 s
                float distanceMoved = Vector3.Distance(before, go.transform.position);
                Assert.That(distanceMoved, Is.EqualTo(0.75f).Within(0.05f),
                    "a pursuing hutch must close at Brute pace (0.75 m/s) in one second of open ground");
                Assert.AreEqual(0f, recorder.TotalDamage,
                    "a hutch not yet in contact must deal 0 damage");

                // Bank the contact cooldown comfortably below zero (it only ticks down inside Pursuit,
                // and the loop above only spent ~1 s of a 1 s cooldown) before testing the "in contact"
                // case, so the very next standoff tick is guaranteed eligible to hit rather than riding
                // on float-rounding to land exactly on zero.
                for (int i = 0; i < 10; i++) hutch.TickMobility(Dt, farMax, recorder);
                Assert.AreEqual(0f, recorder.TotalDamage, "still not in contact — still 0 damage");

                // "In contact" is the standoff ring itself (MowerHutch.PursuitStandoff, 2 m) — the
                // closest a hutch ever gets, since TickPursuit stops closing once it reaches it.
                Vector3 contactPoint = go.transform.position + Vector3.forward * 2f;
                hutch.TickMobility(Dt, contactPoint, recorder);
                Assert.That(recorder.TotalDamage, Is.EqualTo(38f).Within(0.01f),
                    "a hutch in contact with Max must deal 38 damage on this tick — Brute's own contactDamage");

                float afterFirstHit = recorder.TotalDamage;
                hutch.TickMobility(Dt, contactPoint, recorder);
                Assert.AreEqual(afterFirstHit, recorder.TotalDamage,
                    "contact damage must not re-fire again inside the same 1 s cooldown window");

                // Breaking contact must stop the damage even once the cooldown has re-armed.
                for (int i = 0; i < 90; i++) hutch.TickMobility(Dt, farMax, recorder); // 1.5 s, > the cooldown
                Assert.AreEqual(afterFirstHit, recorder.TotalDamage,
                    "a hutch not in contact must deal 0 additional damage even once its cooldown has re-armed");
            }
            finally
            {
                Object.DestroyImmediate(go);
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
