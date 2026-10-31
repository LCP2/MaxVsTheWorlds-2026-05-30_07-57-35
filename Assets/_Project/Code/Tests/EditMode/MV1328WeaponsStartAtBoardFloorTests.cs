using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1328 (the one new test, per CC_AUTONOMY's testing policy): every world entry now resets
    /// PRIMARY/SECONDARY to the bottom of their own board instead of carrying a weapon's earlier
    /// investment across (Lee, 2026-10-31) — before this ticket,
    /// <see cref="WeaponSystemState.ApplyWeaponCoreMorph"/> carried a SECONDARY's full level across
    /// whenever <see cref="WorldDefinition.CarrySecondaryAcrossMorph"/> was set, so a Shoulder Rack
    /// maxed in World 2 arrived fully upgraded in the City, trivialising its robots.
    ///
    /// AC1: a Shoulder Rack maxed in World 2 arrives in the City at its own board start level
    /// (<c>s_rkt</c> == 1, not World 2's max) and still actually fires a rocket.
    /// AC3: ENERGY/MOVE levels earned before the corridor are unchanged by it.
    /// AC5: levels bought AFTER the corridor (inside the City) survive a quit + RESUME — the floor
    /// reset is a WORLD ENTRY behaviour (<see cref="WeaponSystemState.ApplyWeaponCoreMorph"/>), not
    /// something <see cref="SaveSystem.RestoreCheckpoint"/> re-applies.
    ///
    /// Fails on base commit 1cba699 (main HEAD before this ticket): <c>s_rkt</c> arrives in the City
    /// at its World-2 maxLevel (4), not the board's own start level — quoted in the fix comment.
    /// </summary>
    public sealed class MV1328WeaponsStartAtBoardFloorTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo PlayerControllerAwake =
            typeof(PlayerController).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo RobotEnemyOnEnable =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        private string _dir;
        private GameObject _playerGo;
        private GameObject _targetGo;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1328-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            WeaponSystemState.Reset();   // also resets RigState and RigBoard back to World 1
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            WeaponSystemState.Reset();
            RigBoard.ResetForTests();
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
            RobotEnemy.ResetRegistry();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_targetGo != null) Object.DestroyImmediate(_targetGo);
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void ShoulderRackMaxedInWorld2_ArrivesInCityAtItsBoardFloorAndFires_EnergyMoveUnchanged_AndBoughtCityLevelsSurviveResume()
        {
            int stormdrainIndex = WorldCatalog.IndexOf(WorldIds.Stormdrain);
            int cityIndex = WorldCatalog.IndexOf(WorldIds.City);

            // --- Arrange: World 2, Shoulder Rack maxed out, plus real ENERGY/MOVE investment.
            WeaponSystemState.ApplyWeaponCoreMorph(stormdrainIndex);
            RigState.UnlockCategory("SECONDARY");
            RigState.AcquireCap("s_rkt");
            while (RigState.Level("s_rkt") < RigBoard.MaxLevel("s_rkt"))
                Assert.IsTrue(WeaponSystemState.RaiseLevelById("s_rkt"));
            Assert.AreEqual(RigBoard.MaxLevel("s_rkt"), RigState.Level("s_rkt"),
                "test precondition: s_rkt must be maxed in World 2 before the City entry");

            RigState.UnlockCategory("ENERGY");
            RigState.AcquireCap("e_ff");
            RigState.RaiseLevel("e_ff");
            RigState.UnlockCategory("MOVE");
            RigState.AcquireCap("m_spd");

            int energyBefore = RigState.Level("e_ff");
            int moveBefore = RigState.Level("m_spd");

            // --- Act: enter the City through the corridor.
            WeaponSystemState.ApplyWeaponCoreMorph(cityIndex);

            // --- AC1: the Shoulder Rack is back at its own board's start level, not World 2's max,
            // and it still actually fires.
            Assert.AreEqual(1, RigState.Level("s_rkt"),
                "s_rkt must arrive at the City's own board start level (bumped to 1 so the rack still " +
                "fires), not carry World 2's maxed level across");
            AssertRackFires();

            // --- AC3: ENERGY/MOVE are untouched by the corridor.
            Assert.AreEqual(energyBefore, RigState.Level("e_ff"), "e_ff (ENERGY) must carry unchanged across the corridor");
            Assert.AreEqual(moveBefore, RigState.Level("m_spd"), "m_spd (MOVE) must carry unchanged across the corridor");

            // --- AC5: buy two Shoulder Rack levels in the City, quit, RESUME.
            Assert.IsTrue(WeaponSystemState.RaiseLevelById("s_rkt"));
            Assert.IsTrue(WeaponSystemState.RaiseLevelById("s_rkt"));
            Assert.AreEqual(3, RigState.Level("s_rkt"), "test precondition: two bought levels on top of the floor's 1");

            const int slot = 0;
            SaveSystem.Save(slot, new SaveSlotData { HasData = true, DisplayName = "DEXTER", WorldIndex = cityIndex });
            SaveSystem.CaptureCheckpoint(slot, areaIndex: 1, worldIndex: cityIndex);

            WeaponSystemState.Reset();   // "quit" -- wipes RigState/RigBoard back to World 1

            HomeScreen.ApplyResumeState(slot, cityIndex);

            Assert.AreEqual(3, RigState.Level("s_rkt"),
                "a RESUME inside the City must restore the levels actually bought there, not re-apply the corridor's own floor reset");
        }

        /// <summary>Same idiom <c>MV1017World3RigBoardTests</c> uses: a real <see cref="ShoulderRack"/>
        /// self-attached through the real entry point, ticked until its reload elapses, against a real
        /// <see cref="RobotEnemy"/> target — proves the rack fires a real rocket, not just that
        /// <c>s_rkt</c> reads as owned.</summary>
        private void AssertRackFires()
        {
            PickupWallet.SetPowerCellSecondary(1);

            _playerGo = new GameObject("MV1328 Player", typeof(CharacterController), typeof(PlayerController));
            var player = _playerGo.GetComponent<PlayerController>();
            PlayerControllerAwake.Invoke(player, null);
            var rack = _playerGo.GetComponent<ShoulderRack>();
            Assert.IsNotNull(rack, "test precondition: PlayerController.Awake must self-attach ShoulderRack");

            _targetGo = new GameObject("MV1328-Target") { transform = { position = new Vector3(6f, 0f, 0f) } };
            _targetGo.AddComponent<CharacterController>();
            RobotEnemy target = _targetGo.AddComponent<RobotEnemy>();
            target.Apply(EnemyArchetype.Rusher);
            RobotEnemyOnEnable.Invoke(target, null); // seeds RobotEnemy.Active -- OnEnable doesn't run here

            const float dt = 1f / 60f;
            int steps = Mathf.CeilToInt(AbilityTuning.DefaultShoulderRackBaseReloadSeconds / dt) + 1;
            for (int i = 0; i < steps; i++) rack.Tick(dt);

            Assert.That(PlayerRocket.Active.Count, Is.GreaterThan(0),
                "the Shoulder Rack must fire a real rocket at its City-floor level (1) -- it must not " +
                "sit silent just because it arrived unupgraded");
        }
    }
}
