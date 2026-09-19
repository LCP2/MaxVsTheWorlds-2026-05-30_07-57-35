using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-846, Lee: "add capacity — I run out of weapon too often and this needs to be upgradable."
    /// World 2's board had no capacity node for the LPPE at all — World 1's <c>p_flw</c> CAPACITY cuts
    /// the RCDA's own drain and the LPPE never reads it, so there was no way to enlarge the LPPE's tank.
    ///
    /// Asserts RESOLVED values only (MV-465 Tier 2): <see cref="PulseLaser.EffectiveMaxEnergy"/> read
    /// live, the real <see cref="EnergyPool"/> a live <see cref="PulseLaser"/> actually built, and that
    /// pool's own <see cref="EnergyPool.Current"/> spent down tick-by-tick at the LPPE's real pulse
    /// cadence — never an authored constant duplicated on both sides of an assertion (MV-465 Tier 1 ban).
    ///
    /// Fails on 10fce02: <c>rig_board.world2.json</c> carries no <c>p_cap</c> node at all, so
    /// <see cref="RigState.AcquireCap"/>("p_cap") returns false and <see cref="RigState.Level"/>("p_cap")
    /// stays 0 forever; <see cref="PulseLaser"/> has no <c>EffectiveMaxEnergy</c> member at all, so this
    /// test does not compile against that commit.
    /// </summary>
    public sealed class MV846CapacityEnlargesLppeTankTests
    {
        private static readonly MethodInfo PulseLaserAwake =
            typeof(PulseLaser).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PulseLaserTankField =
            typeof(PulseLaser).GetField("_tank", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        [TearDown]
        public void Clear()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigBoard.ResetForTests();
        }

        [Test]
        public void CapacityAtL5RaisesTheLppeTankTo315AndSustainsFireAtLeast25Seconds()
        {
            WeaponSystemState.ApplyWeaponCoreMorph(1); // World 2 board, ActivePrimary -> Lppe, p_dmg at L1

            RigState.AcquireCap("p_cap");
            for (int i = 1; i < 5; i++) RigState.RaiseLevel("p_cap");
            Assert.AreEqual(5, RigState.Level("p_cap"), "test precondition: p_cap must reach its L5 cap");

            var go = new GameObject("MV-846 test PulseLaser");
            try
            {
                PulseLaser laser = go.AddComponent<PulseLaser>();
                PulseLaserAwake.Invoke(laser, null); // Awake doesn't run for AddComponent outside Play mode

                Assert.That(laser.EffectiveMaxEnergy, Is.EqualTo(315f).Within(0.01f),
                    "p_cap at L5 must resolve the LPPE's tank max to 140*(1+0.25*5) = 315");

                var tank = (EnergyPool)PulseLaserTankField.GetValue(laser);
                Assert.That(tank.Max, Is.EqualTo(315f).Within(0.01f),
                    "the LPPE's real EnergyPool must be built at the resolved 315 max, not the base 140");
                Assert.That(tank.Current, Is.EqualTo(315f).Within(0.01f),
                    "test precondition: the tank must start full");

                // Continuous fire from full: spend one pulse's worth of energy every PulseInterval,
                // never letting the regen delay (0.35s, longer than the 0.22s pulse interval) actually
                // kick in -- the same "real TrySpend on the real tank" idiom MV760PrimaryGaugeSourceTests
                // already uses, rather than comparing the authored constants against each other.
                float elapsed = 0f;
                float interval = laser.PulseInterval;
                float cost = laser.EnergyPerPulse;
                const float cap = 40f; // generous ceiling so a regression reads as an assertion, not a hang
                while (elapsed < cap)
                {
                    tank.Tick(interval);
                    if (!tank.TrySpend(cost)) break;
                    elapsed += interval;
                }

                Assert.That(elapsed, Is.GreaterThanOrEqualTo(25f),
                    $"continuous fire from a full, maxed-CAPACITY tank must last at least 25s before " +
                    $"firing locks -- only lasted {elapsed:0.00}s");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
