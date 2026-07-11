using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>YT-54 — damage-number aggregation and bar animation.</summary>
    public sealed class HudPolishTests
    {
        // --- damage numbers ---

        [Test]
        public void SustainedFire_OnOneEnemy_BecomesASingleAccumulatedNumber()
        {
            var agg = new DamageNumberAggregator { Window = 0.35f };
            var results = new List<DamageNumberAggregator.Entry>();

            // The blaster ticks every 0.1s on the same enemy.
            for (int i = 0; i < 6; i++) agg.Add(new Vector3(5f, 0f, 5f), 4f, false, i * 0.1f);

            agg.Flush(0.6f, results);

            Assert.AreEqual(1, results.Count, "six ticks on one enemy must not spawn six numbers");
            Assert.AreEqual(24f, results[0].Amount, 1e-3f,
                "the merged number must show the TOTAL — '24' is quieter than '4 4 4 4 4 4' and " +
                "strictly more informative");
        }

        [Test]
        public void EnemiesStandingApart_KeepTheirOwnNumbers()
        {
            var agg = new DamageNumberAggregator { Window = 0.2f };
            var results = new List<DamageNumberAggregator.Entry>();

            agg.Add(new Vector3(0f, 0f, 0f), 4f, false, 0f);
            agg.Add(new Vector3(10f, 0f, 0f), 4f, false, 0f);

            agg.Flush(0.5f, results);

            Assert.AreEqual(2, results.Count, "separate enemies must each get their own number");
        }

        [Test]
        public void NumbersAreCapped_SoACrowdCannotBuryTheScreen()
        {
            var agg = new DamageNumberAggregator { Window = 0.2f, MaxPerFlush = 8 };
            var results = new List<DamageNumberAggregator.Entry>();

            // 30 enemies, all being sprayed at once.
            for (int i = 0; i < 30; i++) agg.Add(new Vector3(i * 3f, 0f, 0f), 4f + i, false, 0f);

            agg.Flush(0.5f, results);

            Assert.AreEqual(8, results.Count, "the cap must hold at 20-30 enemies");
            Assert.That(agg.OpenBuckets, Is.EqualTo(0),
                "dropped numbers must still be retired, or they'd leak and re-emit forever");

            // The ones that survive should be the biggest hits — the small ones are the ones a
            // player would never have looked at anyway.
            foreach (var e in results) Assert.That(e.Amount, Is.GreaterThan(20f));
        }

        [Test]
        public void DamageIsHeldUntilTheWindowElapses()
        {
            var agg = new DamageNumberAggregator { Window = 0.35f };
            var results = new List<DamageNumberAggregator.Entry>();

            agg.Add(Vector3.zero, 4f, false, 0f);
            agg.Flush(0.1f, results);
            Assert.AreEqual(0, results.Count, "nothing should show before the merge window closes");

            agg.Flush(0.4f, results);
            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void ACritAnywhereInTheBurst_MakesTheWholeNumberACrit()
        {
            var agg = new DamageNumberAggregator { Window = 0.2f };
            var results = new List<DamageNumberAggregator.Entry>();

            agg.Add(Vector3.zero, 4f, false, 0f);
            agg.Add(Vector3.zero, 9f, true, 0.05f);

            agg.Flush(0.5f, results);

            Assert.IsTrue(results[0].Crit, "a crit inside the burst must not be silently swallowed");
        }

        // --- bars ---

        [Test]
        public void Bar_GhostHoldsTheOldValueThenDrains()
        {
            var bar = new BarState();
            bar.Snap(1f);

            bar.Update(0.5f, 0.016f);   // took a big hit

            Assert.That(bar.Ghost, Is.GreaterThan(bar.Fill),
                "the ghost must lag the fill — the gap between them IS the damage readout");
            Assert.That(bar.Flash, Is.GreaterThan(0f), "a hit should flash the bar");

            // Long enough for the hold to expire and the ghost to catch up.
            for (int i = 0; i < 400; i++) bar.Update(0.5f, 0.016f);

            Assert.That(bar.Fill, Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(bar.Ghost, Is.EqualTo(0.5f).Within(1e-3f), "the ghost must eventually catch up");
            Assert.That(bar.Flash, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void Bar_GhostNeverFallsBelowTheFillWhenHealing()
        {
            var bar = new BarState();
            bar.Snap(0.2f);

            for (int i = 0; i < 200; i++) bar.Update(1f, 0.016f);   // heal to full

            Assert.That(bar.Ghost, Is.GreaterThanOrEqualTo(bar.Fill - 1e-4f),
                "a ghost below the fill would render as an inverted chip bar");
            Assert.That(bar.Fill, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void Bar_SmoothsRatherThanSnapping()
        {
            var bar = new BarState();
            bar.Snap(1f);

            bar.Update(0f, 0.016f);   // one frame of a huge drop

            Assert.That(bar.Fill, Is.GreaterThan(0f),
                "the bar must ease toward its value, not teleport — a snap tells the player nothing");
        }
    }
}
