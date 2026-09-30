using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1036 — regression guard for MV-1012's ungated release path. MV-1012 self-attached
    /// <see cref="Undertow"/> to Max in every world but only gated the continuous stream on
    /// <see cref="WeaponSystemState.ActivePrimary"/>; the charge/release path it also carried at the
    /// time (<c>Release</c>/<c>FireCavitation</c>) ran unconditionally on button-up, so holding fire in
    /// World 1 or World 2 and releasing fired World 3's blue ball / a hidden lance hit regardless.
    /// MV-1034 (already merged, <c>a07ff52</c>) removed that charge/release shot entirely and folded
    /// everything — tank, stream, damage — into one <see cref="Undertow"/> Tick gated end-to-end on
    /// <c>ActivePrimary</c>, which happens to already close this exact hole; this test is the missing
    /// coverage for that gate rather than a further code change.
    ///
    /// Fails on <c>baa509c</c> (MV-1033, the commit this ticket's observation cites): at that commit
    /// <c>Undertow.Update</c> tracked <c>_holdTimer</c>/<c>_charged</c> unconditionally and called
    /// <c>Release(_holdTimer)</c> -&gt; <c>FireCavitation</c>/<c>FireLanceTick</c> on button-up with no
    /// <c>ActivePrimary</c> check at all, so a robot 5m ahead of a World-0/World-1 loadout took lance
    /// damage it should never have taken. All assertions read RESOLVED values — a robot's own
    /// <see cref="RobotEnemy.HealthCurrent"/> and the live <see cref="Undertow.IsEmitting"/>/
    /// <see cref="PulseLaser.IsEmitting"/> flags computed by each weapon's real gate — never an authored
    /// constant (MV-465 Tier 2).
    /// </summary>
    public sealed class MV1036UndertowGatedOutsideWorld3Tests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly FieldInfo HealthField =
            typeof(RobotEnemy).GetField("_health", NonPublicInstance);
        private static readonly MethodInfo RobotEnemyOnEnable =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        private static readonly MethodInfo PlayerControllerAwake =
            typeof(PlayerController).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowAwake =
            typeof(Undertow).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowTick =
            typeof(Undertow).GetMethod("Tick", NonPublicInstance);
        private static readonly MethodInfo WaterBlasterAwake =
            typeof(WaterBlaster).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo WaterBlasterUpdate =
            typeof(WaterBlaster).GetMethod("Update", NonPublicInstance);
        private static readonly MethodInfo PulseLaserAwake =
            typeof(PulseLaser).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo PulseLaserUpdate =
            typeof(PulseLaser).GetMethod("Update", NonPublicInstance);

        private static RobotEnemy NewEnemy(Vector3 position)
        {
            var go = new GameObject("MV1036 Robot");
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            go.transform.position = position;
            e.Apply(EnemyArchetype.Rusher);
            RobotEnemyOnEnable.Invoke(e, null);
            // Set AFTER OnEnable (its ResetState re-stamps _health to the archetype's own base HP) --
            // 100 survives any single tick from any of these weapons, isolating the exact-damage asserts
            // below from the Rusher's own HP tuning.
            HealthField.SetValue(e, 100f);
            return e;
        }

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            WeaponSystemState.Reset();
            DevMode.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            WeaponSystemState.Reset();
            DevMode.Reset();
        }

        [Test]
        public void UndertowDealsNoDamageOutsideWorld3ButFiresNormallyInsideIt()
        {
            // ================= World 0 (RCDA) =================
            {
                WeaponSystemState.ApplyWorldLoadout(0);

                var playerGo = new GameObject("MV1036 Player W0", typeof(CharacterController), typeof(PlayerController));
                var player = playerGo.GetComponent<PlayerController>();
                PlayerControllerAwake.Invoke(player, null);

                var undertow = playerGo.GetComponent<Undertow>();
                Assert.IsNotNull(undertow, "Undertow must still self-attach in World 0 (MV-1012's own shape)");
                UndertowAwake.Invoke(undertow, null);

                var wbGo = new GameObject("MV1036 WaterBlaster W0");
                var blaster = wbGo.AddComponent<WaterBlaster>();
                WaterBlasterAwake.Invoke(blaster, null);

                RobotEnemy robot = NewEnemy(Vector3.forward * 5f);
                Physics.SyncTransforms();

                DevMode.Enabled = true;
                DevMode.AutoFire = true;

                // "Hold fire 1.2s" -- one big explicit-dt tick still fires at most one lance tick
                // (Undertow.Tick has no catch-up loop), so this is the worst case for the gate: the
                // longest single hold an EditMode test can drive without a real per-frame loop.
                UndertowTick.Invoke(undertow, new object[] { 1.2f });
                WaterBlasterUpdate.Invoke(blaster, null);

                Assert.IsFalse(undertow.IsEmitting, "Undertow must not emit while World 0's RCDA is the active primary");
                Assert.IsTrue(blaster.IsEmitting, "the RCDA itself must still fire normally in its own world");
                Assert.AreEqual(100f - blaster.EffectiveDamagePerTick, robot.HealthCurrent, 0.01f,
                    "the robot's health must reflect exactly one RCDA tick -- any further drop would mean " +
                    "Undertow's lance also landed a hit it must not deal outside World 3");

                Object.DestroyImmediate(playerGo);
                Object.DestroyImmediate(wbGo);
                Object.DestroyImmediate(robot.gameObject);
                RobotEnemy.ResetRegistry();
            }

            // ================= World 1 (LPPE) =================
            {
                WeaponSystemState.ApplyWorldLoadout(1);

                var playerGo = new GameObject("MV1036 Player W1", typeof(CharacterController), typeof(PlayerController));
                var player = playerGo.GetComponent<PlayerController>();
                PlayerControllerAwake.Invoke(player, null);

                var undertow = playerGo.GetComponent<Undertow>();
                Assert.IsNotNull(undertow, "Undertow must still self-attach in World 1");
                UndertowAwake.Invoke(undertow, null);

                var pulseLaser = playerGo.GetComponent<PulseLaser>();
                Assert.IsNotNull(pulseLaser, "PulseLaser must still self-attach in World 1");
                PulseLaserAwake.Invoke(pulseLaser, null);

                RobotEnemy robot = NewEnemy(Vector3.forward * 5f);
                Physics.SyncTransforms();

                DevMode.Enabled = true;
                DevMode.AutoFire = true;

                UndertowTick.Invoke(undertow, new object[] { 1.2f });
                PulseLaserUpdate.Invoke(pulseLaser, null);

                Assert.IsFalse(undertow.IsEmitting, "Undertow must not emit while World 1's LPPE is the active primary");
                Assert.IsTrue(pulseLaser.IsEmitting, "the LPPE itself must still fire normally in its own world");
                Assert.IsNotNull(pulseLaser.LastSpawnedPulseForTests, "the LPPE must have actually spawned a pulse");
                Assert.AreEqual(100f, robot.HealthCurrent, 0.01f,
                    "the LPPE's pulse travels rather than hitting instantly, so the robot must still read its " +
                    "full health this frame -- any drop here could only come from Undertow's lance, which must " +
                    "not fire outside World 3");

                Object.DestroyImmediate(playerGo);
                Object.DestroyImmediate(robot.gameObject);
                if (pulseLaser.LastSpawnedPulseForTests != null)
                    Object.DestroyImmediate(pulseLaser.LastSpawnedPulseForTests.gameObject);
                RobotEnemy.ResetRegistry();
            }

            // ================= World 2 (UNDERTOW) =================
            {
                WeaponSystemState.ApplyWorldLoadout(2);

                var playerGo = new GameObject("MV1036 Player W2", typeof(CharacterController), typeof(PlayerController));
                var player = playerGo.GetComponent<PlayerController>();
                PlayerControllerAwake.Invoke(player, null);

                var undertow = playerGo.GetComponent<Undertow>();
                Assert.IsNotNull(undertow, "Undertow must self-attach in World 2");
                UndertowAwake.Invoke(undertow, null);

                RobotEnemy robot = NewEnemy(Vector3.forward * 5f);
                Physics.SyncTransforms();

                DevMode.Enabled = true;
                DevMode.AutoFire = true;

                UndertowTick.Invoke(undertow, new object[] { 1.2f });

                Assert.IsTrue(undertow.IsEmitting, "Undertow must emit while World 2's UNDERTOW is the active primary");
                Assert.AreEqual(100f - undertow.EffectiveDamagePerTick, robot.HealthCurrent, 0.01f,
                    "Undertow's own lance must still deal exactly one tick of damage in its own world");

                Object.DestroyImmediate(playerGo);
                Object.DestroyImmediate(robot.gameObject);
                RobotEnemy.ResetRegistry();
            }
        }
    }
}
