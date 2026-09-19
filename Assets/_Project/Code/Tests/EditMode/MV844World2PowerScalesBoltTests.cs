using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-844, Lee: "make damage 8 levels (level 4 should be at the same level as now, so I want to
    /// make it more powerful than it is now), relabel it to power, and as damage increases make the
    /// laser bolts wider and more red." World 2's <c>p_dmg</c> (PRIMARY root, DAMAGE -&gt; POWER) rises
    /// from a 4-level cap to 8, continuing its existing 20%/level damage step past L4
    /// (9 * (1 + 0.2*(L-1))): L4 stays 14.4, L8 lands at 21.6. The LPPE bolt's own sheath now scales
    /// wider and redder with POWER's own resolved level fraction
    /// (<see cref="PulseLaser.PowerVisualStrength"/> -&gt; <see cref="CombatVfxTuning.LppeBolt"/>) rather
    /// than sitting fixed at its old L1-only look.
    ///
    /// Fails on 10fce02: does not compile against that commit --
    /// <c>error CS1061: 'PulseLaser' does not contain a definition for 'PowerVisualStrength'</c> --
    /// since neither the property nor the bolt's own level-scaling exist there yet (quoted in full in
    /// the fix comment). <c>p_dmg</c>'s <c>maxLevel</c> is also still 4 on <c>rig_board.world2.json</c>
    /// there, so even with the property stubbed out the 5th <see cref="RigState.RaiseLevel"/> call
    /// below would refuse and <c>RigState.Level("p_dmg")</c> would never reach 8.
    /// </summary>
    public sealed class MV844World2PowerScalesBoltTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigBoard.ResetForTests();
            SeekerPulse.ResetForTests();
        }

        [Test]
        public void World2PowerCapsAt8AndTheBoltsSheathScalesWithItsResolvedLevel()
        {
            WeaponSystemState.ApplyWeaponCoreMorph(1); // World 2 board, ActivePrimary -> Lppe, p_dmg re-granted at L1
            Assert.That(RigBoard.MaxLevel("p_dmg"), Is.EqualTo(8), "World 2's POWER track must cap at 8 levels");

            var laserGo = new GameObject("MV-844 test PulseLaser");
            PulseLaser laser = laserGo.AddComponent<PulseLaser>();
            try
            {
                // --- L4: same damage as today's old cap, resolved through the live gameplay path. ---
                RigState.RaiseLevel("p_dmg");
                RigState.RaiseLevel("p_dmg");
                RigState.RaiseLevel("p_dmg");
                Assert.That(RigState.Level("p_dmg"), Is.EqualTo(4), "test precondition: p_dmg raised to L4");
                Assert.That(laser.EffectiveDamagePerPulse, Is.EqualTo(14.4f).Within(0.01f),
                    "L4's resolved LPPE damage per pulse must stay 9*(1+0.2*3) = 14.4, unchanged by this ticket");

                // --- raise to the new L8 cap and read the resolved damage/bolt together. ---
                while (RigState.RaiseLevel("p_dmg")) { }
                Assert.That(RigState.Level("p_dmg"), Is.EqualTo(8), "p_dmg must reach the new 8-level cap");
                Assert.That(laser.EffectiveDamagePerPulse, Is.EqualTo(21.6f).Within(0.01f),
                    "L8's resolved LPPE damage per pulse must be 9*(1+0.2*7) = 21.6");
                Assert.That(laser.PowerVisualStrength, Is.EqualTo(1f).Within(1e-4f),
                    "test precondition: L8 of an 8-level cap must resolve to a full 1.0 power fraction");

                SeekerPulse pulse = SeekerPulse.Fire(Vector3.zero, Vector3.forward, PulseLaser.DefaultPulseSpeed,
                    PulseLaser.DefaultPulseTurnRateDegPerSec, PulseLaser.DefaultPulseLifetime,
                    laser.EffectiveDamagePerPulse, laser.LockRange, PulseLaser.DefaultLockHalfAngle,
                    powerLevelFraction: laser.PowerVisualStrength);
                try
                {
                    MeshRenderer sheath = pulse.BoltRendererForTests;
                    Assert.IsNotNull(sheath, "test precondition: SeekerPulse must build a 'Sheath' child");
                    float sheathDiameter = Mathf.Max(sheath.bounds.size.x, sheath.bounds.size.y);
                    Assert.That(sheathDiameter, Is.EqualTo(0.44f).Within(0.01f),
                        $"L8's resolved sheath diameter ({sheathDiameter:0.000}m) must be 0.44m +/-0.01");

                    var mpb = new MaterialPropertyBlock();
                    sheath.GetPropertyBlock(mpb);
                    Color sheathColor = mpb.GetColor("_BaseColor");
                    Assert.That(sheathColor.r, Is.EqualTo(1.00f).Within(0.02f),
                        $"L8's resolved sheath colour red ({sheathColor.r:0.000}) is not 1.00");
                    Assert.That(sheathColor.g, Is.EqualTo(0.08f).Within(0.02f),
                        $"L8's resolved sheath colour green ({sheathColor.g:0.000}) is not 0.08");
                    Assert.That(sheathColor.b, Is.EqualTo(0.04f).Within(0.02f),
                        $"L8's resolved sheath colour blue ({sheathColor.b:0.000}) is not 0.04");
                }
                finally
                {
                    Object.DestroyImmediate(pulse.gameObject);
                }
            }
            finally
            {
                Object.DestroyImmediate(laserGo);
            }
        }
    }
}
