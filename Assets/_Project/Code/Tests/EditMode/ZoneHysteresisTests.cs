using NUnit.Framework;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-447 cause 3: <c>MapRoutes.Waypoint</c> answers differently depending on which room it is
    /// told a robot is standing in, and a robot straddling a zone boundary can have the raw
    /// <c>MapData.ZoneAt</c> answer flip from one tick to the next on sub-metre movement. Without
    /// hysteresis, that flip sends the robot toward materially different waypoints tick to tick.
    /// <see cref="ZoneHysteresis"/> is the pulled-out, pure decision this regresses against directly —
    /// same idiom as <see cref="PursuitStallTests"/>.
    /// </summary>
    public sealed class ZoneHysteresisTests
    {
        [Test]
        public void FirstResolve_AdoptsWhateverIsReportedImmediately()
        {
            var zh = new ZoneHysteresis();
            Assert.AreEqual("area1", zh.Resolve("area1", 0.016f), "a robot with no prior zone has nothing to hold onto");
        }

        [Test]
        public void FlippingEveryTick_NeverAdoptsTheNewZone()
        {
            var zh = new ZoneHysteresis();
            zh.Resolve("area1", 0.016f);

            // A boundary straddle: the raw answer alternates every single tick, never holding "area2"
            // continuously for long enough to count.
            for (int i = 0; i < 50; i++)
            {
                string raw = (i % 2 == 0) ? "area2" : "area1";
                string routed = zh.Resolve(raw, 0.016f);
                Assert.AreEqual("area1", routed, $"tick {i}: adopted a zone that never held continuously");
            }
        }

        [Test]
        public void HoldingContinuouslyPastSwitchDelay_AdoptsTheNewZone()
        {
            var zh = new ZoneHysteresis();
            zh.Resolve("area1", 0.016f);

            const float dt = 0.05f;
            float elapsed = 0f;
            string routed = "area1";
            while (elapsed < ZoneHysteresis.SwitchDelay + 0.1f)
            {
                routed = zh.Resolve("area2", dt);
                elapsed += dt;
            }

            Assert.AreEqual("area2", routed, "area2 held continuously past SwitchDelay and must be adopted");
        }

        [Test]
        public void BriefFlickerBelowSwitchDelay_DoesNotCarryPartialProgressIntoTheNextHold()
        {
            var zh = new ZoneHysteresis();
            zh.Resolve("area1", 0.016f);

            const float dt = 0.05f;

            // Hold "area2" for close to (but under) SwitchDelay, then flick back to area1 before the
            // switch lands. The pending candidate must reset, not carry over stale progress.
            float firstHold = 0f;
            while (firstHold + dt < ZoneHysteresis.SwitchDelay)
            {
                Assert.AreEqual("area1", zh.Resolve("area2", dt));
                firstHold += dt;
            }
            Assert.AreEqual("area1", zh.Resolve("area1", 0.016f), "flicked back before the switch landed");

            // A second, later hold of area2 must need its own full SwitchDelay from zero: if the
            // earlier partial progress had carried over, this shorter hold would incorrectly adopt.
            float secondHold = 0f;
            string routed = "area1";
            while (secondHold + dt < ZoneHysteresis.SwitchDelay - firstHold)
            {
                routed = zh.Resolve("area2", dt);
                Assert.AreEqual("area1", routed, "must not have carried over progress from the earlier flicker");
                secondHold += dt;
            }
        }

        [Test]
        public void Reset_ForgetsEverything_LikeAFreshlyPooledRobot()
        {
            var zh = new ZoneHysteresis();
            zh.Resolve("area1", 0.016f);
            zh.Reset();

            Assert.AreEqual("area7", zh.Resolve("area7", 0.016f),
                "a pooled robot must not inherit the last one's idea of which room it was routing from");
        }
    }
}
