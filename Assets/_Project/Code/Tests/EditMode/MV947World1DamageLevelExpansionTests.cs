using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-947, Lee (playing World 1): killing robots felt much harder after MV-832's Sentinel LOS/
    /// dormant-target change. Lee's decision: give World 1 more upgrade headroom rather than revert
    /// MV-832. World 1's board only (<c>rig_board.json</c>): <c>p_dmg</c>'s <c>maxLevel</c> rises
    /// 4 -&gt; 7, <c>u_dmg</c>'s rises 5 -&gt; 8, each continuing its existing per-level step (World 2's
    /// <c>rig_board.world2.json</c> untouched). Reads both the board's own cap and each track's
    /// resolved value through the same live gameplay path (<see cref="WaterBlaster.EffectiveDamagePerTick"/>,
    /// <see cref="MaxWorlds.Arena.Sentinel.Update"/>) actually uses, not a hardcoded level literal — same
    /// shape as MV-840's own World 2 expansion test.
    /// </summary>
    public sealed class MV947World1DamageLevelExpansionTests
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
        public void World1RaisesPrimaryAndSentinelDamageCapsAndTheirResolvedValuesFollow()
        {
            Assert.That(RigBoard.ActiveWorldIndex, Is.EqualTo(0), "fixture: World 1's board must be active by default");
            Assert.That(RigBoard.MaxLevel("p_dmg"), Is.EqualTo(7), "World 1's PRIMARY Damage track must cap at 7 levels");
            Assert.That(RigBoard.MaxLevel("u_dmg"), Is.EqualTo(8), "World 1's Sentinel Damage track must cap at 8 levels");

            // --- Primary (RCDA) damage: p_dmg is owned from run start at level 1 (RigBoard.StartLevel);
            // raise it to its real (board-read) cap, then read the resolved per-tick damage through the
            // exact same WaterBlaster property live gameplay/FireTick uses.
            var blasterGo = new GameObject("MV-947 test WaterBlaster");
            try
            {
                var blaster = blasterGo.AddComponent<WaterBlaster>();

                while (WeaponSystemState.LevelUpTrack(WeaponTrackKind.Damage)) { }
                Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Damage), Is.EqualTo(7));
                Assert.That(WeaponSystemState.LevelUpTrack(WeaponTrackKind.Damage), Is.False, "must not level past the new cap of 7");

                float damageAtL7 = blaster.EffectiveDamagePerTick;
                float damageAtL4 = WeaponCatalog.EffectiveDamagePerTick(
                    WaterBlaster.DefaultDamagePerTick, 4, WeaponCatalog.DefaultRcdaDamagePerLevel);

                Assert.That(damageAtL4, Is.EqualTo(WaterBlaster.DefaultDamagePerTick * 1.6f).Within(1e-3f),
                    "today's L4 resolved damage (1.6x base) must be unchanged by this ticket");
                Assert.That(damageAtL7, Is.GreaterThan(damageAtL4),
                    "L7's resolved damage must be strictly greater than today's L4 value");
                Assert.That(damageAtL7, Is.EqualTo(WaterBlaster.DefaultDamagePerTick * 2.2f).Within(1e-3f),
                    "L7's resolved damage must continue the existing 20%/level step: 1 + 0.2*6 = 2.2x base");
            }
            finally
            {
                Object.DestroyImmediate(blasterGo);
            }

            // --- Sentinel damage: raise u_dmg to its real (board-read) cap, then read the resolved
            // per-shot damage through the exact same AbilityTuning call Sentinel.Update makes.
            foreach (string categoryId in RigBoard.AllCategoryIds) RigState.UnlockCategory(categoryId);
            Assert.That(WeaponSystemState.Acquire(AbilityKind.Sentinels), Is.True);
            Assert.That(RigState.AcquireCap("u_dmg"), Is.True);

            float damageAtL5 = AbilityTuning.SentinelDamagePerShot(
                5, AbilityTuning.DefaultSentinelBaseDamage, AbilityTuning.DefaultSentinelDamagePerLevel);
            Assert.That(damageAtL5, Is.EqualTo(7f).Within(1e-4f), "today's L5 resolved per-shot must stay 2 + 1*5 = 7");

            while (RigState.RaiseLevel("u_dmg")) { }
            Assert.That(RigState.Level("u_dmg"), Is.EqualTo(8));
            Assert.That(RigState.RaiseLevel("u_dmg"), Is.False, "must not level past the new cap of 8");

            int damageLevel = RigState.Level("u_dmg");
            float damageAtL8 = AbilityTuning.SentinelDamagePerShot(
                damageLevel, AbilityTuning.DefaultSentinelBaseDamage, AbilityTuning.DefaultSentinelDamagePerLevel);
            Assert.That(damageAtL8, Is.EqualTo(10f).Within(1e-4f), "L8's resolved per-shot must be 2 + 1*8 = 10");
            Assert.That(damageAtL8, Is.GreaterThan(damageAtL5), "L8's resolved per-shot must be strictly greater than today's L5 value");

            // --- AC3: World 2's own board (rig_board.world2.json) must be untouched by this ticket's
            // World 1-only change — snapshotted without disturbing the World 1 board this test otherwise runs against.
            RigBoard.UseWorld(1); // rig_board.world2.json
            Assert.That(RigBoard.MaxLevel("p_dmg"), Is.EqualTo(8), "World 2's POWER/p_dmg cap (MV-844) must stay 8");
            Assert.That(RigBoard.MaxLevel("u_dmg"), Is.EqualTo(10), "World 2's Sentinel Damage cap (MV-840) must stay 10");
        }
    }
}
