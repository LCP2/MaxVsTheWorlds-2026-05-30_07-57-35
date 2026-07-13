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
            // A fight with a factory's wave is on the order of ten seconds of held trigger. The old
            // gun managed 4.0s, so you ran dry in the middle of every single one.
            Assert.GreaterOrEqual(BlasterTuning.SustainedFireSeconds, 10f,
                "a full tank must cover a normal engagement without tapping out");
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
            Assert.GreaterOrEqual(BlasterTuning.RechargedFireSeconds, 5f,
                "a recharged tank must fire for long enough to feel like firing");
        }

        [Test]
        public void EvenNeverLettingGo_TheGunIsMostlyFiring()
        {
            // Worst case: the player holds the trigger forever and never earns a natural pause.
            Assert.Greater(BlasterTuning.WorstCaseUptime, 0.75f,
                "downtime must be the exception even for a player who never releases the trigger");
        }

        [Test]
        public void RefillOutrunsTheDrain_SoAnyPauseInTheFightPaysYouBack()
        {
            Assert.Greater(BlasterTuning.RegenPerSec, BlasterTuning.EnergyPerSecond * 3f,
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
