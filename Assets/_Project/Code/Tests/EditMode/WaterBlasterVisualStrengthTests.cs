using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-379: end-to-end through <see cref="WaterBlaster.VisualStrength"/> — drives the real Spread
    /// track state (<see cref="WeaponSystemState"/>) through a live component, the same path the game
    /// plays, rather than just the pure formula in <see cref="WeaponCatalogTests"/>.
    /// </summary>
    public sealed class WaterBlasterVisualStrengthTests
    {
        private GameObject _go;
        private WaterBlaster _blaster;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            _go = new GameObject("wb_visual_strength_test");
            _blaster = _go.AddComponent<WaterBlaster>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            WeaponSystemState.Reset();
        }

        [Test]
        public void FreshWeapon_ReadsTheWeakestVisualStrength()
        {
            Assert.That(_blaster.VisualStrength, Is.EqualTo(0f).Within(1e-5f),
                "a fresh, un-upgraded weapon (Spread at its starting level) must read the weakest visual");
        }

        [Test]
        public void MaxedSpreadTrack_ReadsFullVisualStrength()
        {
            // p_spr's RIG parent is p_rng — unreached (and so un-levelable) until Range is at level
            // 1. Schema 3 (MV-436): both Range's and Spread's own 0->1 unlock only happen via a
            // Morphing Module draft now, never a part spend.
            RigState.AcquireCap("p_rng");
            RigState.AcquireCap("p_spr");
            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Spread); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.Spread);

            Assert.That(_blaster.VisualStrength, Is.EqualTo(1f).Within(1e-5f),
                "a maxed Spread track must read the full, un-scaled-down visual");
        }

        [Test]
        public void LevelingSpreadRaisesVisualStrength_WithoutMovingTheFunctionalConeAtLevelOne()
        {
            // Level 0 and level 1 both read as Spread's un-widened starting point (the formula's own
            // Mathf.Max(1, level) clamp) — so level 1 is where "without moving the cone at level one"
            // is actually measured from; level 2 is the first level that visibly moves either dial.
            // Schema 3 (MV-436): both Range's and Spread's own 0->1 unlock only happen via a Morphing
            // Module draft now, never a part spend.
            RigState.AcquireCap("p_rng"); // unlocks Spread's own reached-ness
            RigState.AcquireCap("p_spr"); // Spread to L1 — the starting point
            float before = _blaster.VisualStrength;
            float coneBefore = _blaster.ConeHalfAngle;

            WeaponSystemState.LevelUpTrack(WeaponTrackKind.Spread); // Spread to L2

            Assert.That(_blaster.VisualStrength, Is.GreaterThan(before),
                "spending a level on Spread must visibly thicken the weapon's presentation");
            Assert.That(_blaster.ConeHalfAngle, Is.GreaterThan(coneBefore),
                "Spread still widens the real hit-cone too — MV-379 only decouples the VISUAL size/density dials, not the track itself");
        }

        [Test]
        public void OtherTracks_DoNotMoveVisualStrength()
        {
            float before = _blaster.VisualStrength;

            RigState.AcquireCap("p_rng");
            WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range);
            WeaponSystemState.LevelUpTrack(WeaponTrackKind.Damage);
            RigState.AcquireCap("p_flw");
            WeaponSystemState.LevelUpTrack(WeaponTrackKind.Endurance);

            Assert.That(_blaster.VisualStrength, Is.EqualTo(before).Within(1e-5f),
                "only the Spread track should move the stream's visual-only presentation");
        }
    }
}
