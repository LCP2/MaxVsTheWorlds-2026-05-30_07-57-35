using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using MaxWorlds.VFX;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-580: the Sentinel is the first body in the roster that is both legged AND mobile (every
    /// existing legged kind — Blinker, Gunner — earns its legs precisely by never walking). A
    /// procedural gait was required rather than reusing <c>RobotRig.SpinWheels</c>' wheel-spin idiom,
    /// since a leg that only spun would not read as a leg. <see cref="LegGaitDriver"/> is that gait:
    /// phase driven by distance travelled (never wall-clock time alone, so it never walks on the spot),
    /// eased back to a neutral rest pose when stationary (so it never freezes mid-stride), and cheap
    /// enough for iOS (no per-frame allocation).
    /// </summary>
    public sealed class LegGaitDriverTests
    {
        private static Transform[] NewLegs(int count)
        {
            var legs = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                var pivot = new GameObject($"Leg{i}").transform;
                // A real leg pivot always has a part hanging under it (see RobotBodies.AddLeg) — a
                // bare pivot's OWN position never moves under rotation, only a CHILD's does, so a
                // stand-in "foot" child is what makes this fixture representative of the real rig.
                var foot = new GameObject("Foot").transform;
                foot.SetParent(pivot, worldPositionStays: false);
                foot.localPosition = new Vector3(0f, -0.15f, 0f);
                legs[i] = pivot;
            }
            return legs;
        }

        private static Transform FootOf(Transform leg) => leg.GetChild(0);

        private static void Destroy(Transform[] legs)
        {
            foreach (var l in legs)
                if (l != null) Object.DestroyImmediate(l.gameObject);
        }

        [Test]
        public void Tick_PhaseAdvancesFasterAtHigherMovementSpeed()
        {
            var slow = new LegGaitDriver();
            var fast = new LegGaitDriver();
            const float dt = 1f / 60f;

            // The very first Tick ever seeds _lastPosition from the position it's handed (there is no
            // "previous frame" to diff against yet), so it always measures zero distance regardless of
            // where it's called with — a warm-up call establishes a real baseline for each driver
            // before the timed step below, the same way a live sentinel's first Update after Init does.
            slow.Tick(null, Vector3.zero, dt);
            fast.Tick(null, Vector3.zero, dt);

            // Same elapsed time, different distance covered per tick — the ONLY thing that should
            // differ between the two drivers' resulting phase.
            slow.Tick(null, new Vector3(0.02f, 0f, 0f), dt);
            fast.Tick(null, new Vector3(0.10f, 0f, 0f), dt);

            Assert.That(fast.Phase, Is.GreaterThan(slow.Phase),
                $"the faster mover's gait phase ({fast.Phase}) did not advance past the slower " +
                $"mover's ({slow.Phase}) — the cycle rate must scale with movement speed");
        }

        [Test]
        public void Tick_MovesALegsFootWorldPositionWhileWalking_AndSettlesItWhenStationary()
        {
            var legs = NewLegs(1);
            try
            {
                Transform foot = FootOf(legs[0]);
                var driver = new LegGaitDriver();
                const float dt = 1f / 60f;
                Vector3 pos = Vector3.zero;

                Vector3 restFootPosition = foot.position;

                // Walk for a couple of seconds' worth of ticks, always moving, tracking the LARGEST
                // deviation seen — sampling only the final frame risks landing on a moment the sine
                // wave happens to cross zero, which would falsely read as "never moved".
                float maxDeviation = 0f;
                for (int i = 0; i < 120; i++)
                {
                    pos += new Vector3(0.05f, 0f, 0f);
                    driver.Tick(legs, pos, dt);
                    maxDeviation = Mathf.Max(maxDeviation, Vector3.Distance(restFootPosition, foot.position));
                }
                Assert.That(maxDeviation, Is.GreaterThan(0.005f),
                    "the leg's foot never moved away from its rest position while the sentinel was walking");

                // Stop moving (position no longer changes) and give the amplitude time to ease to 0.
                for (int i = 0; i < 120; i++) driver.Tick(legs, pos, dt);
                Vector3 settledFootPosition = foot.position;

                Assert.That(Vector3.Distance(settledFootPosition, restFootPosition), Is.LessThan(0.005f),
                    "the leg did not settle back to its neutral rest pose after the sentinel stopped " +
                    "moving — that's a leg frozen mid-stride, the exact 'slides with rigid limbs' look " +
                    "this ticket exists to avoid");
            }
            finally { Destroy(legs); }
        }

        [Test]
        public void Tick_AllocatesNoGcMemoryPerCall()
        {
            var legs = NewLegs(3);
            try
            {
                var driver = new LegGaitDriver();
                // Warm up first — the very first call may lazily touch something the constraint would
                // otherwise (mis)attribute to the method itself.
                driver.Tick(legs, new Vector3(0.01f, 0f, 0f), 1f / 60f);

                Vector3 pos = new Vector3(0.02f, 0f, 0f);
                Assert.That(() => driver.Tick(legs, pos, 1f / 60f),
                    Is.Not.AllocatingGCMemory());
            }
            finally { Destroy(legs); }
        }
    }
}
