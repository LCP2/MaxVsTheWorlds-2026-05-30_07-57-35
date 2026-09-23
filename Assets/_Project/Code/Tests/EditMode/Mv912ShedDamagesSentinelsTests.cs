using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-912 (Lee, 2026-09-23): a moving shed's contact damage (MV-618: <c>ContactDamage</c> 38,
    /// once per <see cref="MaxWorlds.Enemies.RobotCompositionTuning.DefaultContactCooldown"/>) only
    /// ever landed on its pursuit target (Max) — a deployed <see cref="Sentinel"/> standing right next
    /// to a pursuing shed took nothing. Testing policy Rule 1 allows one new test per ticket, so every
    /// AC is pinned in this single method: a Grounded shed leaves an adjacent Sentinel untouched (AC2);
    /// a Pursuing shed damages a Sentinel it's in contact with even while Max himself is still far out
    /// of range, and keeps closing on Max the whole time rather than stalling next to the Sentinel
    /// (AC1 + AC4 — a retarget bug would have frozen the shed at the Sentinel's 0.5 m standoff instead
    /// of the ~0.88 m of further travel toward Max asserted below); and Max still takes the same
    /// ContactDamage once the shed reaches him (AC3).
    /// </summary>
    public sealed class Mv912ShedDamagesSentinelsTests
    {
        private const float BodyWidth = 2.25f;   // MV-541's shed footprint
        private const float BodyHeight = 1.5f;
        private const float Dt = 1f / 60f;
        private const float ContactDamage = 38f; // MowerHutch.ContactDamage, Brute's own figure (MV-618)

        // Distinctive far-off origin — EditMode tests share one physics scene for the whole cc-verify
        // run with no per-test reset (MV618/MV548 precedent).
        private static readonly Vector3 RigOrigin = new Vector3(162355f, 0f, -84933f);

        [SetUp]
        [TearDown]
        public void Clear() => Sentinel.ResetRegistry();

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
            typeof(MowerHutch).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(hutch, null);
            hutch.ConfigureMobility(true);

            Physics.SyncTransforms();
            return (go, hutch);
        }

        /// <summary>Deployed well clear (+30 m Y) of the hutch's own CharacterController/collider so
        /// this rig never triggers an unrelated physical depenetration between the two — MowerHutch's
        /// contact-range check flattens to XZ (<c>to.y = 0f</c>, unchanged by this ticket), so a
        /// Sentinel parked high above still reads as "in contact" exactly like one standing at the same
        /// height would.</summary>
        private static Sentinel BuildSentinel(Vector3 position, GameObject go)
        {
            var sentinel = go.AddComponent<Sentinel>();
            sentinel.Init(position, maxHp: 100f, range: 7f, fireInterval: 0.6f,
                moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
            return sentinel;
        }

        /// <summary>Minimal stub so Max's own contact damage can be observed without a live
        /// PlayerController — same idiom <see cref="MV618HutchPaceAndDamageTests"/> uses.</summary>
        private sealed class DamageRecorder : IDamageable
        {
            public float TotalDamage;
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info) => TotalDamage += info.Amount;
        }

        [Test]
        public void MovingShedDamagesNearbySentinelAndStillDamagesMax_GroundedDoesNotAndPursuitNeverLeavesMax()
        {
            // MowerHutch.BuildCore destroys the primitive core's stock collider via Object.Destroy,
            // edit-mode-illegal and logged regardless of who calls Awake (MV618/MV548 precedent).
            LogAssert.ignoreFailingMessages = true;

            Vector3 groundedCenter = RigOrigin + new Vector3(0f, BodyHeight * 0.5f, 0f);
            (GameObject hutchGo, MowerHutch hutch) = BuildMobileShed(groundedCenter);
            var sentinelGo = new GameObject("Sentinel");
            var maxRecorder = new DamageRecorder();

            try
            {
                Sentinel sentinel = BuildSentinel(groundedCenter + new Vector3(0.5f, 30f, 0f), sentinelGo);

                // --- AC2: Grounded, Sentinel right next to it (flat) — must take nothing, and Max
                // (still far away) must take nothing either. ---
                Vector3 farMax = groundedCenter + Vector3.forward * 50f; // outside the 10 m lift trigger
                for (int i = 0; i < 60; i++) hutch.TickMobility(Dt, farMax, maxRecorder);
                Assert.AreEqual(MowerHutch.ShedMobility.Grounded, hutch.MobilityState,
                    "must stay Grounded with Max out of range and no damage taken");
                Assert.AreEqual(100f, sentinel.HealthCurrent, 1e-3f,
                    "a Grounded shed must not damage a Sentinel merely parked next to it (AC2)");
                Assert.AreEqual(0f, maxRecorder.TotalDamage, "a Grounded shed must not damage Max either");

                // --- Trigger + lift-off into Pursuit (MV-618/MV-548 precedent). ---
                hutch.TakeDamage(new DamageInfo(1f, Vector3.zero, Vector3.forward, Team.Player));
                hutch.TickMobility(0f, farMax, maxRecorder);
                Assert.AreEqual(MowerHutch.ShedMobility.LiftOff, hutch.MobilityState,
                    "first damage must trigger lift-off");
                for (float elapsed = 0f; elapsed < 2.5f; elapsed += Dt)
                    hutch.TickMobility(Dt, farMax, maxRecorder);
                Assert.AreEqual(MowerHutch.ShedMobility.Pursuit, hutch.MobilityState,
                    "2.5 s of lift-off ticks must complete into Pursuit");

                // --- AC1 + AC4: Pursuing, Max still far away, Sentinel in flat contact range the whole
                // time. The hutch must still damage the Sentinel (contact damage isn't gated on Max
                // being in range too) AND must keep closing on Max rather than stalling at the
                // Sentinel's own standoff — a retargeting bug would leave displacementTowardMax ~= 0. ---
                Vector3 beforeSentinelContact = hutchGo.transform.position;
                for (int i = 0; i < 70; i++) hutch.TickMobility(Dt, farMax, maxRecorder); // > 1 s cooldown
                float displacementTowardMax = Vector3.Distance(beforeSentinelContact, hutchGo.transform.position);

                Assert.AreEqual(100f - ContactDamage, sentinel.HealthCurrent, 1e-2f,
                    "a Pursuing shed must deal its ContactDamage to a Sentinel it's in contact with (AC1), " +
                    "even while Max himself is still out of contact range");
                Assert.AreEqual(0f, maxRecorder.TotalDamage,
                    "Max is still 50 m away — this hit must be the Sentinel's alone, not his");
                Assert.That(displacementTowardMax, Is.EqualTo(70f * Dt * 0.75f).Within(0.05f),
                    "the shed must keep closing on Max at Brute pace the whole time — a shed that had " +
                    "retargeted to the adjacent Sentinel would have stalled at ITS 2 m standoff instead (AC4)");

                // --- AC3: move the Sentinel out of the way, let the cooldown re-arm, then let the shed
                // actually reach Max — his own ContactDamage must be unchanged by any of the above. ---
                sentinel.transform.position = RigOrigin + new Vector3(9000f, 30f, 9000f);
                for (int i = 0; i < 70; i++) hutch.TickMobility(Dt, farMax, maxRecorder); // re-arm cooldown
                Assert.AreEqual(0f, maxRecorder.TotalDamage, "still 50 m from Max — no hit yet");

                Vector3 contactPoint = hutchGo.transform.position + Vector3.forward * 2f; // the standoff ring
                hutch.TickMobility(Dt, contactPoint, maxRecorder);
                Assert.AreEqual(ContactDamage, maxRecorder.TotalDamage, 1e-2f,
                    "Max must still take the unchanged ContactDamage once the shed reaches him (AC3)");
            }
            finally
            {
                Object.DestroyImmediate(hutchGo);
                Object.DestroyImmediate(sentinelGo);
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
