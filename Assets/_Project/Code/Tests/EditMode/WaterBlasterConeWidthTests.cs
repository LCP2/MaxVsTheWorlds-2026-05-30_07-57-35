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

            RigState.AcquireCap("p_rng"); // MV-436: Range only reaches level 1 via a Morphing Module draft now
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

            RigState.AcquireCap("p_flw"); // MV-436: Flow only reaches level 1 via a Morphing Module draft now
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
            RigState.AcquireCap("p_rng");
            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Range); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range);
            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Damage); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.Damage);
            RigState.AcquireCap("p_flw");
            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.DepletionRate); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.DepletionRate);

            Assert.That(_blaster.ConeHalfAngle, Is.EqualTo(WaterBlaster.DefaultConeHalfAngle).Within(1e-5f),
                "with Spread untouched, the cone must read exactly the authored base half-angle " +
                "no matter how far every other track is pushed");
        }

        [Test]
        public void OnlySpread_WidensTheCone_MV403()
        {
            // p_spr's RIG parent is p_rng, not p_dmg — it isn't reached until Range is at level 1.
            // Schema 3 (MV-436): both Range's own 0->1 unlock and Spread's own 0->1 unlock only
            // happen via a Morphing Module draft now, never a part spend, so both are drafted
            // directly here. Spread's own formula treats level 0 and level 1 identically (both read
            // as "not widened yet") — one more Spread spend past the draft is what's needed to see
            // the cone actually move.
            RigState.AcquireCap("p_rng");
            RigState.AcquireCap("p_spr");
            float coneBefore = _blaster.ConeHalfAngle;

            WeaponSystemState.LevelUpTrack(WeaponTrackKind.Spread);

            Assert.That(_blaster.ConeHalfAngle, Is.GreaterThan(coneBefore),
                "Spread must remain the only track that actually widens the cone");
        }
    }
}
