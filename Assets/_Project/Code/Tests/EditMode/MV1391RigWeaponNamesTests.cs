using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1391: THE RIG's PRIMARY and SECONDARY column headers are two lines — the slot, then the
    /// equipped weapon's WeaponCatalog name — and SECONDARY reads "?" while locked. Resolved text of
    /// the built Text components (Tier 2), compared against the catalog, not against literals.
    /// </summary>
    public sealed class MV1391RigWeaponNamesTests
    {
        private GameObject _go;
        private WeaponsScreen _screen;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
            _go = new GameObject("WeaponsScreen");
            _screen = _go.AddComponent<WeaponsScreen>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;   // Open() pauses; never leak it into ProjectSettings/TimeManager.asset
            RigBoard.UseWorld(0);
            RigBoardLayout.ResetForTests();
        }

        [Test]
        public void WeaponColumnHeadersNameTheEquippedWeapons_MV1391()
        {
            WeaponSystemState.ActivePrimary = WeaponCatalog.PrimaryKind.Ricochet;
            WeaponSystemState.SecondaryKind = SecondaryKind.ShoulderRack;
            _screen.Open();
            _screen.RebuildBoard();

            Assert.That(_screen.CategorySlotLabel("PRIMARY")?.text, Is.EqualTo("PRIMARY"));
            Assert.That(_screen.CategoryNameLabel("PRIMARY").text, Is.EqualTo(WeaponCatalog.DisplayName(WeaponCatalog.PrimaryKind.Ricochet)));
            Assert.That(_screen.CategorySlotLabel("SECONDARY")?.text, Is.EqualTo("SECONDARY"));
            Assert.That(_screen.CategoryNameLabel("SECONDARY").text, Is.EqualTo(WeaponCatalog.DisplayName(SecondaryKind.ShoulderRack)));

            WeaponSystemState.ActivePrimary = WeaponCatalog.PrimaryKind.Undertow;
            _screen.RebuildBoard();
            Assert.That(_screen.CategoryNameLabel("PRIMARY").text, Is.EqualTo(WeaponCatalog.DisplayName(WeaponCatalog.PrimaryKind.Undertow)));

            // AC2: locked secondary reads "?".
            RigState.ActivateSecondaryMystery();
            _screen.RebuildBoard();
            Assert.IsTrue(RigState.SecondaryLocked, "precondition: secondary locked");
            Assert.That(_screen.CategoryNameLabel("SECONDARY").text, Is.EqualTo("?"));
        }
    }
}
