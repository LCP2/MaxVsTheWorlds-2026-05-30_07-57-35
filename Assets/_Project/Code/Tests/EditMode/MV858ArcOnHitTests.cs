using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-858 — Lee: "Fork -- firstly it should be called 'Arc' and secondly it works inconsistently
    /// (almost never)." The old <c>PulseLaser.RegisterKill</c> only released a second pulse when the
    /// FIRST pulse's hit killed its target or left it under 15% health (<c>SeekerPulse</c>'s own
    /// <c>NearDeathForkThreshold</c>) -- against World 2's health pools that was rare, so the mechanic
    /// read as doing nothing. This ticket replaces it entirely: ARC now fires on EVERY pulse hit that
    /// lands on a robot (kill or not), arcing an instant 50%-damage hit to the nearest OTHER alive robot
    /// within 8m of the hit point. This is the one new EditMode test the testing policy allows this
    /// ticket, asserting entirely from RESOLVED state (MV-465 Tier 2) -- the second robot's own measured
    /// health loss, never an authored constant.
    ///
    /// Fails on dd85276 (base commit before this fix): a pulse that hits robot A without killing it (and
    /// without leaving it under 15% health) releases no fork/arc at all under the old kill/near-death
    /// gate, so robot B takes zero damage in BOTH the 5m and 9m cases below -- the first assertion
    /// (B at 5m must lose exactly 50% of the pulse's damage) fails outright.
    /// </summary>
    public sealed class MV858ArcOnHitTests
    {
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo HealthField =
            typeof(RobotEnemy).GetField("_health", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo RobotEnemyOnEnable =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo PulseLaserAwake =
            typeof(PulseLaser).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo PulseLaserFireTick =
            typeof(PulseLaser).GetMethod("FireTick", BindingFlags.NonPublic | BindingFlags.Instance);

        private static RobotEnemy NewEnemy(string name, Vector3 position)
        {
            var go = new GameObject(name);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            go.transform.position = position;
            e.Apply(EnemyArchetype.Rusher);
            HealthField.SetValue(e, 1000f); // comfortably survives a hit either way -- never near-death
            RobotEnemyOnEnable.Invoke(e, null);
            return e;
        }

        private static void AdvanceUntilSpent(SeekerPulse pulse, float step, float cap)
        {
            float elapsed = 0f;
            while (!pulse.IsSpent && elapsed < cap) { pulse.Tick(step); elapsed += step; }
        }

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            WeaponSystemState.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            RobotEnemy.ResetRegistry();
            RigBoard.ResetForTests();
        }

        private static PulseLaser NewLaser()
        {
            var go = new GameObject("PulseLaser_MV858");
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            PulseLaser laser = go.AddComponent<PulseLaser>();
            PulseLaserAwake.Invoke(laser, null); // Awake doesn't run for AddComponent outside Play mode
            return laser;
        }

        [Test]
        public void ArcOnHitDamagesTheNearestOtherRobotWithin8mForHalfDamage_AndNotBeyondIt()
        {
            WeaponSystemState.ApplyWeaponCoreMorph(1); // World 2 board, ActivePrimary -> Lppe
            WeaponSystemState.AcquireById("p_rng");    // p_frk's own parent
            WeaponSystemState.AcquireById("p_frk");    // ARC at level 1
            Assert.AreEqual(1, RigState.Level("p_frk"), "test precondition: p_frk (ARC) must reach level 1");

            const float step = 1f / 60f;

            // --- Case 1: B 5m beyond A, dead ahead -- well within ARC's 8m range. ---
            PulseLaser laser1 = NewLaser();
            RobotEnemy a1 = NewEnemy("A1", new Vector3(0f, 0f, 8f));
            RobotEnemy b1 = NewEnemy("B1", new Vector3(0f, 0f, 13f)); // 5m beyond a1, same bearing
            Physics.SyncTransforms();
            float dmg = laser1.EffectiveDamagePerPulse;

            try
            {
                PulseLaserFireTick.Invoke(laser1, null);
                SeekerPulse pulse1 = laser1.LastSpawnedPulseForTests;
                Assert.AreSame(a1, pulse1.Target, "test precondition: the pulse must lock onto a1 (the nearest robot)");

                float b1HealthBefore = b1.HealthCurrent;
                AdvanceUntilSpent(pulse1, step, cap: 1f);
                Assert.IsTrue(a1.IsAlive, "test precondition: a1 must survive its own hit");

                float b1Loss = b1HealthBefore - b1.HealthCurrent;
                Assert.AreEqual(dmg * 0.5f, b1Loss, 0.01f,
                    $"B 5m from the hit point must take exactly 50% of the pulse's damage " +
                    $"({dmg * 0.5f:0.00} expected, lost {b1Loss:0.00}) -- ARC must fire on ANY hit, not " +
                    "just a kill or near-death hit (the old FORK gate this ticket removes)");
            }
            finally
            {
                Object.DestroyImmediate(a1.gameObject);
                Object.DestroyImmediate(b1.gameObject);
                Object.DestroyImmediate(laser1.gameObject);
            }

            RobotEnemy.ResetRegistry();

            // --- Case 2: B 9m beyond A, dead ahead -- just outside ARC's 8m range. ---
            PulseLaser laser2 = NewLaser();
            RobotEnemy a2 = NewEnemy("A2", new Vector3(0f, 0f, 8f));
            RobotEnemy b2 = NewEnemy("B2", new Vector3(0f, 0f, 17f)); // 9m beyond a2, same bearing
            Physics.SyncTransforms();

            try
            {
                PulseLaserFireTick.Invoke(laser2, null);
                SeekerPulse pulse2 = laser2.LastSpawnedPulseForTests;
                Assert.AreSame(a2, pulse2.Target, "test precondition: the pulse must lock onto a2 (the nearest robot)");

                float b2HealthBefore = b2.HealthCurrent;
                AdvanceUntilSpent(pulse2, step, cap: 1f);
                Assert.IsTrue(a2.IsAlive, "test precondition: a2 must survive its own hit");

                Assert.AreEqual(0f, b2HealthBefore - b2.HealthCurrent, 0.001f,
                    "B 9m from the hit point (beyond ARC's 8m range) must take no damage at all");
            }
            finally
            {
                Object.DestroyImmediate(a2.gameObject);
                Object.DestroyImmediate(b2.gameObject);
                Object.DestroyImmediate(laser2.gameObject);
            }
        }
    }
}
