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
    /// MV-1480 AC1-AC3 (supersedes MV1476AllWeaponsPushTests): a weapon hit HOLDS a robot (no walking) and
    /// pushes it only a token distance. Asserts RESOLVED positions of a real <see cref="RobotEnemy"/> after
    /// simulated ticks (Tier 2). The chase is modelled by stepping toward Max at the robot's own resolved
    /// <see cref="RobotEnemy.EffectiveMoveSpeed"/>. Fails on b68c6b2 (base): ApplyWeaponHit/IsHitHeld/HoldSeconds
    /// do not exist there, so this does not compile.
    /// </summary>
    public sealed class MV1480WeaponHitHoldTests
    {
        private const float Dt = 0.005f;
        private static readonly Vector3 Origin = new Vector3(0f, 500f, 0f);
        private static readonly Vector3 Max = new Vector3(0f, 500f, 10f);
        private static readonly BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo CcField = typeof(RobotEnemy).GetField("_cc", NP);
        private static readonly MethodInfo TickKnockback =
            typeof(RobotEnemy).GetMethod("ApplyKnockback", NP, null, new[] { typeof(float) }, null);
        private static readonly MethodInfo TickHold = typeof(RobotEnemy).GetMethod("TickHitHold", NP);

        private static RobotEnemy NewRobot(EnemyArchetype a)
        {
            var go = new GameObject("Hold Probe");
            go.transform.position = Origin;
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(a);
            Physics.SyncTransforms();
            return e;
        }

        private static void SetPower(int level) =>
            RigState.RestoreSnapshot(new Dictionary<string, int> { { "p_dmg", level } }, new string[0]);

        /// <summary>One simulated tick: hold timer, chase step toward Max at the resolved speed, knockback.</summary>
        private static void Step(RobotEnemy r)
        {
            TickHold.Invoke(r, new object[] { Dt });
            Vector3 to = Max - r.transform.position; to.y = 0f;
            r.transform.position += to.normalized * (r.EffectiveMoveSpeed * Dt);
            Physics.SyncTransforms();
            TickKnockback.Invoke(r, new object[] { Dt });
            Physics.SyncTransforms();
        }

        private static float Fire(RobotEnemy r, float seconds, float hitsPerSecond)
        {
            float interval = 1f / hitsPerSecond, timer = 0f;
            for (int i = 0; i < Mathf.RoundToInt(seconds / Dt); i++)
            {
                timer -= Dt;
                if (timer <= 0f) { WeaponPush.Apply(r, Vector3.back, hitsPerSecond); timer += interval; }
                Step(r);
            }
            return r.transform.position.z;
        }

        [Test]
        public void HitHoldsRusherTinyPushThenResumesAndChargeAndBlastBehave()
        {
            try
            {
                foreach (var c in new[] { (power: 1, limit: 0.3f), (power: 5, limit: 0.5f) })
                {
                    SetPower(c.power);
                    RobotEnemy r = NewRobot(EnemyArchetype.Rusher);
                    float z0 = r.transform.position.z;
                    float z = Fire(r, 2f, WeaponPush.UndertowHitsPerSecond);
                    Assert.LessOrEqual(z, z0 + 0.0001f, $"POWER {c.power}: never closer to Max");
                    Assert.GreaterOrEqual(z, z0 - c.limit, $"POWER {c.power}: within {c.limit} m behind first hit");
                    Object.DestroyImmediate(r.gameObject); RobotEnemy.ResetRegistry();
                }

                // AC2: resumes at normal speed 0.3 s after the last hit.
                SetPower(1);
                RobotEnemy rr = NewRobot(EnemyArchetype.Rusher);
                float normal = rr.EffectiveMoveSpeed;
                Assert.Greater(normal, 0.5f);
                WeaponPush.Apply(rr, Vector3.back, 10f);
                Assert.AreEqual(0f, rr.EffectiveMoveSpeed, 1e-5f, "held: no walking");
                for (int i = 0; i < Mathf.RoundToInt(0.31f / Dt); i++) TickHold.Invoke(rr, new object[] { Dt });
                Assert.AreEqual(normal, rr.EffectiveMoveSpeed, 1e-4f, "resumes at normal speed");
                Object.DestroyImmediate(rr.gameObject); RobotEnemy.ResetRegistry();

                // AC3: a Charger mid-charge is not held.
                RobotEnemy ch = NewRobot(EnemyArchetype.Charger);
                typeof(RobotEnemy).GetProperty("Current").SetValue(ch, RobotEnemy.State.Lunge);
                WeaponPush.Apply(ch, Vector3.back, 10f);
                Assert.IsFalse(ch.IsHitHeld, "a committed charge carries on");
                Object.DestroyImmediate(ch.gameObject); RobotEnemy.ResetRegistry();

                // AC3: a rocket blast moves a Rusher <= 0.4 m.
                RobotEnemy rk = NewRobot(EnemyArchetype.Rusher);
                Vector3 p0 = rk.transform.position;
                WeaponPush.ApplyRadial(rk, p0, p0 + Vector3.left * 0.2f, WeaponPush.RocketHitsPerSecond);
                for (int i = 0; i < 400; i++) { TickKnockback.Invoke(rk, new object[] { Dt }); Physics.SyncTransforms(); }
                float moved = (rk.transform.position - p0).magnitude;
                Assert.Greater(moved, 0.05f, "the blast still nudges");
                Assert.LessOrEqual(moved, 0.4f, "blast push cap");
                Object.DestroyImmediate(rk.gameObject); RobotEnemy.ResetRegistry();
            }
            finally { RigState.Reset(); RobotEnemy.ResetRegistry(); }
        }
    }
}
