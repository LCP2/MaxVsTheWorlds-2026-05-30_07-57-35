using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1285 AC1 (Testing policy Rule 1 — one new test): robot A rolls Linked (<c>EnemyArchetype.Of
    /// (Rusher).WithElite(Linked)</c>) 2 m from an ordinary Rusher B, both in area 1. One
    /// <see cref="RobotEnemy.TickLinked"/> call on A pairs them (B is promoted — elite, full health,
    /// same as A). A 20-damage hit on A leaves A AND B each 10 below their own max health (point 3's
    /// 50/50 split). Killing A (<see cref="RobotEnemy.KillWithoutDrops"/>, so the kill itself never
    /// re-enters the split) starts B's revenge countdown; advancing B 3.1 s via a second
    /// <see cref="RobotEnemy.TickLinked"/> call leaves B at full health again (point 4).
    ///
    /// <c>OnEnable</c> is force-invoked via reflection for each robot (same idiom
    /// <c>MV1284ShieldProjectorDomeTests</c> already uses) — Awake/OnEnable are not reliably invoked
    /// for a freshly-activated GameObject outside Play mode, so <see cref="RobotEnemy"/>'s own
    /// <c>_active</c> registry (what point 2's pairing search walks) would otherwise never see either
    /// robot.
    ///
    /// Fails to compile on 1cf3fab (the commit before this ticket): <c>RobotEnemy.TickLinked</c> and
    /// <c>RobotEnemy.LinkedPartner</c> do not exist there at all — same "brand new member, fails to
    /// compile" fail-first proof <c>MV1283MoltenEliteTests</c> already uses for its own brand-new kind.
    /// </summary>
    public sealed class MV1285LinkedEliteTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        [SetUp]
        public void SetUp() => RobotEnemy.ResetRegistry();

        [TearDown]
        public void TearDown() => RobotEnemy.ResetRegistry();

        [Test]
        public void LinkedElite_SplitsDamage5050_RevengeHealsSurvivorAfter3s_MV1285()
        {
            var root = new GameObject("MV1285 Probe Root");
            try
            {
                GameObject aGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                aGo.transform.SetParent(root.transform, false);
                aGo.AddComponent<CharacterController>();
                RobotEnemy a = aGo.AddComponent<RobotEnemy>();
                a.Apply(EnemyArchetype.Of(EnemyKind.Rusher).WithElite(EliteAffix.Linked));
                a.SetAreaIndex(1);
                aGo.transform.position = Vector3.zero;
                RobotOnEnableMethod.Invoke(a, null);

                GameObject bGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                bGo.transform.SetParent(root.transform, false);
                bGo.AddComponent<CharacterController>();
                RobotEnemy b = bGo.AddComponent<RobotEnemy>();
                b.Apply(EnemyArchetype.Of(EnemyKind.Rusher)); // plain — A's roll pairs it, per point 2
                b.SetAreaIndex(1);
                bGo.transform.position = new Vector3(2f, 0f, 0f); // well within the 8 m search radius
                RobotOnEnableMethod.Invoke(b, null);

                Assert.AreEqual(2, RobotEnemy.ActiveCount,
                    "test setup: both robots must be registered in RobotEnemy's own active registry");

                // --- pairing: one tick resolves point 2's search; both are now full-health Linked elites. ---
                a.TickLinked(0f);

                Assert.AreSame(b, a.LinkedPartner, "A must pair with the only eligible neighbour in range");
                Assert.AreSame(a, b.LinkedPartner, "the pairing must be mutual");
                Assert.IsTrue(b.IsElite && b.Affix == EliteAffix.Linked,
                    "the promoted partner must read as a Linked elite, same as a freshly-rolled one");
                Assert.AreEqual(a.MaxHealth, b.HealthCurrent, 0.001f,
                    "a freshly-promoted partner must be at full (elite) health");

                // --- point 3: a 20-damage hit on A splits 10/10. ---
                float beforeA = a.HealthCurrent;
                float beforeB = b.HealthCurrent;
                a.TakeDamage(new DamageInfo(20f, aGo.transform.position, Vector3.back, Team.Player));

                Assert.AreEqual(10f, beforeA - a.HealthCurrent, 0.001f,
                    "a 20-damage hit on a Linked elite must leave IT 10 below its own max");
                Assert.AreEqual(10f, beforeB - b.HealthCurrent, 0.001f,
                    "a 20-damage hit on one Linked elite must leave its partner 10 below max too");

                // --- point 4: killing A starts B's countdown; 3.1s later B is healed to full and un-Linked. ---
                a.KillWithoutDrops();
                b.TickLinked(3.1f);

                Assert.AreEqual(b.MaxHealth, b.HealthCurrent, 0.001f,
                    "the survivor must heal to full max health once the 3s revenge countdown expires");
                Assert.AreEqual(EliteAffix.None, b.Affix,
                    "the survivor must no longer be Linked once the revenge heal resolves");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
