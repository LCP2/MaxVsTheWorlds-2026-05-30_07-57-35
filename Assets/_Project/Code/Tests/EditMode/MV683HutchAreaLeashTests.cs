using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-683 (Lee's playtest read): a23's mobile shed lifted off, pursued Max out of its own area and
    /// "vanished" — it hadn't died, nothing bounded <see cref="MowerHutch.TickPursuit"/> to the area it
    /// was authored inside, so it just walked through the open gate and kept going. This pins the fix:
    /// <see cref="MowerHutch.SetAreaFootprint"/> plus the leash <see cref="MowerHutch.TickMobility"/> now
    /// applies after every state's own movement. A Tier-2 resolved-value assertion (test policy) — it
    /// drives <c>TickMobility</c> with a target placed far outside the hutch's authored area and asserts
    /// the hutch's own resolved <c>transform.position</c> stays inside that area's footprint, where before
    /// this fix it walked to the target unbounded.
    /// </summary>
    public sealed class MV683HutchAreaLeashTests
    {
        private const float BodyWidth = 2.25f;
        private const float BodyHeight = 1.5f;
        private const float Dt = 1f / 60f;

        // Same distinctive far-off origin idiom MV548/MV618 use — EditMode tests share one physics scene
        // for the whole cc-verify run with no per-test reset, so a rig built near the world origin can
        // start out overlapping a previous test's leftover collider.
        private static readonly Vector3 RigOrigin = new Vector3(214009f, 0f, -38217f);

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

        [Test]
        public void PursuingHutch_NeverLeavesItsAuthoredAreaFootprint_EvenWithATargetFarOutsideIt()
        {
            // MowerHutch.BuildCore destroys the primitive core's stock collider via Object.Destroy,
            // which is edit-mode-illegal and logs an [Error] regardless of who calls Awake — same shape
            // MV548/MV618 carry.
            LogAssert.ignoreFailingMessages = true;

            Vector3 groundedCenter = RigOrigin + new Vector3(0f, BodyHeight * 0.5f, 0f);
            (GameObject go, MowerHutch hutch) = BuildMobileShed(groundedCenter);
            try
            {
                // A 20x20 m room centred on the hutch's own grounded spot — same Rect convention
                // MapZone.Footprint/MapRuntime.BuildFactory hands a real mobile shed.
                var footprint = new Rect(groundedCenter.x - 10f, groundedCenter.z - 10f, 20f, 20f);
                hutch.SetAreaFootprint(footprint);

                // Force straight to Pursuit via first damage (MV548/MV618 precedent).
                hutch.TakeDamage(new DamageInfo(1f, Vector3.zero, Vector3.forward, Team.Player));
                Vector3 farOutsideTarget = groundedCenter + Vector3.forward * 500f; // way past the room
                hutch.TickMobility(0f, farOutsideTarget);
                Assert.AreEqual(MowerHutch.ShedMobility.LiftOff, hutch.MobilityState,
                    "first damage must trigger lift-off");
                for (float elapsed = 0f; elapsed < 2.5f; elapsed += Dt)
                    hutch.TickMobility(Dt, farOutsideTarget);
                Assert.AreEqual(MowerHutch.ShedMobility.Pursuit, hutch.MobilityState,
                    "2.5 s of lift-off ticks must complete into Pursuit");

                // 60 s of continuous pursuit toward a target 500 m outside the room — at the fixed
                // 0.75 m/s Brute pace this would, unbounded, cover ~45 m: comfortably enough to walk
                // straight out of the 20 m room if nothing leashed it.
                for (int i = 0; i < 3600; i++)
                    hutch.TickMobility(Dt, farOutsideTarget);

                Vector3 resolved = go.transform.position;
                Assert.That(resolved.x, Is.InRange(footprint.xMin, footprint.xMax),
                    "a mobile shed's resolved X must stay inside its own area's footprint, even chasing a target far outside it");
                Assert.That(resolved.z, Is.InRange(footprint.yMin, footprint.yMax),
                    "a mobile shed's resolved Z must stay inside its own area's footprint, even chasing a target far outside it");
            }
            finally
            {
                Object.DestroyImmediate(go);
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
