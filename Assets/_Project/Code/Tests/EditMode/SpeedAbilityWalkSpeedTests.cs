using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The Speed ability (WV-231, spec §6) is a passive multiplier on
    /// <see cref="PlayerController.WalkSpeed"/> — the single number the move loop actually uses, same
    /// pattern as the Acceleration engine's <c>UpgradeState.MoveSpeedMultiplier</c>.
    /// </summary>
    public sealed class SpeedAbilityWalkSpeedTests
    {
        private GameObject _go;
        private PlayerController _player;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            DevTuning.Reset();
            _go = new GameObject("Max", typeof(CharacterController), typeof(PlayerController));
            _player = _go.GetComponent<PlayerController>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            WeaponSystemState.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void UnacquiredSpeedDoesNotChangeWalkSpeed()
        {
            Assert.That(_player.WalkSpeed, Is.EqualTo(_player.AuthoredMoveSpeed).Within(1e-4f));
        }

        [Test]
        public void EachSpeedLevelRaisesWalkSpeed()
        {
            float unowned = _player.WalkSpeed;

            WeaponSystemState.Acquire(AbilityKind.Speed);
            float l1 = _player.WalkSpeed;
            Assert.That(l1, Is.GreaterThan(unowned), "L1 Speed must raise the walk speed");

            for (int i = 1; i < WeaponCatalog.MaxLevel(AbilityKind.Speed); i++)
                WeaponSystemState.LevelUpAbility(AbilityKind.Speed);
            float maxed = _player.WalkSpeed;
            Assert.That(maxed, Is.GreaterThan(l1), "a maxed Speed must raise it further than L1");
        }
    }
}
