using NUnit.Framework;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-433/MV-445 — THE RIG board's scale-to-fit clamp. The chrome's visual assertions (backdrop
    /// opacity, region tint, node glow, connectors, labels, font sizes) were culled by MV-465 as
    /// EditMode appearance/presence tests; visual conformance is gated by the PNG-vs-spec harness
    /// instead. This one test survives because it asserts a pure numeric boundary on
    /// <see cref="WeaponsScreen.ComputeBoardScale"/> with no rendered object involved.
    /// </summary>
    public sealed class RigBoardChromeTests
    {
        /// <summary>
        /// What's actually enforced: the clamp itself never drops below <see cref="WeaponsScreen.BoardScaleFloor"/>
        /// (MV-445 defect 2: 0.83, was 0.9) at any aspect, however narrow.
        /// </summary>
        [Test]
        public void BoardScaleNeverDropsBelowItsOwnFloor()
        {
            float[] aspects = { 2.17f, 16f / 9f, 1.60f, 1.50f, 1.4f, 1.33f, 1.0f };
            foreach (float aspect in aspects)
                Assert.That(WeaponsScreen.ComputeBoardScale(aspect), Is.GreaterThanOrEqualTo(0.83f - 1e-4f), $"aspect {aspect}");
        }
    }
}
