using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1476 AC1-AC2: every weapon pushes robots by the shared WeaponPush rule. Asserts RESOLVED
    /// displacement of a real <see cref="RobotEnemy"/> after simulated 0.005 s ticks (MV-465 Tier 2).
    /// Fails on 0ae0830 (base): <c>WeaponPush</c> does not exist there, so this does not compile.
    /// </summary>
    public sealed class MV1476AllWeaponsPushTests
    {
        private const float Dt = 0.005f;   // fine step: the robot integrates knockback per frame, coarse steps over-count
        private static readonly Vector3 Origin = new Vector3(0f, 500f, 0f);
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo CcField = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly MethodInfo TickKnockback =
            typeof(RobotEnemy).GetMethod("ApplyKnockback", NonPublicInstance, null, new[] { typeof(float) }, null);

        private static RobotEnemy NewRobot(EnemyArchetype a)
        {
            var go = new GameObject("Push Probe");
            go.transform.position = Origin;
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(a);
            Physics.SyncTransforms();
            return e;
        }

        private static void SetPower(int level)
        {
            RigState.RestoreSnapshot(new Dictionary<string, int> { { "p_dmg", level } }, new string[0]);
        }

        /// <summary>Hits at hitsPerSecond for 1 s, ticking the robot's own knockback each 0.02 s; returns
        /// metres moved on the ground plane.</summary>
        private static float DisplacementOverOneSecond(RobotEnemy r, float hitsPerSecond, System.Action hit)
        {
            Vector3 start = r.transform.position;
            float interval = 1f / hitsPerSecond, timer = 0f;
            for (int i = 0; i < 200; i++)
            {
                timer -= Dt;
                if (timer <= 0f) { hit(); timer += interval; }
                TickKnockback.Invoke(r, new object[] { Dt });
                Physics.SyncTransforms();
            }
            // let the last impulse finish decaying
            for (int i = 0; i < 200; i++) { TickKnockback.Invoke(r, new object[] { Dt }); Physics.SyncTransforms(); }
            Vector3 d = r.transform.position - start; d.y = 0f;
            return d.magnitude;
        }

        private static float Measure(EnemyArchetype a, float hitsPerSecond, int power)
        {
            SetPower(power);
            RobotEnemy r = NewRobot(a);
            try
            {
                return DisplacementOverOneSecond(r, hitsPerSecond,
                    () => WeaponPush.Apply(r, Vector3.forward, hitsPerSecond));
            }
            finally { Object.DestroyImmediate(r.gameObject); RobotEnemy.ResetRegistry(); }
        }

        [Test]
        public void EveryWeaponPushesLikeTheHoseAndScalesWithPower()
        {
            try
            {
                // Reference: the hose's own per-tick impulse at 10 Hz on a Rusher.
                SetPower(1);
                RobotEnemy hose = NewRobot(EnemyArchetype.Rusher);
                float hoseDisp = DisplacementOverOneSecond(hose, 10f,
                    () => hose.ApplyKnockback(Vector3.forward * WaterBlaster.DefaultSprayKnockback));
                Object.DestroyImmediate(hose.gameObject); RobotEnemy.ResetRegistry();
                Assert.Greater(hoseDisp, 0.5f, "the hose reference must displace the Rusher");

                var rates = new Dictionary<string, float>
                {
                    { "Ricochet", WeaponPush.RicochetHitsPerSecond },
                    { "Undertow", WeaponPush.UndertowHitsPerSecond },
                    { "LightMissile", WeaponPush.LightMissileHitsPerSecond },
                };
                foreach (var kv in rates)
                {
                    float l1 = Measure(EnemyArchetype.Rusher, kv.Value, 1);
                    Assert.AreEqual(hoseDisp, l1, hoseDisp * 0.2f, $"{kv.Key} POWER 1 vs hose displacement");
                    float l5 = Measure(EnemyArchetype.Rusher, kv.Value, 5);
                    Assert.AreEqual(1.48f * l1, l5, 1.48f * l1 * 0.1f, $"{kv.Key} POWER 5 vs 1.48x its level 1");
                }

                // Rocket: radial, 1 m from the blast, away from the centre.
                SetPower(1);
                RobotEnemy rusher = NewRobot(EnemyArchetype.Rusher);
                Vector3 blast = rusher.transform.position + Vector3.left; // blast 1 m to the -X side
                WeaponPush.ApplyRadial(rusher, rusher.transform.position, blast, WeaponPush.RocketHitsPerSecond);
                float before = rusher.transform.position.x;
                for (int i = 0; i < 200; i++) { TickKnockback.Invoke(rusher, new object[] { Dt }); Physics.SyncTransforms(); }
                Assert.Greater(rusher.transform.position.x - before, 0.1f, "a blast pushes the Rusher away from its centre");
                Object.DestroyImmediate(rusher.gameObject); RobotEnemy.ResetRegistry();

                // Dreadnought: any weapon moves it 0 m.
                Assert.AreEqual(0f, Measure(EnemyArchetype.Dreadnought, WeaponPush.UndertowHitsPerSecond, 5), 0.001f,
                    "a Dreadnought must not move");
            }
            finally { RigState.Reset(); RobotEnemy.ResetRegistry(); }
        }
    }
}
