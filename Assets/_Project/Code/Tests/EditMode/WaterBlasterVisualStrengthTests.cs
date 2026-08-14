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
            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Spread); i++)
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.Spread);

            Assert.That(_blaster.VisualStrength, Is.EqualTo(1f).Within(1e-5f),
                "a maxed Spread track must read the full, un-scaled-down visual");
        }

        [Test]
        public void LevelingSpreadRaisesVisualStrength_WithoutMovingTheFunctionalConeAtLevelOne()
        {
            float before = _blaster.VisualStrength;
            float coneBefore = _blaster.ConeHalfAngle;

            WeaponSystemState.LevelUpTrack(WeaponTrackKind.Spread);

            Assert.That(_blaster.VisualStrength, Is.GreaterThan(before),
                "spending a level on Spread must visibly thicken the weapon's presentation");
            Assert.That(_blaster.ConeHalfAngle, Is.GreaterThan(coneBefore),
                "Spread still widens the real hit-cone too — MV-379 only decouples the VISUAL size/density dials, not the track itself");
        }

        [Test]
        public void OtherTracks_DoNotMoveVisualStrength()
        {
            float before = _blaster.VisualStrength;

            WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range);
            WeaponSystemState.LevelUpTrack(WeaponTrackKind.Damage);
            WeaponSystemState.LevelUpTrack(WeaponTrackKind.DepletionRate);

            Assert.That(_blaster.VisualStrength, Is.EqualTo(before).Within(1e-5f),
                "only the Spread track should move the stream's visual-only presentation");
        }
    }
}
