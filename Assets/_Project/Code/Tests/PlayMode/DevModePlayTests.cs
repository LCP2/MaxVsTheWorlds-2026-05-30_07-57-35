using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// YT-60 — dev mode against a real player. PlayMode because PlayerHealth reads its starting
    /// health in Awake, which never runs in edit mode.
    ///
    /// The test that actually matters is the first one: a debug affordance that leaks into a normal
    /// session is worse than no debug affordance at all.
    /// </summary>
    public sealed class DevModePlayTests
    {
        [TearDown]
        public void TearDown() => DevMode.Reset();

        [UnityTest]
        public IEnumerator Player_IsAsMortalAsEver_WhenDevModeIsOff()
        {
            DevMode.Reset();

            var go = new GameObject("player", typeof(PlayerHealth));
            yield return null;   // let Awake run

            var hp = go.GetComponent<PlayerHealth>();
            float before = hp.Current;
            Assert.That(before, Is.GreaterThan(0f), "the player should start alive");

            hp.TakeDamage(new DamageInfo(10f, Vector3.zero, Vector3.forward, Team.Enemy));

            Assert.That(hp.Current, Is.LessThan(before),
                "with dev mode off, nothing about damage may change");

            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator Player_IsInvincible_OnlyWhileDevModeIsOn()
        {
            var go = new GameObject("player", typeof(PlayerHealth));
            yield return null;

            var hp = go.GetComponent<PlayerHealth>();
            float full = hp.Current;

            DevMode.Enabled = true;
            hp.TakeDamage(new DamageInfo(9999f, Vector3.zero, Vector3.forward, Team.Enemy));
            Assert.AreEqual(full, hp.Current, 1e-3f, "dev mode should make Max invincible");
            Assert.IsTrue(hp.IsAlive);

            DevMode.Reset();
            hp.TakeDamage(new DamageInfo(10f, Vector3.zero, Vector3.forward, Team.Enemy));
            Assert.That(hp.Current, Is.LessThan(full), "and mortal again the moment it's switched off");

            Object.Destroy(go);
        }
    }
}
