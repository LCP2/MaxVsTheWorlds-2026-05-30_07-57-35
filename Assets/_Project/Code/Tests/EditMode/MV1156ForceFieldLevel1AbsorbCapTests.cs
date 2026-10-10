using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1156, Lee (10 Oct, World 1 playtest): "the Force Field fails too quickly... Just make it
    /// start at 100." Raises <see cref="AbilityTuning.DefaultForceFieldAbsorbCap"/> from 40 to 100 —
    /// the old 40 was 20% of Max's since-retired 200 HP (MV-677); 100 is 20% of his current 500 HP
    /// (MV-658). Asserts the RESOLVED absorb cap <see cref="PlayerAbilities"/> actually activates
    /// with, via the same private-field reflection idiom <see cref="MV586ForceFieldRamTests"/> uses,
    /// at Level 1 and at World 1's own maxLevel (read live from the loaded board, not hard-coded —
    /// same reasoning as <see cref="MV989ForceFieldExtraLevelsTests"/>). Fails on base commit c8302bd,
    /// where Level 1 resolves to 40 and World 1's maxLevel (8) resolves to 442.5 (40 + 57.5*7).
    /// </summary>
    public sealed class MV1156ForceFieldLevel1AbsorbCapTests
    {
        private GameObject _max;
        private PlayerAbilities _abilities;

        private static readonly FieldInfo AbsorbCapField =
            typeof(PlayerAbilities).GetField("_forceFieldAbsorbCap", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            DevTuning.Reset();
            PickupWallet.Reset();
            RigState.Reset();
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);

            _max = new GameObject("Max", typeof(CharacterController), typeof(PlayerController));
            _abilities = _max.GetComponent<PlayerAbilities>();
            if (_abilities == null) _abilities = _max.AddComponent<PlayerAbilities>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_max);
            RigState.Reset();
            WeaponSystemState.Reset();
            DevTuning.Reset();
            PickupWallet.Reset();
        }

        [Test]
        public void ResolvedAbsorbCapIsOneHundredAtLevelOneAndFiveOhTwoPointFiveAtWorldOnesMaxLevel()
        {
            Assert.That(RigState.AcquireCap("e_ff"), Is.True, "precondition: e_ff must be acquirable");

            _abilities.ForceActivateForceFieldForTuning();
            PlayerAbilities.RefreshForceFieldAbsorbCap();
            float level1Cap = (float)AbsorbCapField.GetValue(_abilities);
            Assert.That(level1Cap, Is.EqualTo(100f).Within(1e-3f),
                "MV-1156: a Level 1 Force Field must resolve to a 100 absorb cap");

            while (RigState.RaiseLevel("e_ff")) { }
            int maxLevel = WeaponCatalog.MaxLevel(AbilityKind.ForceField);
            Assert.That(RigState.Level("e_ff"), Is.EqualTo(maxLevel), "precondition: e_ff must be maxed");

            PlayerAbilities.RefreshForceFieldAbsorbCap();
            float maxCap = (float)AbsorbCapField.GetValue(_abilities);
            Assert.That(maxCap, Is.EqualTo(502.5f).Within(1e-3f),
                "MV-1156: World 1's maxed Force Field (level 8) must resolve to 100 + 57.5*7 = 502.5");
        }
    }
}
