using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The aim indicator and the water under it have to describe the same weapon (YT-110).
    ///
    /// They had drifted badly: a 6° jet drawn beneath a 35° indicator, so the reticle looked like it
    /// was lying about where the water went — when the reticle was the honest one and the VFX was
    /// drawing a fifth of the arc the blaster actually damages.
    ///
    /// These assert the RELATIONSHIP rather than the two numbers. Retuning the blaster's cone is a
    /// game-feel call somebody will make later, and when they do the spray must follow it without
    /// anyone remembering this file exists.
    /// </summary>
    public sealed class SprayIndicatorWidthTests
    {
        /// <summary>What the shipped blaster uses, and what the reticle is built from.</summary>
        private const float BlasterConeHalfAngle = 35f;

        [Test]
        public void TheIndicatorIsTwiceTheWidthOfTheVisibleSpray()
        {
            float spray = WaterVfx.SprayHalfAngleFor(BlasterConeHalfAngle);

            Assert.That(BlasterConeHalfAngle / spray, Is.EqualTo(2f).Within(0.01f),
                        "YT-110 asks for an indicator about twice the width of the water you can see");
        }

        [Test]
        public void TheSprayFollowsTheConeWhenTheWeaponIsRetuned()
        {
            foreach (float cone in new[] { 12f, 20f, 35f, 60f })
            {
                Assert.That(WaterVfx.SprayHalfAngleFor(cone), Is.EqualTo(cone * 0.5f).Within(0.01f),
                            $"a {cone}° weapon must draw a {cone * 0.5f}° stream");
            }
        }

        /// <summary>
        /// The old failure, pinned so it cannot come back: the spray must never be a token jet under
        /// a broad indicator. At the shipped cone the previous 6° stream filled well under a quarter
        /// of the arc, which is what made the indicator read as dishonest.
        /// </summary>
        [Test]
        public void TheSprayIsNoLongerATokenJetUnderAWideIndicator()
        {
            float spray = WaterVfx.SprayHalfAngleFor(BlasterConeHalfAngle);

            Assert.That(spray, Is.GreaterThan(6f),
                        "still the old 6° jet — the water would not read as covering what it hits");
            Assert.That(spray / BlasterConeHalfAngle, Is.GreaterThan(0.25f));
        }

        /// <summary>
        /// The spray must stay strictly inside the indicator. A stream drawn wider than the arc that
        /// damages would promise reach the weapon does not have — the one direction this must never
        /// fail in, since the player aims by the water.
        /// </summary>
        [Test]
        public void TheWaterNeverReachesOutsideTheIndicator()
        {
            foreach (float cone in new[] { 12f, 35f, 60f })
                Assert.That(WaterVfx.SprayHalfAngleFor(cone), Is.LessThanOrEqualTo(cone),
                            "the visible water spills outside the arc the blaster actually hits");
        }

        [Test]
        public void ADegenerateWeaponStillGetsAVisibleStream()
        {
            Assert.That(WaterVfx.SprayHalfAngleFor(0f), Is.GreaterThan(0f),
                        "a zero-width cone must not collapse the particle shape to nothing");
            Assert.That(WaterVfx.SprayHalfAngleFor(-10f), Is.GreaterThan(0f));
        }

        [Test]
        public void TheBlasterBuildsItsWaterFromItsOwnCone()
        {
            var go = new GameObject("Blaster");
            try
            {
                var vfx = go.AddComponent<WaterVfx>();
                vfx.Init(range: 6f, radius: 0.6f, coneHalfAngle: BlasterConeHalfAngle);

                Assert.That(vfx.StreamHalfAngle,
                            Is.EqualTo(WaterVfx.SprayHalfAngleFor(BlasterConeHalfAngle)).Within(0.01f),
                            "Init ignored the cone it was handed");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
