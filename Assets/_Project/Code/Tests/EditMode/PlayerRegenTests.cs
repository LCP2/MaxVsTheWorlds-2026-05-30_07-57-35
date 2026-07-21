using NUnit.Framework;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Max's slow out-of-combat regen (YT-80). The whole design lives between two failure modes —
    /// too slow and it may as well not exist, too fast and it deletes the reason to dodge — so these
    /// pin BOTH edges, not just that the number moves.
    /// </summary>
    public sealed class PlayerRegenTests
    {
        private const float Max = 69.82f;    // PlayerHealth.maxHealth (YT-106: Lee's number, was 100)
        private const float PerSec = PlayerTuning.RegenPerSec;
        private const float Delay = PlayerTuning.RegenDelay;
        private static readonly float RusherHit = EnemyArchetype.Rusher.ContactDamage;

        private static float Regen(float current, float timeSinceDamage, float dt) =>
            PlayerHealth.Regenerate(current, Max, timeSinceDamage, Delay, PerSec, dt);

        [Test]
        public void NothingHappensUntilMaxHasBeenLeftAlone()
        {
            Assert.AreEqual(50f, Regen(50f, timeSinceDamage: 0f, dt: 1f), 1e-4);
            Assert.AreEqual(50f, Regen(50f, timeSinceDamage: Delay - 0.01f, dt: 1f), 1e-4);
        }

        [Test]
        public void OncePastTheDelay_HealthTicksBackUp()
        {
            Assert.AreEqual(53f, Regen(50f, timeSinceDamage: Delay, dt: 1f), 1e-4);
        }

        [Test]
        public void ItNeverOverfills()
        {
            Assert.AreEqual(Max, Regen(Max - 1f, timeSinceDamage: 60f, dt: 10f), 1e-4);
            Assert.AreEqual(Max, Regen(Max, timeSinceDamage: 60f, dt: 10f), 1e-4);
        }

        [Test]
        public void ItNeverRevivesACorpse()
        {
            // Death is death — the Result screen has already been shown by the time this could fire.
            Assert.AreEqual(0f, Regen(0f, timeSinceDamage: 999f, dt: 10f), 1e-4);
        }

        [Test]
        public void ItCannotOutHealEvenASingleRusher()
        {
            // The dodge pressure has to survive this feature. One rusher lands ~12 damage roughly
            // every couple of seconds; regen pays 3/s and only after five untouched seconds — which
            // a rusher that is still on you will never allow. Standing in a pack must still kill.
            float regenAcrossAFullDelay = PerSec * Delay;   // the best case: 15 HP per 5s window
            Assert.Less(regenAcrossAFullDelay, RusherHit * 2f,
                "regen must not pay for more than it costs to stand still and get hit");
        }

        [Test]
        public void HealingAFullBarTakesLongEnoughToHurt()
        {
            float secondsToFull = Max / PerSec;
            // Lee's smaller pool (YT-106) heals in ~23s rather than ~33s — but with less HP to lose,
            // Max is more vulnerable, not less, so a full heal still has to be a real investment.
            Assert.GreaterOrEqual(secondsToFull, 20f,
                "if a full heal is quick, damage stops mattering and so does dodging");
        }

        [Test]
        public void ButItIsFastEnoughToActuallyNotice()
        {
            // The other edge: a trickle nobody sees is a feature nobody has. Getting clear of a
            // fight should visibly buy back a rusher's hit inside a few seconds.
            float secondsToUndoOneHit = RusherHit / PerSec;
            Assert.LessOrEqual(secondsToUndoOneHit, 6f,
                "disengaging has to visibly pay, or there's no reward for surviving");
        }
    }
}
