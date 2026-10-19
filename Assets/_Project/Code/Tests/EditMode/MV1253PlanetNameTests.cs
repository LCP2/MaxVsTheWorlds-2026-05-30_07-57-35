using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Intro;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1253 (the one new test, per CC_AUTONOMY's testing policy): running the end shot to its
    /// "TO BE CONTINUED" step must show a text element whose resolved string is "SOTAMI", and that
    /// string must come from <see cref="ObservationDeckSequence.PlanetName"/> rather than a second,
    /// divergent literal -- same idiom <c>CaptureDirector</c>'s own MV-1245 preset already uses to
    /// build the overlay standalone (<see cref="ObservationDeckSequence.EndShotOverlay.Create"/>)
    /// without replaying the whole finale.
    ///
    /// Fails on base commit (pre-MV-1253): <c>PlanetName</c> does not exist yet, and the end shot
    /// overlay carries no caption text beside the planet at all.
    /// </summary>
    public sealed class MV1253PlanetNameTests
    {
        [Test]
        public void EndShot_ToBeContinued_ShowsPlanetNameCaption_DrivenByPlanetNameConstant()
        {
            ObservationDeckSequence.EndShotOverlay overlay = null;
            try
            {
                overlay = ObservationDeckSequence.EndShotOverlay.Create();
                overlay.ShowToBeContinued();

                Assert.IsNotNull(overlay.PlanetNameText,
                    "the end shot must carry a text element for the planet's name caption");
                Assert.AreEqual(ObservationDeckSequence.PlanetName, overlay.PlanetNameText.text,
                    "the caption's resolved string must come from PlanetName, not a duplicated literal");
                Assert.AreEqual("SOTAMI", overlay.PlanetNameText.text,
                    "Lee, 19 Oct: the planet's name is SOTAMI");
            }
            finally
            {
                overlay?.DestroySelf();
            }
        }
    }
}
