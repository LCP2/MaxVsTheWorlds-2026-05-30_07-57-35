using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1156, Lee 10 Oct 2026 playing World 1: "the Force Field fails too quickly. Just make it
    /// start at 100. I think 57 per level addition is fine." <see cref="AbilityTuning.DefaultForceFieldAbsorbCap"/>
    /// was set to 40 in MV-677 as 20% of Max's then-200 HP; MV-658 later raised Max's HP to 500, so 40
    /// is now only 8%. Asserts the RESOLVED live absorb cap <see cref="PlayerAbilities"/> actually runs
    /// with (via reflection on the private <c>_forceFieldAbsorbCap</c> field, same idiom
    /// MV660ShieldStrengthKnobTests uses for a private field/method), through the exact
    /// <see cref="PlayerAbilities.RefreshForceFieldAbsorbCap"/> path the Settings panel drives — not the
    /// authored constant directly (Rule 2). Fails on base commit 863019d, where Level 1 resolves to 40
    /// and World 1's max level (8) resolves to 40 + 57.5*7 = 442.5.
    /// </summary>
    public sealed class MV1156ForceFieldLevel1AbsorbCapTests
    {
        private GameObject _max;
        private PlayerAbilities _abilities;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            DevTuning.Reset();
            RigBoard.ResetForTests();
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);

            _max = new GameObject("Max", typeof(CharacterController), typeof(PlayerController));
            _abilities = _max.GetComponent<PlayerAbilities>();
            if (_abilities == null) _abilities = _max.AddComponent<PlayerAbilities>();

            WeaponSystemState.Acquire(AbilityKind.ForceField);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_max);
            WeaponSystemState.Reset();
            DevTuning.Reset();
            RigBoard.ResetForTests();
        }

        private static float LiveAbsorbCap(PlayerAbilities abilities)
        {
            var field = typeof(PlayerAbilities).GetField("_forceFieldAbsorbCap",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return (float)field.GetValue(abilities);
        }

        [Test]
        public void LevelOneAbsorbCapIsOneHundredAfterRefresh()
        {
            _abilities.ForceActivateForceFieldForTuning(); // level 1, acquired above
            PlayerAbilities.RefreshForceFieldAbsorbCap();

            Assert.That(LiveAbsorbCap(_abilities), Is.EqualTo(100f).Within(1e-3f),
                "MV-1156: a Level 1 Force Field must resolve to an absorb cap of 100 (20% of Max's 500 HP)");
        }

        [Test]
        public void WorldOneMaxLevelAbsorbCapMatchesTheMV1156Retune()
        {
            int maxLevel = WeaponCatalog.MaxLevel(AbilityKind.ForceField); // read live, not hard-coded
            for (int i = 1; i < maxLevel; i++) WeaponSystemState.LevelUpAbility(AbilityKind.ForceField);
            Assert.That(WeaponSystemState.AbilityLevel(AbilityKind.ForceField), Is.EqualTo(maxLevel),
                "precondition: Force Field must actually be levelled to World 1's own max level");

            _abilities.ForceActivateForceFieldForTuning();
            PlayerAbilities.RefreshForceFieldAbsorbCap();

            Assert.That(LiveAbsorbCap(_abilities), Is.EqualTo(502.5f).Within(1e-3f),
                $"MV-1156: World 1's max level ({maxLevel}) Force Field must resolve to 100 + 57.5*{maxLevel - 1} = 502.5");
        }
    }
}
