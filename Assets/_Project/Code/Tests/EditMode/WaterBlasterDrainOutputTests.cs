using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-368: end-to-end through <see cref="WaterBlaster.EnergyPerTick"/> — the Depletion Rate track
    /// was a dead upgrade because drain never moved with the Range/Spread tracks. These drive the real
    /// track state (<see cref="WeaponSystemState"/>) through a live component, the same path the game
    /// plays, rather than just the pure formula in <see cref="WeaponCatalogTests"/>.
    /// </summary>
    public sealed class WaterBlasterDrainOutputTests
    {
        private GameObject _go;
        private WaterBlaster _blaster;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            DevTuning.Reset();
            _go = new GameObject("wb_drain_test");
            _blaster = _go.AddComponent<WaterBlaster>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            WeaponSystemState.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void FreshWeapon_DrainsAtExactlyTheAuthoredBaseRate_MV368AC2()
        {
            float expectedPerTick = BlasterTuning.EnergyPerSecond * _blaster.FireInterval;
            Assert.That(_blaster.EnergyPerTick, Is.EqualTo(expectedPerTick).Within(1e-4f),
                "level 1 across every track must reproduce today's drain unchanged");
        }

        [Test]
        public void LevelingRangeAndSpread_IncreasesTheDrain_MV368AC1()
        {
            float baseline = _blaster.EnergyPerTick;

            // Schema 3 (MV-436): Range's and Spread's own 0->1 unlocks only happen via a Morphing
            // Module draft now, never a part spend.
            RigState.AcquireCap("p_rng");
            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Range); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range);
            RigState.AcquireCap("p_spr");
            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Spread); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.Spread);

            Assert.That(_blaster.EnergyPerTick, Is.GreaterThan(baseline),
                "a maxed Range+Spread weapon must drain noticeably faster than the un-upgraded baseline");
        }

        [Test]
        public void LevelingEndurance_OffsetsTheIncreasedDrainFromOutput_MV368AC3()
        {
            RigState.AcquireCap("p_rng");
            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Range); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range);
            RigState.AcquireCap("p_spr");
            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Spread); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.Spread);

            float noDepletionSpend = _blaster.EnergyPerTick;

            RigState.AcquireCap("p_flw");
            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Endurance); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.Endurance);

            float maxedDepletionSpend = _blaster.EnergyPerTick;

            Assert.Less(maxedDepletionSpend, noDepletionSpend,
                "spending on Depletion Rate must buy back sustain even against a maxed-output weapon");
            Assert.Greater(maxedDepletionSpend, 0f,
                "a maxed weapon must still be costly but usable, never free (MV-368 AC4)");
        }

        [Test]
        public void DamageTrackAlone_DoesNotAffectDrain()
        {
            // Damage isn't "water volume" — only Range/Spread (the actual reach and cone) should move
            // the drain. This pins the ticket's own note: scale off output, not off every track spent.
            float baseline = _blaster.EnergyPerTick;

            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Damage); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.Damage);

            Assert.That(_blaster.EnergyPerTick, Is.EqualTo(baseline).Within(1e-4f),
                "the Damage track must not change how fast the tank drains");
        }
    }
}
