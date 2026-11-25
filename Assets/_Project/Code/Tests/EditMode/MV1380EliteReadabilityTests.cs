using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1380: a Shielded elite hit from the front (fully deflected, so it never takes damage and the
    /// damage trigger never showed its bar) must still resolve a nameplate containing "SHIELDED", an
    /// active health bar strip, emit the deflect signal, and wear a #E53935 elite ring.
    /// Fails on 9733772: SetEliteMarker has no affix text, the bar only shows after damage, and the
    /// elite ring tint is pale gold (0.90, 0.77, 0.35).
    /// </summary>
    public sealed class MV1380EliteReadabilityTests
    {
        [Test]
        public void ShieldedElite_FrontHit_ShowsAffixNameplate_BarAndRedRing_MV1380()
        {
            RobotEnemy.ResetRegistry();
            var root = new GameObject("MV1380 Probe Root");
            int deflects = 0;
            System.Action<Vector3, Vector3> onDeflect = (p, d) => deflects++;
            HudSignals.ShieldDeflected += onDeflect;
            try
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<CharacterController>();
                RobotEnemy robot = go.AddComponent<RobotEnemy>();
                typeof(RobotEnemy).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(robot, null);
                robot.Apply(EnemyArchetype.Of(EnemyKind.Rusher).WithElite(EliteAffix.Shielded));

                robot.TakeDamage(new DamageInfo(1f, go.transform.position, Vector3.back, Team.Player));
                robot.SyncEliteMarker();
                WorldHealthBar bar = robot.HealthBar;
                Assert.IsNotNull(bar);
                bar.RefreshForTests();

                Assert.AreEqual(1, deflects, "a fully deflected front hit must emit the deflect signal");
                StringAssert.Contains("SHIELDED", bar.EliteMarkerText);
                Assert.IsTrue(bar.EliteMarkerActive, "the elite tag must be active");
                Assert.IsTrue(bar.BarStripActive, "an undamaged elite must still show its health bar");

                var ringHost = new GameObject("MV1380 Anchor");
                GroundRing ring = ringHost.AddComponent<GroundAnchorVfx>().BuildEliteRingForTests();
                Color c = ring.ResolvedTint;
                Assert.AreEqual(0xE5, Mathf.RoundToInt(c.r * 255f), 2, "ring R");
                Assert.AreEqual(0x39, Mathf.RoundToInt(c.g * 255f), 2, "ring G");
                Assert.AreEqual(0x35, Mathf.RoundToInt(c.b * 255f), 2, "ring B");
                Object.DestroyImmediate(ringHost);
            }
            finally
            {
                HudSignals.ShieldDeflected -= onDeflect;
                Object.DestroyImmediate(root);
                RobotEnemy.ResetRegistry();
            }
        }
    }
}
