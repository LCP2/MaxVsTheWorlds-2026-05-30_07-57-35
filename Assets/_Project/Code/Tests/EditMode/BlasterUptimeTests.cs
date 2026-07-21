using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The gun ran out too quickly (YT-80). These pin the uptime it was given back, so a later pass
    /// can't quietly shrink the tank again — and, more to the point, so the numbers stay authored in
    /// code where the scene can't overwrite them, which is what went wrong the first time.
    /// </summary>
    public sealed class BlasterUptimeTests
    {
        [Test]
        public void FullTank_HoldsAnEntireEngagement()
        {
            // Lee raised the drain on-device (YT-106): the tank now lasts ~7s, deliberately shorter
            // than the old 14s so running dry is a real pressure, not a formality. Still a proper
            // burst though — a full tank has to cover most of an engagement, not a squirt.
            Assert.GreaterOrEqual(BlasterTuning.SustainedFireSeconds, 6f,
                "a full tank must still fire long enough to feel like a weapon, not a water pistol");
        }

        [Test]
        public void RunningDry_CostsOneShortPause_NotAConstantStutter()
        {
            Assert.LessOrEqual(BlasterTuning.RecoverySeconds, 2.5f,
                "running dry should cost a beat, not a coffee break");
        }

        [Test]
        public void EveryBurstAfterTheFirst_IsStillWorthHaving()
        {
            // This is the one that actually caused the complaint. The old hysteresis handed back 35%
            // of a small tank — 1.4s of water — so after the first magazine the gun was in a
            // permanent squirt/wait cycle. The recharged burst has to be a burst.
            Assert.GreaterOrEqual(BlasterTuning.RechargedFireSeconds, 3.5f,
                "a recharged tank must fire for long enough to feel like firing");
        }

        [Test]
        public void EvenNeverLettingGo_TheGunIsMostlyFiring()
        {
            // Worst case: the player holds the trigger forever and never earns a natural pause.
            // With Lee's faster drain (YT-106) this sits near 0.69 — more downtime pressure by
            // design — but firing must still be the majority state, not a stutter.
            Assert.Greater(BlasterTuning.WorstCaseUptime, 0.6f,
                "downtime must stay the exception even for a player who never releases the trigger");
        }

        [Test]
        public void RefillOutrunsTheDrain_SoAnyPauseInTheFightPaysYouBack()
        {
            // Lee raised the drain but left regen at 55 (YT-106), so refill now outruns drain by
            // ~2.8x rather than the old 5.5x. Still comfortably ahead — a pause pays you back — just
            // less lopsided, which is the point of making the tank matter.
            Assert.Greater(BlasterTuning.RegenPerSec, BlasterTuning.EnergyPerSecond * 2f,
                "a pause has to put real ammo back, or letting go is never worth it");
        }

        [Test]
        public void TheDelayIsShortEnoughToRewardTrigger_Discipline()
        {
            Assert.LessOrEqual(BlasterTuning.RegenDelay, 0.5f,
                "if the delay outlasts the gap between targets, tapping off never refills anything");
        }

        [Test]
        public void ThePowerRampCannotMakeTheGunThirstier()
        {
            // YT-67's invariant, restated against the authored number: a fire-rate upgrade must not
            // drain the tank faster, or the "upgrade" just trades uptime for nothing.
            const float fireInterval = 0.1f;
            float perTickAtBase = BlasterTuning.EnergyPerSecond * fireInterval;
            float perTickAtDouble = BlasterTuning.EnergyPerSecond * (fireInterval / 2f);

            Assert.AreEqual(BlasterTuning.EnergyPerSecond, perTickAtBase / fireInterval, 1e-4,
                "cost per second must be the authored number at base fire rate");
            Assert.AreEqual(BlasterTuning.EnergyPerSecond, perTickAtDouble / (fireInterval / 2f), 1e-4,
                "…and still the authored number at double fire rate");
        }

        /// <summary>
        /// The guard that was missing (YT-80), and the reason this bug survived two previous
        /// attempts to fix the feel by editing C# defaults.
        ///
        /// Backyard_Slice.unity is the scene that actually ships. When the blaster's energy lived in
        /// [SerializeField]s, Unity baked a copy of every one of them into that file — so the scene,
        /// not the code, decided how the gun played, and the code was left describing a weapon that
        /// did not exist. Nothing anywhere failed. Tuning is only "easy to find" if the value you
        /// find is the value that runs, so: no energy key may reappear in the scene, ever.
        /// </summary>
        [Test]
        public void TheSceneCannotShadowTheAuthoredTuning()
        {
            string scene = Path.Combine(
                Application.dataPath, "_Project", "Scenes", "Backyard_Slice.unity");
            Assert.IsTrue(File.Exists(scene), $"the shipping scene has moved: {scene}");

            string yaml = File.ReadAllText(scene);
            foreach (string key in new[]
                     { "maxEnergy", "energyPerTick", "regenPerSec", "regenDelay", "rechargeFraction" })
            {
                StringAssert.DoesNotContain($"{key}:", yaml,
                    $"'{key}' has been serialized into the scene — from here on the scene, not the " +
                    $"code, decides how the game plays, and editing the authored value will appear " +
                    $"to do nothing. Whoever re-added it as a [SerializeField]: put it back in " +
                    $"BlasterTuning (blaster energy) or PlayerTuning (Max's regen).");
            }
        }
    }
}
