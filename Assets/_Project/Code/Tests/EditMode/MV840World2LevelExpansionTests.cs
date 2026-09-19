using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-840, Lee: "add 2 more levels to Force field, 5 more levels to Sentinel damage." World 2's
    /// board only (<c>rig_board.world2.json</c>): <c>e_ff</c>'s maxLevel rises 5 -&gt; 7, <c>u_dmg</c>'s
    /// rises 5 -&gt; 10, continuing each track's existing per-level step (World 1's <c>rig_board.json</c>
    /// untouched). Reads both the board's own cap and each track's resolved value through the same
    /// RigState/PlayerAbilities/AbilityTuning path live gameplay (<see cref="PlayerAbilities.TryActivateForceField"/>,
    /// <see cref="MaxWorlds.Arena.Sentinel.Update"/>) actually uses, not a hardcoded level literal —
    /// proven to fail on the pre-fix commit (10fce02), where <c>e_ff</c>/<c>u_dmg</c> still cap at 5 on
    /// World 2's board.
    /// </summary>
    public sealed class MV840World2LevelExpansionTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            PickupWallet.Reset();
            DevTuning.Reset();
            RigBoard.ResetForTests();
        }

        [Test]
        public void World2RaisesForceFieldAndSentinelDamageCapsAndTheirResolvedValuesFollow()
        {
            RigBoard.UseWorld(1); // rig_board.world2.json
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);

            Assert.That(RigBoard.MaxLevel("e_ff"), Is.EqualTo(7), "World 2's Force Field track must cap at 7 levels");
            Assert.That(RigBoard.MaxLevel("u_dmg"), Is.EqualTo(10), "World 2's Sentinel Damage track must cap at 10 levels");

            // --- Force Field: raise e_ff to its real (board-read) cap, then activate through the same
            // PlayerAbilities path live gameplay uses, reading the resolved absorb cap back out via
            // AbsorbForceFieldDamage (there's no direct cap getter) rather than recomputing it by hand.
            Assert.That(RigState.AcquireCap("e_ff"), Is.True);
            while (RigState.RaiseLevel("e_ff")) { }
            Assert.That(RigState.Level("e_ff"), Is.EqualTo(7));

            var maxGo = new GameObject("MV-840 test Max", typeof(CharacterController), typeof(PlayerController));
            try
            {
                var abilities = maxGo.AddComponent<PlayerAbilities>();
                Assert.That(abilities.TryActivateForceField(), Is.True);

                // PopForceField() calls Destroy() on the bubble, fine at runtime but logs an
                // edit-mode-only error here (same idiom MV523ForceFieldFreeActivationTests uses).
                LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called from edit mode"));
                float leaked = abilities.AbsorbForceFieldDamage(9999f); // exceeds the cap, popping the bubble
                float resolvedAbsorbCap = 9999f - leaked;
                Assert.That(resolvedAbsorbCap, Is.EqualTo(385f).Within(1e-3f),
                    "Force Field L7's resolved absorb cap must be 40 + 57.5*6 = 385");
            }
            finally
            {
                Object.DestroyImmediate(maxGo);
            }

            // --- Sentinel damage: raise u_dmg to its real (board-read) cap, then read the resolved
            // per-shot damage through the exact same AbilityTuning call Sentinel.Update makes.
            Assert.That(WeaponSystemState.Acquire(AbilityKind.Sentinels), Is.True);
            Assert.That(RigState.AcquireCap("u_dmg"), Is.True);
            while (RigState.RaiseLevel("u_dmg")) { }
            Assert.That(RigState.Level("u_dmg"), Is.EqualTo(10));

            int damageLevel = RigState.Level("u_dmg");
            float damage = AbilityTuning.SentinelDamagePerShot(
                damageLevel, AbilityTuning.DefaultSentinelBaseDamage, AbilityTuning.DefaultSentinelDamagePerLevel);
            Assert.That(damage, Is.EqualTo(12f).Within(1e-4f), "Sentinel Damage L10's resolved per-shot must be 2 + 1*10 = 12");
        }
    }
}
