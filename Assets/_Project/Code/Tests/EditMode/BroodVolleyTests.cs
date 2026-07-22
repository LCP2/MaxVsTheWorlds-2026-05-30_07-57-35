using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Unit tests for Big Bermuda's SECOND attack (YT-157) — the brood volley. Both halves are pure
    /// maths with no scene: <see cref="BroodArc"/> (where a flung robot is at each instant) and
    /// <see cref="BroodVolley"/> (when the hatches open, when the robots fly, and how the wave ramps
    /// with enrage). Testing the cadence here is the same reason <see cref="BigBermudaBrain"/> is a
    /// pure class: the dodge/read window before the fling is a number that must not be able to drift.
    /// </summary>
    public sealed class BroodVolleyTests
    {
        [SetUp]
        public void SetUp() => DevTuning.Reset();

        [TearDown]
        public void TearDown() => DevTuning.Reset();

        // ---------------------------------------------------------------- BroodArc

        [Test]
        public void Arc_StartsAtTheHatch_EndsAtTheLanding()
        {
            var from = new Vector3(2f, 1.7f, 0f);
            var to = new Vector3(8f, 0.7f, 3f);

            Assert.That(BroodArc.PointAt(from, to, 4f, 0f), Is.EqualTo(from).Using(Vec()));
            Assert.That(BroodArc.PointAt(from, to, 4f, 1f), Is.EqualTo(to).Using(Vec()));
        }

        [Test]
        public void Arc_PeaksAboveTheMidpoint_ByTheApex()
        {
            var from = new Vector3(0f, 0f, 0f);
            var to = new Vector3(10f, 0f, 0f);

            Vector3 mid = BroodArc.PointAt(from, to, 4f, 0.5f);
            // Halfway across (x = 5) and 4 m up — the parabola peaks at t = 0.5.
            Assert.AreEqual(5f, mid.x, 1e-3f);
            Assert.AreEqual(4f, mid.y, 1e-3f, "the arc should peak a full apex above the line at its middle");
        }

        [Test]
        public void Arc_ClampsOutsideZeroToOne()
        {
            var from = Vector3.zero;
            var to = new Vector3(10f, 0f, 0f);
            Assert.That(BroodArc.PointAt(from, to, 4f, -1f), Is.EqualTo(from).Using(Vec()));
            Assert.That(BroodArc.PointAt(from, to, 4f, 2f), Is.EqualTo(to).Using(Vec()));
        }

        [Test]
        public void Muzzle_SitsOnTheCorrectFlank_AtHatchHeight()
        {
            // Boss facing +Z (identity). Right is +X; left hatch is -X.
            Vector3 left = BroodArc.Muzzle(Vector3.zero, Quaternion.identity, -1f, 2.2f, 1.7f);
            Vector3 right = BroodArc.Muzzle(Vector3.zero, Quaternion.identity, 1f, 2.2f, 1.7f);

            Assert.Less(left.x, 0f, "the left hatch is not on the left");
            Assert.Greater(right.x, 0f, "the right hatch is not on the right");
            Assert.AreEqual(1.7f, left.y, 1e-3f, "the muzzle is not at hatch height");
            Assert.AreEqual(1.7f, right.y, 1e-3f);
        }

        [Test]
        public void Landing_IsToTheSide_OnTheGround_AndSpreadsWithEachRobot()
        {
            Vector3 near = BroodArc.Landing(Vector3.zero, Quaternion.identity, 1f, 6f, 1.5f, 0.7f, 0f);
            Vector3 far = BroodArc.Landing(Vector3.zero, Quaternion.identity, 1f, 6f, 1.5f, 0.7f, 4f);

            Assert.AreEqual(0.7f, near.y, 1e-3f, "the add must land on the ground, not float");
            Assert.Greater(near.x, 0f, "a right-hatch add should land to the right");
            Assert.Greater(far.x, near.x, "each successive add in a volley should land further out, not stack");
        }

        // ---------------------------------------------------------------- BroodVolley cadence

        [Test]
        public void Volley_StartsShut_AndDoesNotFireImmediately()
        {
            var v = new BroodVolley();
            Assert.AreEqual(0f, v.SpawnWindup01, 1e-3f, "the hatches are open before the first interval");
            v.Tick(0.1f, enraged: false, canVent: true);
            Assert.IsFalse(v.JustFired, "it flung a volley on the very first tick, with no telegraph");
        }

        [Test]
        public void Volley_TelegraphsThenFires_AfterTheInterval()
        {
            var v = new BroodVolley();

            RunUntil(v, () => v.SpawnWindup01 > 0f, enraged: false, canVent: true, budget: 30f);
            Assert.Greater(v.SpawnWindup01, 0f, "the hatches never began to open");
            Assert.Less(v.SpawnWindup01, 1f, "it flung before it had told the player anything");

            bool fired = RunUntil(v, () => v.JustFired, enraged: false, canVent: true, budget: 5f);
            Assert.IsTrue(fired, "the volley never flung after telegraphing");
        }

        [Test]
        public void Volley_HoldsShut_WhileItCannotVent()
        {
            var v = new BroodVolley();
            // Never allow a vent (a permanent charge / at the add cap): it must never fire.
            for (float t = 0f; t < 40f; t += 0.1f)
            {
                v.Tick(0.1f, enraged: false, canVent: false);
                Assert.IsFalse(v.JustFired, "it flung a volley while it was forbidden to vent");
                Assert.AreEqual(0f, v.SpawnWindup01, 1e-3f, "the hatches opened while it could not vent");
            }
        }

        [Test]
        public void Volley_AbortsTheWindup_IfAChargeInterrupts()
        {
            var v = new BroodVolley();
            RunUntil(v, () => v.SpawnWindup01 > 0.2f, enraged: false, canVent: true, budget: 30f);

            // A charge starts mid-telegraph: the hatches must snap shut and nothing is flung.
            v.Tick(0.05f, enraged: false, canVent: false);
            Assert.AreEqual(0f, v.SpawnWindup01, 1e-3f, "the hatches stayed open through a charge");
            Assert.IsFalse(v.JustFired, "it flung mid-charge, blurring the two reads");
        }

        [Test]
        public void Volley_ReArmsImmediately_AfterAnAbort_SoItStillLandsBetweenCharges()
        {
            var v = new BroodVolley();
            RunUntil(v, () => v.SpawnWindup01 > 0.2f, enraged: false, canVent: true, budget: 30f);
            v.Tick(0.05f, enraged: false, canVent: false);   // abort

            // The window reopens: it should telegraph again within a windup, not wait a whole interval.
            bool fired = RunUntil(v, () => v.JustFired, enraged: false, canVent: true,
                                  budget: BossTuning.VolleyWindup + 0.5f);
            Assert.IsTrue(fired, "after an abort it waited a whole interval instead of retrying");
        }

        [Test]
        public void Volley_FiresAgain_AfterOpenHold()
        {
            var v = new BroodVolley();
            int fires = 0;
            for (float t = 0f; t < 60f && fires < 2; t += 0.05f)
            {
                v.Tick(0.05f, enraged: false, canVent: true);
                if (v.JustFired) fires++;
            }
            Assert.AreEqual(2, fires, "the volley did not repeat — a one-shot attack is not a signature");
        }

        [Test]
        public void Volley_ComesFasterAndBigger_WhenEnraged()
        {
            float calm = TimeToFirstFire(enraged: false);
            float enraged = TimeToFirstFire(enraged: true);
            Assert.Less(enraged, calm, "an enraged boss should fling its waves sooner, not the same");

            var v = new BroodVolley();
            Assert.Greater(v.RobotsThisVolley(enraged: true), v.RobotsThisVolley(enraged: false),
                "an enraged volley should be a bigger wave");
        }

        [Test]
        public void Volley_Interval_IsLiveTunable()
        {
            DevTuning.BossVolleyInterval = 2f;   // much shorter than the authored default
            float fast = TimeToFirstFire(enraged: false);
            DevTuning.Reset();
            float authored = TimeToFirstFire(enraged: false);
            Assert.Less(fast, authored, "a shortened interval on the Settings panel did not retime the volley");
        }

        // ---------------------------------------------------------------- helpers

        private static float TimeToFirstFire(bool enraged)
        {
            var v = new BroodVolley();
            for (float t = 0f; t < 60f; t += 0.05f)
            {
                v.Tick(0.05f, enraged, canVent: true);
                if (v.JustFired) return t;
            }
            return float.MaxValue;
        }

        private static bool RunUntil(BroodVolley v, System.Func<bool> done, bool enraged, bool canVent, float budget)
        {
            for (float t = 0f; t < budget; t += 0.05f)
            {
                v.Tick(0.05f, enraged, canVent);
                if (done()) return true;
            }
            return done();
        }

        private static System.Collections.Generic.IComparer<Vector3> Vec() => new VecCompare();

        private sealed class VecCompare : System.Collections.Generic.IComparer<Vector3>
        {
            public int Compare(Vector3 a, Vector3 b) => (a - b).sqrMagnitude <= 1e-6f ? 0 : 1;
        }
    }
}
