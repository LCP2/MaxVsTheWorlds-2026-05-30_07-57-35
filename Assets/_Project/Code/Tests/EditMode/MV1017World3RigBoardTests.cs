using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1017 (the one new test): World 3's own RIG board. Before this ticket,
    /// <see cref="RigBoardLibrary.ForWorld"/> mapped every world &gt;= 1 onto
    /// <c>rig_board.world2.json</c>, so World 3 inherited World 2's LPPE-only tracks (p_rof/p_frk/p_cap)
    /// that <see cref="Undertow"/> never reads — paid-for levels wired to nothing — and the World 2 -&gt;
    /// World 3 Weapon Core morph unconditionally re-armed <see cref="RigState.ActivateSecondaryMystery"/>,
    /// re-locking SECONDARY (the Shoulder Rack) for the whole of World 3 even though the player already
    /// earned it in World 2.
    ///
    /// AC1: every PRIMARY/SECONDARY node on <c>rig_board.world3.json</c> moves a real runtime read when
    /// levelled up — <see cref="Undertow"/>'s own resolved Range/EffectiveDamagePerTick/ConeHalfAngle for
    /// PRIMARY, the Shoulder Rack's own resolved damage/salvo-count/reload/splash/cluster reads for
    /// SECONDARY — built through the real entry point, <see cref="PlayerController.Awake"/> (invoked via
    /// reflection, the same idiom every other self-attach test in this project uses, since AddComponent
    /// doesn't run Unity lifecycle methods outside Play mode).
    ///
    /// AC2: <see cref="WeaponSystemState.ApplyWeaponCoreMorph"/>(2) (the World 2 -&gt; World 3 morph) must
    /// not re-lock SECONDARY, and the Shoulder Rack must actually fire a real rocket off a real
    /// <see cref="ShoulderRack.Tick"/> loop afterwards.
    ///
    /// Fails on base commit ebcb87e (before this ticket): <c>RigBoardLibrary.ForWorld(2)</c> resolves to
    /// <c>rig_board.world2.json</c>, which still carries p_rof/p_frk/p_cap (none of which UNDERTOW reads,
    /// so the PRIMARY half of AC1 fails on those three ids) and doesn't carry p_spr at all; separately,
    /// <c>ApplyWeaponCoreMorph</c> unconditionally calls <c>RigState.ActivateSecondaryMystery</c> on every
    /// morph, so AC2's <c>SecondaryLocked</c> assertion fails after <c>ApplyWeaponCoreMorph(2)</c>.
    /// </summary>
    public sealed class MV1017World3RigBoardTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo PlayerControllerAwake =
            typeof(PlayerController).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowAwake =
            typeof(Undertow).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowUpdate =
            typeof(Undertow).GetMethod("Update", NonPublicInstance);

        private GameObject _playerGo;
        private GameObject _targetGo;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            DevMode.Reset();
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            DevMode.Reset();
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
            RigBoard.ResetForTests();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_targetGo != null) Object.DestroyImmediate(_targetGo);
        }

        [Test]
        public void EveryWorld3PrimaryAndSecondaryNodeMovesARuntimeRead_AndTheWorld3MorphKeepsSecondaryUnlocked()
        {
            // --- the real entry point: PlayerController.Awake self-attaches both weapons under test.
            _playerGo = new GameObject("MV1017 Player", typeof(CharacterController), typeof(PlayerController));
            var player = _playerGo.GetComponent<PlayerController>();
            PlayerControllerAwake.Invoke(player, null);

            var undertow = _playerGo.GetComponent<Undertow>();
            Assert.IsNotNull(undertow, "test precondition: PlayerController.Awake must self-attach Undertow");
            UndertowAwake.Invoke(undertow, null); // Undertow's own Awake doesn't run for AddComponent outside Play mode

            var rack = _playerGo.GetComponent<ShoulderRack>();
            Assert.IsNotNull(rack, "test precondition: PlayerController.Awake must self-attach ShoulderRack");

            // --- AC1: no dead PRIMARY/SECONDARY node on World 3's own board.
            RigBoard.UseWorld(2); // rig_board.world3.json
            string[] ids = RigBoard.AllIds
                .Where(id => RigBoard.Category(id) == "PRIMARY" || RigBoard.Category(id) == "SECONDARY")
                .ToArray();
            Assert.That(ids, Does.Contain("p_spr"), "test precondition: World 3's board must carry p_spr");
            Assert.That(ids, Does.Not.Contain("p_rof"), "test precondition: World 3's board must not carry World 2's p_rof");

            foreach (string id in ids)
                AssertNodeMovesARuntimeRead(id, undertow, rack);

            // --- AC2: the World 2 -> World 3 morph itself must not re-lock SECONDARY.
            AssertWorld3MorphKeepsSecondaryUnlockedAndTheRackFires(rack);
        }

        // ------------------------------------------------------------ AC1

        private static void AssertNodeMovesARuntimeRead(string id, Undertow undertow, ShoulderRack rack)
        {
            SeedWorld3WithSecondaryCarriedOver(exceptSRkt: id == "s_rkt");
            EnsureAncestorsReached(id); // e.g. s_clu needs s_rld >= 1 -- s_rkt alone isn't enough

            if (RigBoard.Category(id) == "PRIMARY")
            {
                var before = (undertow.Range, undertow.EffectiveDamagePerTick, undertow.ConeHalfAngle);
                Assert.IsTrue(RaiseToMaxLevel(id), $"test precondition: {id} must be raisable to its own max level");
                UndertowUpdate.Invoke(undertow, null);
                var after = (undertow.Range, undertow.EffectiveDamagePerTick, undertow.ConeHalfAngle);
                Assert.AreNotEqual(before, after,
                    $"{id}: raising it must change something UNDERTOW itself reads " +
                    "(Range/EffectiveDamagePerTick/ConeHalfAngle) -- a dead PRIMARY node");
            }
            else
            {
                var before = ShoulderRackReads(rack);
                Assert.IsTrue(RaiseToMaxLevel(id), $"test precondition: {id} must be raisable to its own max level");
                var after = ShoulderRackReads(rack);
                Assert.AreNotEqual(before, after,
                    $"{id}: raising it must change something the Shoulder Rack itself reads " +
                    "(IsBought/rocket damage/salvo count/reload seconds/splash radius/cluster) -- a dead SECONDARY node");
            }
        }

        /// <summary>The same resolved numbers <see cref="ShoulderRack.QueueSalvo"/> and
        /// <see cref="ShoulderRack.ReloadFraction01"/> actually compute from live RIG levels — read
        /// through the same production <see cref="AbilityTuning"/> formulas rather than re-implemented,
        /// so this is "the secondary actually reads it at runtime", not a re-derivation of its logic.</summary>
        private static (bool bought, float damage, int salvoCount, float reload, float splash, bool cluster) ShoulderRackReads(ShoulderRack rack) => (
            rack.IsBought,
            AbilityTuning.ShoulderRackRocketDamage(
                WeaponSystemState.ShoulderRackTrackLevel(ShoulderRackTrackKind.RocketDamage),
                AbilityTuning.DefaultShoulderRackBaseDamage, AbilityTuning.DefaultShoulderRackDamagePerLevel),
            AbilityTuning.ShoulderRackSalvoCount(
                WeaponSystemState.ShoulderRackTrackLevel(ShoulderRackTrackKind.Salvo),
                AbilityTuning.DefaultShoulderRackMaxSalvoCount),
            AbilityTuning.ShoulderRackReloadSeconds(
                WeaponSystemState.ShoulderRackTrackLevel(ShoulderRackTrackKind.Reload),
                AbilityTuning.DefaultShoulderRackBaseReloadSeconds, AbilityTuning.DefaultShoulderRackReloadFloorSeconds,
                WeaponCatalog.MaxLevel(ShoulderRackTrackKind.Reload)),
            AbilityTuning.ShoulderRackSplashRadius(
                WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.SplashArea),
                AbilityTuning.DefaultShoulderRackBaseSplashRadius, AbilityTuning.DefaultShoulderRackMaxSplashRadius,
                WeaponCatalog.MaxLevel(WaterBalloonTrackKind.SplashArea)),
            WeaponSystemState.ShoulderRackTrackLevel(ShoulderRackTrackKind.Cluster) >= 1);

        /// <summary>Grants every ancestor of <paramref name="id"/> (not <paramref name="id"/> itself) at
        /// level 1, root-first, so a multi-level chain like <c>s_clu</c> (child of <c>s_rld</c>, in turn
        /// a child of root <c>s_rkt</c>) is actually REACHED before the test tries to raise it — the
        /// shared seed only grants the SECONDARY root, same as a real Rack Module pickup would.</summary>
        private static void EnsureAncestorsReached(string id)
        {
            string parent = RigBoard.Parent(id);
            if (string.IsNullOrEmpty(parent)) return;
            EnsureAncestorsReached(parent);
            if (RigState.Level(parent) == 0) WeaponSystemState.AcquireById(parent);
        }

        /// <summary>Unlocks a node (a Morphing Module draft) if unowned, then spends cells up to its own
        /// <see cref="RigBoard.MaxLevel"/> — never just one level up. Every continuous track this ticket
        /// touches (p_rng/p_spr/p_dmg, s_rkt/s_spl/s_sal/s_rld) reads level 1 as its OWN unmodified
        /// baseline by design (<see cref="WeaponCatalog.EffectiveRange"/> et al. all use
        /// <c>Mathf.Max(1, level) - 1</c> — MV-263/MV-291's own "reached but not yet levelled" shape),
        /// so asserting a change at exactly level 0 -&gt; 1 would false-fail on every one of them; raising
        /// all the way to the cap is what actually proves the node has a live consumer, a strict superset
        /// of the 0 -&gt; 1 check for the boolean-style nodes (s_clu, and s_rkt's own <c>IsBought</c>) where
        /// the very first level already flips something.</summary>
        private static bool RaiseToMaxLevel(string id)
        {
            if (RigState.Level(id) == 0 && !WeaponSystemState.AcquireById(id)) return false;
            while (RigState.Level(id) < RigBoard.MaxLevel(id))
                if (!WeaponSystemState.RaiseLevelById(id)) return false;
            return true;
        }

        /// <summary>Real progression up to World 3: the World 1 -&gt; World 2 morph, then the World 2
        /// Rack Module pickup (<see cref="MaxWorlds.Pickups.PickupDirector.Collect"/>'s own
        /// <c>RigState.UnlockCategory("SECONDARY")</c> + <c>RigState.AcquireCap("s_rkt")</c> pair) —
        /// unless <paramref name="exceptSRkt"/>, which leaves <c>s_rkt</c> itself unowned so it can still
        /// be raised 0 -&gt; 1 like every other id under test — then the World 2 -&gt; World 3 morph this
        /// ticket fixes.</summary>
        private static void SeedWorld3WithSecondaryCarriedOver(bool exceptSRkt)
        {
            WeaponSystemState.Reset();
            WeaponSystemState.ApplyWeaponCoreMorph(1);
            RigState.UnlockCategory("SECONDARY");
            if (!exceptSRkt) RigState.AcquireCap("s_rkt");
            WeaponSystemState.ApplyWeaponCoreMorph(2);
        }

        // ------------------------------------------------------------ AC2

        private void AssertWorld3MorphKeepsSecondaryUnlockedAndTheRackFires(ShoulderRack rack)
        {
            SeedWorld3WithSecondaryCarriedOver(exceptSRkt: false);

            Assert.IsTrue(RigState.IsCategoryUnlocked("SECONDARY"),
                "SECONDARY must stay unlocked across the World 2 -> World 3 morph, not re-lock");
            Assert.IsFalse(RigState.SecondaryLocked,
                "SECONDARY must not read as mystery-locked again after the World 3 morph");
            Assert.GreaterOrEqual(RigState.Level("s_rkt"), 1,
                "s_rkt's level earned in World 2 must carry into World 3, not reset to 0");

            PickupWallet.SetPowerCellSecondary(1);
            _targetGo = new GameObject("MV1017-Target") { transform = { position = new Vector3(6f, 0f, 0f) } };
            _targetGo.AddComponent<CharacterController>();
            RobotEnemy target = _targetGo.AddComponent<RobotEnemy>();
            target.Apply(EnemyArchetype.Rusher);

            const float dt = 1f / 60f;
            int steps = Mathf.CeilToInt(AbilityTuning.DefaultShoulderRackBaseReloadSeconds / dt) + 1;
            for (int i = 0; i < steps; i++) rack.Tick(dt);

            Assert.That(PlayerRocket.Active.Count, Is.GreaterThan(0),
                "the Shoulder Rack must fire a real rocket once the World 3 morph has run, with " +
                "SECONDARY already unlocked from World 2 -- it must not sit mystery-locked/silent");
        }
    }
}
