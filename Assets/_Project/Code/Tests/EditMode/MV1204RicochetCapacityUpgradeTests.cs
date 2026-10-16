using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1204, Lee: "RICOCHET needs a way to upgrade its energy capacity if it runs out." The City's
    /// board had no CAPACITY node for RICOCHET at all -- Ricochet.Awake built its tank as a flat
    /// <see cref="BlasterTuning.MaxEnergy"/> (140), never retuned, so there was no way to enlarge it.
    ///
    /// Asserts RESOLVED values only (MV-465 Tier 2): the real <see cref="EnergyPool"/> a live
    /// <see cref="Ricochet"/> actually built, read off its own private tank field after a Tick (so the
    /// per-tick Retune, not just Awake's construction-time value, is exercised) -- never an authored
    /// constant compared to itself.
    ///
    /// Fails on base commit 7915970 (main HEAD before this ticket): <c>rig_board.city.json</c> carries
    /// no <c>p_cap</c> node at all, so <see cref="RigState.AcquireCap"/>("p_cap") returns false and
    /// <see cref="RigState.Level"/>("p_cap") stays 0 forever; <see cref="RicochetTrackKind"/> has no
    /// <c>Capacity</c> member and <see cref="Ricochet"/> never retunes its tank, so the L4 case still
    /// reads 140 instead of resolving to 280.
    /// </summary>
    public sealed class MV1204RicochetCapacityUpgradeTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo RicochetAwake = typeof(Ricochet).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo RicochetTick = typeof(Ricochet).GetMethod("Tick", NonPublicInstance);
        private static readonly FieldInfo RicochetTankField = typeof(Ricochet).GetField("_tank", NonPublicInstance);

        private GameObject _weaponGo;

        [SetUp]
        [TearDown]
        public void Clear()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigBoard.ResetForTests();
            if (_weaponGo != null) Object.DestroyImmediate(_weaponGo);
            _weaponGo = null;
        }

        [Test]
        public void CapacityAtL4RaisesTheRicochetTankTo280AfterATick()
        {
            WeaponSystemState.ApplyWeaponCoreMorph(WorldCatalog.IndexOf(WorldIds.City));
            Assert.AreEqual(WeaponCatalog.PrimaryKind.Ricochet, WeaponSystemState.ActivePrimary,
                "test precondition: the City morph must make RICOCHET the active primary");

            _weaponGo = new GameObject("MV1204 Ricochet");
            Ricochet weapon = _weaponGo.AddComponent<Ricochet>();
            RicochetAwake.Invoke(weapon, null); // Awake doesn't run for AddComponent outside Play mode
            RicochetTick.Invoke(weapon, new object[] { 0f });

            var tank = (EnergyPool)RicochetTankField.GetValue(weapon);
            Assert.That(tank.Max, Is.EqualTo(140f).Within(0.01f),
                "p_cap unowned (level 0) must leave the tank at the base 140");

            RigState.AcquireCap("p_cap");
            RigState.RaiseLevel("p_cap");
            RigState.RaiseLevel("p_cap");
            RigState.RaiseLevel("p_cap");
            Assert.AreEqual(4, RigState.Level("p_cap"), "test precondition: p_cap must reach level 4");

            RicochetTick.Invoke(weapon, new object[] { 0f });

            Assert.That(tank.Max, Is.EqualTo(280f).Within(0.01f),
                "p_cap at L4 must resolve the tank max to 140*(1+0.25*4) = 280, read from the live " +
                "tank after one tick");
        }
    }
}
