using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>MV-688 AC1: the Grate Lurker's own RATTLE -> EMERGED -> SUBMERGING -> SUBMERGED cycle,
    /// driven purely through <see cref="LurkerCycle"/> — no live RobotEnemy/GameObject needed, same
    /// "pull the timing math into a small pure static function and test that directly" idiom
    /// <c>BlinkerTeleportTests</c> already uses for exactly this reason (RobotEnemy's own Update isn't
    /// driven directly in EditMode). Fails to compile on the MV-701 merge commit: LurkerCycle doesn't
    /// exist yet.</summary>
    public sealed class MV688LurkerCycleTests
    {
        [Test]
        public void SubmergedLurker_RattlesEmergesAndReappearsAtAnotherGrate_OnMaxsSchedule()
        {
            var grateA = new Vector3(0f, 0f, 0f);
            var grateB = new Vector3(4f, 0f, 0f);   // two grates 4 m apart
            var grates = new List<Vector3> { grateA, grateB };
            const float distToMax = 4f;             // Max 4 m from grate A — inside WakeRadius (5 m)

            var phase = LurkerCycle.Phase.Submerged;
            float elapsed = LurkerCycle.CooldownDuration;   // freshly garrisoned: ready to rattle immediately

            // Awake (the universal "dormant until seen" latch, owned by the caller) and within
            // WakeRadius: SUBMERGED must begin a RATTLE on the very next tick, even a zero-length one.
            phase = LurkerCycle.Step(phase, ref elapsed, 0f, awake: true, distToMax, hitsThisEmergence: 0);
            Assert.AreEqual(LurkerCycle.Phase.Rattle, phase,
                "RATTLE must begin the instant it is awake and Max is within WakeRadius");

            // Advance 1.1 s (RattleDuration 0.8 s + 0.3 s into EMERGED): EMERGED, damageable.
            phase = LurkerCycle.Step(phase, ref elapsed, 1.1f, awake: true, distToMax, hitsThisEmergence: 0);
            Assert.AreEqual(LurkerCycle.Phase.Emerged, phase, "1.1s past RATTLE must land in EMERGED");
            Assert.IsTrue(LurkerCycle.IsDamageable(phase), "EMERGED must be damageable — it's a normal target");

            // Advance a further 3.1 s (2.7 s left of the 3 s EMERGED cap + the 0.4 s SUBMERGING beat):
            // fully SUBMERGED again, invulnerable.
            phase = LurkerCycle.Step(phase, ref elapsed, 3.1f, awake: true, distToMax, hitsThisEmergence: 0);
            Assert.AreEqual(LurkerCycle.Phase.Submerged, phase, "3.1s past EMERGED must land fully back in SUBMERGED");
            Assert.IsFalse(LurkerCycle.IsDamageable(phase), "SUBMERGED must be invulnerable again");

            // And it reappears at the OTHER authored grate, within ReappearRadius (6 m; A-B is 4 m).
            Vector3 reappear = LurkerCycle.PickReappearGrate(grateA, grates, LurkerCycle.ReappearRadius);
            Assert.AreEqual(grateB, reappear, "must reappear at the other authored grate, not the one it left");
        }
    }
}
