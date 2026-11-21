using NUnit.Framework;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-433/MV-445/MV-472 — THE RIG board's scale-to-fit clamp and, as of MV-472, its two numeric
    /// legibility/clipping floors. Most of the chrome's visual assertions (backdrop opacity, region
    /// tint, node glow, connectors) were culled by MV-465 as EditMode appearance/presence tests, gated
    /// by the PNG-vs-spec harness instead — but MV-472's REGRESSION NOTE explicitly restores the two
    /// tests below and exempts them from any future cull: both assert a pure numeric measurement
    /// (on-screen point size, ref-px window bounds) with no rendered object involved, not an appearance
    /// judgement, so Rule 2/3 of the testing policy (MV-465) doesn't apply to them.
    /// </summary>
    public sealed class RigBoardChromeTests
    {
        /// <summary>
        /// What's actually enforced: the clamp itself never drops below <see cref="WeaponsScreen.BoardScaleFloor"/>
        /// (MV-472: 0.70, was MV-445's 0.83 — see that constant's own doc comment for why) at any aspect,
        /// however narrow.
        /// </summary>
        [Test]
        public void BoardScaleNeverDropsBelowItsOwnFloor()
        {
            float[] aspects = { 2.17f, 16f / 9f, 1.60f, 1.50f, 1.4f, 1.33f, 1.0f };
            foreach (float aspect in aspects)
                Assert.That(WeaponsScreen.ComputeBoardScale(aspect), Is.GreaterThanOrEqualTo(0.70f - 1e-4f), $"aspect {aspect}");
        }

        // ------------------------------------------------------------------ MV-472 AC1: the 44pt/11pt floors, MET

        // ------------------------------------------------------------------ MV-472 AC1: nothing clips, at every tested aspect

    }
}
