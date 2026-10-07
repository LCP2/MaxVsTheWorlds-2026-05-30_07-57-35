using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Upgrades;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-714 — UNDERTOW, World 3's primary: the lance's piercing cap (AC1) and its DPS ratio against
    /// the RCDA (AC5). MV-1034 removed the charge/cavitation shot entirely (Lee: it didn't work as a
    /// design), so the AC2 (charge/release)/AC3 (implosion pull+stagger)/AC4 (cooldown refusal) cases
    /// this test used to carry are culled along with the code they covered — see
    /// <c>CavitationBubble</c>/<c>CavitationImplosion</c>'s removal. All remaining assertions read
    /// RESOLVED values — a robot's actual <see cref="RobotEnemy.HealthCurrent"/>, a live
    /// <see cref="Undertow.DamagePerSecond"/> vs <see cref="WaterBlaster.DamagePerSecond"/> ratio — never
    /// an authored constant (MV-465 Tier 2).
    ///
    /// MV-1106 (Lee: "the primary weapon much too weak") gave UNDERTOW its own base damage constant,
    /// double the RCDA's rather than tracking it within 10% — AC5's own ratio assertion below now
    /// expects 2.0, not 1.0.
    ///
    /// Fails on 43c8d6e (MV-704, the commit before MV-714): none of <see cref="Undertow"/> or
    /// <see cref="WeaponCatalog.PrimaryKind.Undertow"/> exist on that commit, so this test does not
    /// compile there — the same class of pre-fix evidence <c>PulseLaserTests</c>' own doc comment uses.
    ///
    /// Updated for MV-1070, which removed the single-shot <c>FireLanceTick</c> this test used to invoke
    /// directly: a robot can now only take damage once the tip's own seeking spring has physically
    /// latched onto it (<see cref="Undertow.IsLatched"/>), so AC1 now ticks the REAL <c>Tick</c> path
    /// until latched before asserting the pierce. MV-1070 also added a distance tie-break to candidate
    /// selection specifically for this geometry (three robots dead-ahead in a line all read the same
    /// angle off aim) — the nearest must be the one sought/latched/pierced-from first.
    /// </summary>
    public sealed class UndertowTests
    {
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo HealthField =
            typeof(RobotEnemy).GetField("_health", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo RobotEnemyOnEnable =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo UndertowAwake =
            typeof(Undertow).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo UndertowTick =
            typeof(Undertow).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo WaterBlasterAwake =
            typeof(WaterBlaster).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _weaponGo;

        private static RobotEnemy NewEnemy(string name, Vector3 position)
        {
            var go = new GameObject(name);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            go.transform.position = position;
            e.Apply(EnemyArchetype.Rusher);
            RobotEnemyOnEnable.Invoke(e, null);
            // Set AFTER OnEnable (its ResetState re-stamps _health to the archetype's own base HP,
            // which would silently undo an earlier override) — 100 survives every hit in this test,
            // isolating the position asserts from the Rusher's own HP tuning.
            HealthField.SetValue(e, 100f);
            return e;
        }

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            WeaponSystemState.Reset();
            WeaponSystemState.ActivePrimary = WeaponCatalog.PrimaryKind.Undertow;
            UpgradeState.Reset();
            DevTuning.Reset();
            DevMode.Reset();

            _weaponGo = new GameObject("Undertow");
            _weaponGo.transform.position = Vector3.zero;
            _weaponGo.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            UpgradeState.Reset();
            DevTuning.Reset();
            DevMode.Reset();
            RobotEnemy.ResetRegistry();
            if (_weaponGo != null) Object.DestroyImmediate(_weaponGo);
        }

        [Test]
        public void LancePiercesTwoRobotsAndDpsTracksTheRcda_MV714()
        {
            DevMode.Enabled = true;
            DevMode.InfiniteEnergy = true;

            Undertow undertow = _weaponGo.AddComponent<Undertow>();
            UndertowAwake.Invoke(undertow, null); // Awake doesn't run for AddComponent outside Play mode
            undertow.SetFiring(true); // no aimSource on this standalone probe -- SetFiring drives it directly

            // --- AC1: a line of three robots dead-ahead; the tip latches onto the nearest (MV-1070's own
            // distance tie-break for equal-angle candidates), then pierces to the second-closest.
            RobotEnemy near = NewEnemy("Near", Vector3.forward * 2f);
            RobotEnemy mid = NewEnemy("Mid", Vector3.forward * 4f);
            RobotEnemy far = NewEnemy("Far", Vector3.forward * 6f);
            Physics.SyncTransforms();

            const int maxAcquireTicks = 10; // 1s budget -- generous over the ~0.45s settle spec
            int t = 0;
            for (; t < maxAcquireTicks && !undertow.IsLatched; t++)
                UndertowTick.Invoke(undertow, new object[] { 0.1f });
            Assert.IsTrue(undertow.IsLatched, $"the tip never latched onto the nearest dead-ahead robot within {maxAcquireTicks * 0.1f:0.0}s");
            Assert.AreSame(near.gameObject, undertow.LatchedTransform.gameObject,
                "of three equally-dead-ahead robots the tip must latch onto the CLOSEST one");

            float nearBefore = near.HealthCurrent, midBefore = mid.HealthCurrent, farBefore = far.HealthCurrent;
            UndertowTick.Invoke(undertow, new object[] { 0.1f });

            float tickDamage = undertow.EffectiveDamagePerTick;
            Assert.AreEqual(nearBefore - tickDamage, near.HealthCurrent, 0.01f,
                "the closest (latched) robot in the line must take one lance tick of damage");
            Assert.AreEqual(midBefore - tickDamage, mid.HealthCurrent, 0.01f,
                "the second-closest robot in the line must ALSO take one lance tick of damage (pierces two)");
            Assert.AreEqual(farBefore, far.HealthCurrent, 0.01f,
                "the third robot in the line must take NO damage — the lance pierces at most two");

            // --- AC5: UNDERTOW's lance DPS at track level 0 (fresh state) is within 10% of the RCDA's.
            var wbGo = new GameObject("wb_dps_compare");
            try
            {
                var blaster = wbGo.AddComponent<WaterBlaster>();
                WaterBlasterAwake.Invoke(blaster, null);

                Assert.Greater(blaster.DamagePerSecond, 0f, "test precondition: the RCDA comparison has zero DPS");
                float ratio = undertow.DamagePerSecond / blaster.DamagePerSecond;
                // MV-1106 (Lee: "the primary weapon much too weak") gave UNDERTOW its own base damage
                // constant, double the RCDA's rather than tracking it — AC5 above is superseded.
                Assert.That(ratio, Is.InRange(1.9f, 2.1f),
                    $"UNDERTOW's lance DPS ({undertow.DamagePerSecond:0.00}) should be double the RCDA's own DPS " +
                    $"({blaster.DamagePerSecond:0.00}) at base level (MV-1106) — ratio was {ratio:0.000}");
            }
            finally
            {
                Object.DestroyImmediate(wbGo);
            }
        }
    }
}
