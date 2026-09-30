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
    /// MV-714 — UNDERTOW, World 3's primary: the lance's piercing cap (AC1) and its DPS tracking the
    /// RCDA's within 10% (AC5). MV-1034 removed the charge/cavitation shot entirely (Lee: it didn't work
    /// as a design), so the AC2 (charge/release)/AC3 (implosion pull+stagger)/AC4 (cooldown refusal)
    /// cases this test used to carry are culled along with the code they covered — see
    /// <c>CavitationBubble</c>/<c>CavitationImplosion</c>'s removal. All remaining assertions read
    /// RESOLVED values — a robot's actual <see cref="RobotEnemy.HealthCurrent"/>, a live
    /// <see cref="Undertow.DamagePerSecond"/> vs <see cref="WaterBlaster.DamagePerSecond"/> ratio — never
    /// an authored constant (MV-465 Tier 2).
    ///
    /// Fails on 43c8d6e (MV-704, the commit before MV-714): none of <see cref="Undertow"/> or
    /// <see cref="WeaponCatalog.PrimaryKind.Undertow"/> exist on that commit, so this test does not
    /// compile there — the same class of pre-fix evidence <c>PulseLaserTests</c>' own doc comment uses.
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
        private static readonly MethodInfo UndertowFireLanceTick =
            typeof(Undertow).GetMethod("FireLanceTick", BindingFlags.NonPublic | BindingFlags.Instance);
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
            RobotEnemy.ResetRegistry();
            if (_weaponGo != null) Object.DestroyImmediate(_weaponGo);
        }

        [Test]
        public void LancePiercesTwoRobotsAndDpsTracksTheRcda_MV714()
        {
            Undertow undertow = _weaponGo.AddComponent<Undertow>();
            UndertowAwake.Invoke(undertow, null); // Awake doesn't run for AddComponent outside Play mode

            // --- AC1: a lance tick fired along a line of three robots damages exactly the first two.
            RobotEnemy near = NewEnemy("Near", Vector3.forward * 2f);
            RobotEnemy mid = NewEnemy("Mid", Vector3.forward * 4f);
            RobotEnemy far = NewEnemy("Far", Vector3.forward * 6f);
            Physics.SyncTransforms();

            UndertowFireLanceTick.Invoke(undertow, null);

            float tickDamage = undertow.EffectiveDamagePerTick;
            Assert.AreEqual(100f - tickDamage, near.HealthCurrent, 0.01f,
                "the closest robot in the line must take one lance tick of damage");
            Assert.AreEqual(100f - tickDamage, mid.HealthCurrent, 0.01f,
                "the second-closest robot in the line must ALSO take one lance tick of damage (pierces two)");
            Assert.AreEqual(100f, far.HealthCurrent, 0.01f,
                "the third robot in the line must take NO damage — the lance pierces at most two");

            // --- AC5: UNDERTOW's lance DPS at track level 0 (fresh state) is within 10% of the RCDA's.
            var wbGo = new GameObject("wb_dps_compare");
            try
            {
                var blaster = wbGo.AddComponent<WaterBlaster>();
                WaterBlasterAwake.Invoke(blaster, null);

                Assert.Greater(blaster.DamagePerSecond, 0f, "test precondition: the RCDA comparison has zero DPS");
                float ratio = undertow.DamagePerSecond / blaster.DamagePerSecond;
                Assert.That(ratio, Is.InRange(0.9f, 1.1f),
                    $"UNDERTOW's lance DPS ({undertow.DamagePerSecond:0.00}) should track the RCDA's own DPS " +
                    $"({blaster.DamagePerSecond:0.00}) within +/-10% at base level — ratio was {ratio:0.000}");
            }
            finally
            {
                Object.DestroyImmediate(wbGo);
            }
        }
    }
}
