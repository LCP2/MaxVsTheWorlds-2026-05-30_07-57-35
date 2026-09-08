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
    /// MV-714 — UNDERTOW, World 3's primary: the lance's piercing cap (AC1), the charge/release
    /// decision (AC2), the cavitation implosion's pull/stagger (AC3), its cooldown refusal (AC4), and its
    /// lance DPS tracking the RCDA's within 10% (AC5). One test method, all five AC — the same
    /// consolidated shape <c>PulseLaserTests</c> uses for MV-708's own multi-part AC, since they share
    /// one weapon/robot setup rather than five independent fixtures (MV-465 Rule 1: at most one new test
    /// per ticket). All assertions read RESOLVED values — a robot's actual <see cref="RobotEnemy.HealthCurrent"/>,
    /// its actual position delta, <see cref="RobotEnemy.IsStunned"/>/<see cref="RobotEnemy.StunTimeRemaining"/>,
    /// a live <see cref="Undertow.DamagePerSecond"/> vs <see cref="WaterBlaster.DamagePerSecond"/> ratio —
    /// never an authored constant (MV-465 Tier 2).
    ///
    /// Fails on 43c8d6e (MV-704, the commit before this ticket): none of <see cref="Undertow"/>,
    /// <see cref="CavitationBubble"/>, <see cref="CavitationImplosion"/>, <see cref="RobotEnemy.ApplyPull"/>
    /// or <see cref="WeaponCatalog.PrimaryKind.Undertow"/> exist on that commit, so this test does not
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
        private static readonly MethodInfo UndertowRelease =
            typeof(Undertow).GetMethod("Release", BindingFlags.NonPublic | BindingFlags.Instance);
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
            // isolating the position/stun asserts from the Rusher's own HP tuning.
            HealthField.SetValue(e, 100f);
            return e;
        }

        private static bool InvokeRelease(Undertow weapon, float heldSeconds) =>
            (bool)UndertowRelease.Invoke(weapon, new object[] { heldSeconds });

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            WeaponSystemState.Reset();
            WeaponSystemState.ActivePrimary = WeaponCatalog.PrimaryKind.Undertow;
            UpgradeState.Reset();
            DevTuning.Reset();
            CavitationBubble.DestroyAllActive();

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
            CavitationBubble.DestroyAllActive();
            if (_weaponGo != null) Object.DestroyImmediate(_weaponGo);
        }

        [Test]
        public void LancePiercesTwoRobotsChargeReleaseGatesCavitationImplosionPullsAndStaggersCooldownRefusesASecondShotAndDpsTracksTheRcda_MV714()
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

            // --- AC2: releasing before the 0.9s charge threshold fires a lance tick, no projectile.
            float midHealthBeforeEarlyRelease = mid.HealthCurrent;
            bool firedProjectileEarly = InvokeRelease(undertow, 0.5f);
            Assert.IsFalse(firedProjectileEarly, "releasing at 0.5s (never charged) must not report a cavitation fire");
            Assert.IsNull(undertow.LastSpawnedCavitationForTests, "releasing at 0.5s must not spawn a cavitation projectile");
            Assert.AreEqual(0, CavitationBubble.Active.Count, "releasing at 0.5s must not spawn a cavitation projectile");
            Assert.Less(mid.HealthCurrent, midHealthBeforeEarlyRelease,
                "releasing at 0.5s must still fire an ordinary lance tick — never a wasted shot");

            // --- AC2 continued: holding to 0.9s and releasing produces a cavitation projectile.
            bool firedProjectileCharged = InvokeRelease(undertow, 0.9f);
            Assert.IsTrue(firedProjectileCharged, "holding to 0.9s and releasing must fire the cavitation shot");
            Assert.IsNotNull(undertow.LastSpawnedCavitationForTests, "a charged release must spawn a cavitation projectile");
            Assert.AreEqual(1, CavitationBubble.Active.Count, "a charged release must spawn exactly one cavitation projectile");

            // --- AC3: on implosion, robots at 1m/3.9m/5m from the impact point end 2.5m closer / 2.5m
            // closer / unmoved, and only the two moved robots are staggered. Seeded on three DIFFERENT
            // axes from the impact point (not colinear) so a robot pulled through the impact point never
            // collides with another seeded robot's own CharacterController along the way — colinear
            // seeding (all on -X) let "edge" clip "close" mid-pull and stop short, the exact MV-386-style
            // failure CharacterControllerMotion.SafeMove logs a warning for.
            Vector3 impact = new Vector3(0f, 0f, 40f);   // far from the AC1/AC2 line so overlaps never cross
            RobotEnemy close = NewEnemy("Close", impact + new Vector3(-1f, 0f, 0f));
            RobotEnemy edge = NewEnemy("Edge", impact + new Vector3(0f, 0f, -3.9f));
            RobotEnemy outside = NewEnemy("Outside", impact + new Vector3(5f, 0f, 0f));
            Vector3 closeBefore = close.transform.position;
            Vector3 edgeBefore = edge.transform.position;
            Vector3 outsideBefore = outside.transform.position;
            Physics.SyncTransforms();

            CavitationImplosion.Apply(impact, damage: 12f,
                pullRadius: Undertow.DefaultPullRadius, pullDistance: Undertow.DefaultPullDistance,
                staggerSeconds: Undertow.DefaultStaggerSeconds);

            float closeMoved = (close.transform.position - closeBefore).magnitude;
            float edgeMoved = (edge.transform.position - edgeBefore).magnitude;
            float outsideMoved = (outside.transform.position - outsideBefore).magnitude;
            Assert.AreEqual(Undertow.DefaultPullDistance, closeMoved, 0.01f,
                $"a robot 1m from the impact point must end up pulled the full 2.5m, got {closeMoved:0.000}m");
            Assert.AreEqual(Undertow.DefaultPullDistance, edgeMoved, 0.01f,
                $"a robot 3.9m from the impact point (inside the 4m pull radius) must end up pulled the full 2.5m, got {edgeMoved:0.000}m");
            Assert.AreEqual(0f, outsideMoved, 0.01f,
                $"a robot 5m from the impact point (outside the 4m pull radius) must not move at all, got {outsideMoved:0.000}m");

            Assert.IsTrue(close.IsStunned, "a pulled robot must be staggered");
            Assert.AreEqual(Undertow.DefaultStaggerSeconds, close.StunTimeRemaining, 0.05f);
            Assert.IsTrue(edge.IsStunned, "a pulled robot must be staggered");
            Assert.AreEqual(Undertow.DefaultStaggerSeconds, edge.StunTimeRemaining, 0.05f);
            Assert.IsFalse(outside.IsStunned, "a robot outside the pull radius must not be staggered");

            // --- AC4: a second cavitation shot is refused within 4s of the first (no Update() ticks the
            // cooldown down between these two Release calls).
            int activeBubblesBeforeSecondAttempt = CavitationBubble.Active.Count;
            bool firedSecondProjectile = InvokeRelease(undertow, 0.9f);
            Assert.IsFalse(firedSecondProjectile, "a second cavitation release within 4s of the first must be refused");
            Assert.AreEqual(activeBubblesBeforeSecondAttempt, CavitationBubble.Active.Count,
                "a refused cavitation release must not spawn a second bubble");

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
