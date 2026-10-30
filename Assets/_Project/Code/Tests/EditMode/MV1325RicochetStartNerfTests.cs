using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1325 (the one new test, Rule 1): World 3 (City) play feedback was that RICOCHET is too
    /// strong from the very first second (floor: 12 damage/disc, 2 bounces), so the new support
    /// robots never get a chance to matter. This asserts the two RESOLVED values (MV-465 Tier 2)
    /// the nerf actually touches — AC1 at the City's own start levels, AC2 at full power/ricochets —
    /// never an authored constant read back at itself.
    ///
    /// Fails on base commit f5ba228 (main HEAD before this ticket): <c>Ricochet.EffectiveDamagePerDisc</c>
    /// reads 12 at the City's start levels, not 8 (<c>PowerDamageByLevel[0]</c> is still 12f there), and
    /// <c>BouncesPerDisc</c> reads 2, not 1 (<c>rig_board.city.json</c>'s own <c>p_ric</c> still starts
    /// at <c>startLevel: 2</c>).
    /// </summary>
    public sealed class MV1325RicochetStartNerfTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo RicochetAwake = typeof(Ricochet).GetMethod("Awake", NonPublicInstance);

        private string _dir;
        private GameObject _weaponGo;

        [SetUp]
        public void SetUp()
        {
            _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ytgame-mv1325-tests");
            if (System.IO.Directory.Exists(_dir)) System.IO.Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            WeaponSystemState.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            WeaponSystemState.Reset();
            RigBoard.ResetForTests();
            if (_weaponGo != null) Object.DestroyImmediate(_weaponGo);
            if (System.IO.Directory.Exists(_dir)) System.IO.Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void FloorIsNerfedAndFullPowerIsUnchanged()
        {
            // AC1: PrevExit dev start into the City (World 1 -> World 2's own exit rig, one world
            // later) arrives at the City's own board start levels, exactly as CityDevStartTests
            // already proves for p_ric/p_dmg themselves.
            int cityIndex = WorldCatalog.IndexOf(WorldIds.City);
            HomeScreen.StartDevWorld(0, cityIndex);

            Assert.AreEqual(1, RigState.Level("p_ric"), "test precondition: p_ric must now start at 1");
            Assert.AreEqual(1, RigState.Level("p_dmg"), "test precondition: p_dmg must start at 1");

            Ricochet weapon = NewWeapon();
            Assert.AreEqual(8f, weapon.EffectiveDamagePerDisc, 1e-4f,
                "AC1: a fresh disc at the floor must deal 8 damage, not the old 12");
            Assert.AreEqual(1, weapon.BouncesPerDisc, "AC1: a fresh disc at the floor must bounce once, not twice");

            // AC2: full power/ricochets still top out exactly where they did before this ticket --
            // only the floor moved.
            RigState.RestoreSnapshot(
                new System.Collections.Generic.Dictionary<string, int> { { "p_dmg", 8 }, { "p_ric", 6 } },
                new[] { "PRIMARY" });

            Assert.AreEqual(40f, weapon.EffectiveDamagePerDisc, 1e-4f,
                "AC2: POWER level 8 must still top out at 40 damage per disc");
            Assert.AreEqual(6, weapon.BouncesPerDisc, "AC2: RICOCHETS level 6 must still bounce 6 times");
        }

        private Ricochet NewWeapon()
        {
            _weaponGo = new GameObject("MV1325 Ricochet");
            Ricochet weapon = _weaponGo.AddComponent<Ricochet>();
            RicochetAwake.Invoke(weapon, null); // Awake doesn't run for AddComponent outside Play mode
            return weapon;
        }
    }
}
