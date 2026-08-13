using NUnit.Framework;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-353: the bottom readout used to show "SUB-ZONE n/1" beside "FACTORIES n/m". SUB-ZONE was a
    /// leftover from the pre-MV-242 single-arena slice, is not the same thing as an Area in the 10-area
    /// gated chain, and is permanently redundant with FACTORIES (it flips only at the instant
    /// FactoriesDestroyed reaches FactoriesTotal). It has been dropped from the HUD; FACTORIES is the
    /// only counter left, and it must keep tracking real destroyed/total counts correctly.
    /// </summary>
    public sealed class ArenaLabelTests
    {
        [Test]
        public void LabelNeverMentionsSubZone()
        {
            var a = new ArenaProgress(subZonesTotal: 1, factoriesTotal: 3);
            StringAssert.DoesNotContain("SUB-ZONE", HudController.ArenaLabelText(a));

            a.DestroyFactory();
            a.ClearSubZone();
            StringAssert.DoesNotContain("SUB-ZONE", HudController.ArenaLabelText(a));
        }

        [Test]
        public void LabelShowsFactoriesDestroyedOverTotal()
        {
            var a = new ArenaProgress(subZonesTotal: 1, factoriesTotal: 3);
            Assert.AreEqual("FACTORIES 0/3", HudController.ArenaLabelText(a));
        }

        [Test]
        public void LabelUpdatesAsRealFactoriesAreDestroyed()
        {
            // Mirrors how the live HUD discovers its total (HudModel.RegisterFactory) and advances it
            // (HudModel.RegisterFactoryDestroyed) — MV-242's chain currently registers three (Areas
            // 3/6/9), so the label must move for each one, not just flip once at the end.
            var a = new ArenaProgress(subZonesTotal: 1, factoriesTotal: 3);
            Assert.AreEqual("FACTORIES 0/3", HudController.ArenaLabelText(a));

            a.DestroyFactory();
            Assert.AreEqual("FACTORIES 1/3", HudController.ArenaLabelText(a));

            a.DestroyFactory();
            Assert.AreEqual("FACTORIES 2/3", HudController.ArenaLabelText(a));

            a.DestroyFactory();
            Assert.AreEqual("FACTORIES 3/3", HudController.ArenaLabelText(a));
        }
    }
}
