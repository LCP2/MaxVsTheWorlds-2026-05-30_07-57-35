using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-403: locks in MV-301/MV-280's requirement that the primary's functional cone
    /// (<see cref="WaterBlaster.ConeHalfAngle"/> — the same number the hit test, VFX and aim
    /// reticle all read) is driven by the Spread track alone. Drives the real track state
    /// (<see cref="WeaponSystemState"/>) through a live component, the same path the game plays,
    /// same idiom as <see cref="WaterBlasterVisualStrengthTests"/>.
    /// </summary>
    public sealed class WaterBlasterConeWidthTests
    {
        private GameObject _go;
        private WaterBlaster _blaster;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            _go = new GameObject("wb_cone_width_test");
            _blaster = _go.AddComponent<WaterBlaster>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            WeaponSystemState.Reset();
        }

        [Test]
        public void MaxingRange_NeverWidensTheConeAtBaseSpread_MV403()
        {
            float coneBefore = _blaster.ConeHalfAngle;

            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Range); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range);

            Assert.That(_blaster.ConeHalfAngle, Is.EqualTo(coneBefore).Within(1e-5f),
                "Range at its cap must not move the cone at all while Spread stays at level 1 " +
                "(Lee's exact MV-403 repro state)");
        }

        [Test]
        public void MaxingDamage_NeverWidensTheCone_MV403()
        {
            float coneBefore = _blaster.ConeHalfAngle;

            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Damage); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.Damage);

            Assert.That(_blaster.ConeHalfAngle, Is.EqualTo(coneBefore).Within(1e-5f),
                "Damage must never touch cone width, per MV-301");
        }

        [Test]
        public void MaxingDepletionRate_NeverWidensTheCone_MV403()
        {
            float coneBefore = _blaster.ConeHalfAngle;

            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.DepletionRate); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.DepletionRate);

            Assert.That(_blaster.ConeHalfAngle, Is.EqualTo(coneBefore).Within(1e-5f),
                "Depletion Rate must never touch cone width either");
        }

        [Test]
        public void MaxingRangeDamageAndDepletion_StillLeavesTheConeAtItsBaseValue_MV403()
        {
            // The exact acceptance criterion: spend everywhere EXCEPT Spread, then confirm the
            // cone is still exactly the authored base — not just "unchanged from some prior read".
            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Range); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range);
            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Damage); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.Damage);
            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.DepletionRate); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.DepletionRate);

            Assert.That(_blaster.ConeHalfAngle, Is.EqualTo(WaterBlaster.DefaultConeHalfAngle).Within(1e-5f),
                "with Spread untouched, the cone must read exactly the authored base half-angle " +
                "no matter how far every other track is pushed");
        }

        [Test]
        public void OnlySpread_WidensTheCone_MV403()
        {
            // MV-422: p_spr's RIG parent is p_rng, not p_dmg — it isn't reached until Range has been
            // spent at least once. p_rng itself IS reached from run start (its own parent, p_dmg, is
            // already at L1), so this one Range spend is the minimum needed before Spread can move at
            // all. Spread's own formula treats level 0 and level 1 identically (both read as "not
            // widened yet", same as the old model's level-1 starting point) — two Spread spends are
            // needed to see the cone actually move.
            WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range);
            WeaponSystemState.LevelUpTrack(WeaponTrackKind.Spread);
            float coneBefore = _blaster.ConeHalfAngle;

            WeaponSystemState.LevelUpTrack(WeaponTrackKind.Spread);

            Assert.That(_blaster.ConeHalfAngle, Is.GreaterThan(coneBefore),
                "Spread must remain the only track that actually widens the cone");
        }
    }
}
