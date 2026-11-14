using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1370 (the one new test): driving the Helmsman to 65.9% health through the real
    /// <see cref="IDamageable.TakeDamage"/> path puts it in phase 2 (it was a single threshold at 50%),
    /// and the 2 s P1 -> P2 beat is invulnerable - damage dealt during it does not reduce health (a
    /// resolved value, Tier 2) - and only after the beat does damage land again.
    ///
    /// Fails on base commit 299193e: the only threshold is 0.5, so 65.9% is still phase 1
    /// (<see cref="HelmsmanBoss.IsWalking"/> is false straight after the hit).
    /// </summary>
    public sealed class MV1370HelmsmanPhaseBeatTests
    {
        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private static void SetPrivateField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static DestructibleHealth GetHealth(HelmsmanBoss boss) =>
            (DestructibleHealth)typeof(HelmsmanBoss)
                .GetField("_health", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(boss);

        [Test]
        public void At65Point9Percent_IsPhaseTwo_AndInvulnerableForTheTwoSecondBeat()
        {
            GameObject bossGo = null, playerGo = null;
            try
            {
                playerGo = new GameObject("MV1370 Max") { tag = "Player" };
                playerGo.transform.position = new Vector3(8f, 0f, 0f);

                bossGo = new GameObject("MV1370 Helmsman");
                bossGo.AddComponent<CharacterController>();
                var boss = bossGo.AddComponent<HelmsmanBoss>();
                InvokeAwake(boss);   // AddComponent never fires Awake in this EditMode harness
                SetPrivateField(boss, "_phase", System.Enum.Parse(
                    typeof(HelmsmanBoss).GetNestedType("Phase", BindingFlags.NonPublic), "Seated"));

                DestructibleHealth health = GetHealth(boss);
                Assert.IsFalse(boss.IsWalking, "must start in phase 1");

                boss.TakeDamage(new DamageInfo(health.Max * 0.341f, Vector3.zero, Vector3.forward, Team.Player));
                Assert.AreEqual(0.659f, health.Normalized, 0.001f, "the hit should leave it at 65.9%");
                Assert.IsTrue(boss.IsWalking, "65.9% is below the 66% line: it must be in phase 2");

                // The beat: 1.9 s in, a huge hit changes nothing.
                const float dt = 0.1f;
                float held = health.Current;
                for (int i = 0; i < 19; i++) boss.Tick(dt);
                boss.TakeDamage(new DamageInfo(health.Max * 0.2f, Vector3.zero, Vector3.forward, Team.Player));
                Assert.AreEqual(held, health.Current, 0.001f, "damage during the 2 s beat must not reduce health");

                // Past 2 s the beat is over and damage lands again.
                for (int i = 0; i < 3; i++) boss.Tick(dt);
                boss.TakeDamage(new DamageInfo(100f, Vector3.zero, Vector3.forward, Team.Player));
                Assert.AreEqual(held - 100f, health.Current, 0.001f, "after the beat damage must land again");
            }
            finally
            {
                if (bossGo != null) Object.DestroyImmediate(bossGo);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                BossCensus.Reset();
            }
        }
    }
}
