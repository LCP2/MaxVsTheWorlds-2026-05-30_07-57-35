using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-398: deployed sentinels (Wall and Gunner) passively regen HP once left unhit for a while.
    /// The formula is <see cref="Sentinel.Regenerate"/>, the same delay-gated linear trickle as
    /// <see cref="MaxWorlds.Player.PlayerHealth"/>'s own YT-80 regen — the ticket asked for tuning
    /// "consistent with the game's existing pacing" rather than an invented number, so
    /// <see cref="AbilityTuning.DefaultSentinelRegenDelaySeconds"/>/<see
    /// cref="AbilityTuning.DefaultSentinelRegenPerSec"/> alias Max's own values outright. These tests
    /// pin the shared math against both the Wall's (200) and Gunner's (60) base HP.
    /// </summary>
    public sealed class SentinelRegenTests
    {
        private const float Delay = AbilityTuning.DefaultSentinelRegenDelaySeconds;
        private const float PerSec = AbilityTuning.DefaultSentinelRegenPerSec;

        private static float Regen(float current, float max, float timeSinceDamage, float dt) =>
            Sentinel.Regenerate(current, max, timeSinceDamage, Delay, PerSec, dt);

        [Test]
        public void NothingHappensUntilTheDelayHasElapsed()
        {
            Assert.AreEqual(100f, Regen(100f, 200f, timeSinceDamage: 0f, dt: 1f), 1e-4);
            Assert.AreEqual(100f, Regen(100f, 200f, timeSinceDamage: Delay - 0.01f, dt: 1f), 1e-4);
        }

        [Test]
        public void OncePastTheDelay_HealthTicksBackUp()
        {
            Assert.AreEqual(100f + PerSec, Regen(100f, 200f, timeSinceDamage: Delay, dt: 1f), 1e-4);
        }

        [Test]
        public void ItNeverOverfillsPastMax()
        {
            Assert.AreEqual(200f, Regen(199f, 200f, timeSinceDamage: 60f, dt: 10f), 1e-4);
            Assert.AreEqual(60f, Regen(60f, 60f, timeSinceDamage: 60f, dt: 10f), 1e-4);
        }

        [Test]
        public void ItNeverRevivesADestroyedSentinel()
        {
            // A destroyed sentinel is gone for good (DECISION, 15 Aug 2026, untouched by MV-398) — the
            // pure formula itself must refuse to lift a 0-HP sentinel off the floor, same as
            // PlayerHealth.Regenerate never revives a dead Max.
            Assert.AreEqual(0f, Regen(0f, 200f, timeSinceDamage: 999f, dt: 10f), 1e-4);
        }

        [Test]
        public void TheSentinelsBaseHpStillFullyHealsInAFiniteTime()
        {
            // Starts at 1 HP, not 0 — 0 HP means already destroyed (see ItNeverRevivesADestroyedSentinel
            // above), which a live sentinel's Update() never reaches in the first place (IsAlive gates it).
            float secondsToFull = 59f / PerSec; // the sentinel's base 60 HP (AbilityTuning.DefaultSentinelBaseHp)
            Assert.Greater(secondsToFull, 0f);
            Assert.AreEqual(60f, Regen(1f, 60f, timeSinceDamage: Delay + secondsToFull, dt: secondsToFull), 1e-3);
        }
    }
}
