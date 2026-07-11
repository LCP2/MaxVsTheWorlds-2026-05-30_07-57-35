using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// YT-55 — the boss spectacle. The one that matters is the defeat sequence surviving the pause:
    /// the run ends the instant the boss dies and the result screen freezes everything with
    /// timeScale = 0, so any scaled-time explosion would be frozen solid before a particle moved.
    /// </summary>
    public sealed class BossSpectaclePlayTests
    {
        [TearDown]
        public void TearDown() => Time.timeScale = 1f;

        [UnityTest]
        public IEnumerator DeathVfx_StillPlays_WhileTheGameIsFrozenByTheResultScreen()
        {
            yield return null;   // let BossSpectacle install itself

            Assert.IsNotNull(Object.FindFirstObjectByType<BossSpectacle>(),
                "BossSpectacle should install itself with no scene wiring");

            // Exactly what happens on a win: the boss dies and the run ends in the same frame.
            HudSignals.EmitBossDefeated();
            Time.timeScale = 0f;

            var fire = FindSystem("BossFire");
            Assert.IsNotNull(fire, "the defeat fire system was never built");
            Assert.IsTrue(fire.main.useUnscaledTime,
                "the death VFX must run on unscaled time, or the result screen's pause freezes the " +
                "explosion before the player sees any of it");

            // Give the (realtime) sequence a moment and confirm particles actually moved.
            yield return new WaitForSecondsRealtime(0.35f);

            Assert.That(fire.particleCount, Is.GreaterThan(0),
                "the defeat burst should be alive on screen even though the game is paused");
        }

        [UnityTest]
        public IEnumerator Engage_KicksUpAnIntroBeat()
        {
            yield return null;

            HudSignals.EmitBossEngaged("BIG BERMUDA", 2);
            yield return null;
            yield return null;

            var dust = FindSystem("BossDust");
            Assert.IsNotNull(dust);
            Assert.That(dust.particleCount, Is.GreaterThan(0),
                "the boss waking up should kick dust off the ground");
        }

        private static ParticleSystem FindSystem(string name)
        {
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (ps.name == name) return ps;
            }
            return null;
        }
    }
}
