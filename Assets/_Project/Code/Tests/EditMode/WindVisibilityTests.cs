using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Does the wind actually MOVE ANYTHING ON THE SCREEN? (YT-78, second pass.)
    ///
    /// This is the test the first cut of this ticket needed and did not have. WindTests checks that
    /// the wind is a number in a range of METRES — and every one of those tests passed while the yard
    /// was, to Lee's eye and to a pixel diff of the shipped build, completely still. A sway measured
    /// in metres tells you nothing: the game is played from 25 m back through a 40 degree lens, and
    /// whether a plant moves is a question about PIXELS.
    ///
    /// So this converts. It models the amplitude curve in StylizedSurface.Wind() (the `lerp(0.15, 1,
    /// saturate(posWS.y / _WindHeight)) * gust` at StylizedSurface.shader:116-120), projects the
    /// result through the real rig, and asks the only question that matters: how far does the top of
    /// a bush travel, in pixels, on Lee's screen and on a phone.
    ///
    /// The lower bound is "a human can see this". The upper bound is the Craft Bible's line that juice
    /// must never obscure — ambience that pulls the eye off a telegraph has stopped being ambience.
    /// </summary>
    public sealed class WindVisibilityTests
    {
        // --- the rig the game is actually played from ---------------------------------------------

        /// <summary>Metres back along the view ray. <see cref="MaxWorlds.CameraRig.FixedAngleCameraRig"/>
        /// ships 25.1 (YT-82).</summary>
        private const float CameraDistance = 25.1f;

        /// <summary>Vertical field of view of the Cinemachine lens in Backyard_Slice.</summary>
        private const float FovDegrees = 40f;

        /// <summary>The fixed top-down pitch (YT-33). A horizontal sway is seen at this angle, so only
        /// its component across the view ray lands on the screen — worst case, sin(72°).</summary>
        private const float PitchDegrees = 72f;

        private const int DesktopPixels = 1080;   // Lee's screen
        private const int PhonePixels = 750;      // a 6-inch phone in landscape — the non-negotiable

        /// <summary>Pixels per metre of world, measured across the view ray at the play distance.</summary>
        private static float PixelsPerMetre(int screenPixelHeight)
        {
            float worldHeightOnScreen = 2f * CameraDistance * Mathf.Tan(FovDegrees * 0.5f * Mathf.Deg2Rad);
            return screenPixelHeight / worldHeightOnScreen;
        }

        /// <summary>How far a point at <paramref name="plantHeight"/> travels, peak to peak, in metres.
        /// This mirrors StylizedSurface.Wind(): the bend works up over _WindHeight, and the gust is a
        /// slow 0.65 ± 0.35 multiplier — so a TYPICAL gust is 0.65, not 1.0. Modelling the peak here
        /// would flatter the wind by half again.</summary>
        private static float SwayMetres(float plantHeight, float windStrength, float bendHeight)
        {
            const float TypicalGust = 0.65f;
            float h = Mathf.Clamp01(plantHeight / bendHeight);
            float amplitude = windStrength * Mathf.Lerp(0.15f, 1f, h) * TypicalGust;
            return 2f * amplitude;   // sin() swings both ways
        }

        private static float SwayPixels(float plantHeight, int screenPixelHeight)
        {
            var foliage = MaterialLibrary.Surface(SurfaceKind.Foliage);
            float sway = SwayMetres(plantHeight,
                                    foliage.GetFloat("_WindStrength"),
                                    foliage.GetFloat("_WindHeight"));

            float acrossTheView = Mathf.Sin(PitchDegrees * Mathf.Deg2Rad);   // ~0.95
            return sway * PixelsPerMetre(screenPixelHeight) * acrossTheView;
        }

        [SetUp]
        public void SetUp()
        {
            MaterialLibrary.Clear();
            MaterialLibrary.Palette = BiomePalette.Backyard;
        }

        /// <summary>
        /// The yard is shrubs, tufts and flower beds — knee-high things. If THEY don't move, nothing
        /// the player looks at moves, whatever the trees are doing.
        /// </summary>
        [Test]
        public void AnOrdinaryShrub_VisiblySwaysOnLeesScreen()
        {
            float px = SwayPixels(plantHeight: 0.8f, DesktopPixels);

            Assert.That(px, Is.GreaterThan(6f),
                $"the top of a knee-high shrub travels {px:0.0} px at the play camera. That is the " +
                "wind the first cut of YT-78 shipped: it was 11 cm of sway normalised over 2.5 m of " +
                "height, so an actual bush reached about a pixel and a half of it, and the yard was " +
                "still. A wind you cannot see is not a wind.");

            Assert.That(px, Is.LessThan(40f),
                $"the shrub travels {px:0.0} px — it is not swaying, it is waving. Ambience that " +
                "pulls the eye off a telegraph has stopped being ambience (Craft Bible: juice serves " +
                "readability).");
        }

        /// <summary>Non-negotiable: readable on a 6-inch screen. A sway tuned on a monitor and lost on
        /// a phone is not done.</summary>
        [Test]
        public void TheSway_SurvivesA6InchPhone()
        {
            float px = SwayPixels(plantHeight: 0.8f, PhonePixels);

            Assert.That(px, Is.GreaterThan(4f),
                $"a shrub moves {px:0.0} px on a phone. The target platform is the one that decides.");
        }

        /// <summary>A tree bends more than a tuft — the taller thing has more lever. If the curve ever
        /// inverts, the wind is coming from somewhere other than the sky.</summary>
        [Test]
        public void TallerPlantsBendFurtherThanShortOnes()
        {
            Assert.That(SwayPixels(2.0f, DesktopPixels), Is.GreaterThan(SwayPixels(0.4f, DesktopPixels)));
        }

        /// <summary>
        /// The lawn's blades lean too, and the lawn is most of the screen — so this is most of whether
        /// the yard reads as alive. Same conversion, same reason: 6 cm of lean was under two pixels.
        /// </summary>
        [Test]
        public void TheLawnsLean_MovesEnoughGrassToSee()
        {
            float lean = MaterialLibrary.Surface(SurfaceKind.Ground).GetFloat("_WindStrength");

            // The ground shader shifts where the grass texture is SAMPLED, so the lean is a straight
            // world-space offset of the blade detail (StylizedGround.shader:209).
            float px = lean * PixelsPerMetre(DesktopPixels) * Mathf.Sin(PitchDegrees * Mathf.Deg2Rad);

            Assert.That(px, Is.GreaterThan(5f),
                $"the grass detail slides {px:0.0} px at the peak of a gust — under about five, the " +
                "lawn is a photograph.");

            Assert.That(px, Is.LessThan(25f),
                $"the grass detail slides {px:0.0} px. Past this the texture is no longer a blade " +
                "bending, it is the whole floor sliding under the player's feet.");
        }
    }
}
