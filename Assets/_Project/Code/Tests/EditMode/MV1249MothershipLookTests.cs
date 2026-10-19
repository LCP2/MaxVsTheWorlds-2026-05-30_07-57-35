using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1249 (the one new test, per CC_AUTONOMY's testing policy): World 5, the Mothership, gets its
    /// own lighting — one <see cref="BackyardLook"/> per deck (Hangar/Assembly/Holds/Engineering/
    /// Command), instead of reusing the Reef's <see cref="BackyardLook.Default"/> (Lee, 19 Oct 2026,
    /// "this world is very bland at the moment").
    ///
    /// Asserts RESOLVED values only (Testing Policy v2, Tier 2): the catalog row's own resolved
    /// <see cref="MaxWorlds.Arena.WorldDefinition.Palette"/>, and <see cref="MothershipDecks.LookFor"/>'s
    /// real area-index -> look resolution for one area in each deck range — never an authored struct
    /// literal re-read against itself.
    ///
    /// Fails on base commit a374e94 (before this ticket): <c>BiomePalette</c> has no <c>Ship</c> member
    /// and <c>MaxWorlds.Arena.MothershipDecks</c> does not exist, so this fails to COMPILE (CS0117 /
    /// CS0234) — confirmed via <c>cc-test.bat</c> on base-plus-test-only:
    /// "error CS0117: 'BiomePalette' does not contain a definition for 'Ship'"
    /// "error CS0234: The type or namespace name 'MothershipDecks' does not exist in the namespace 'MaxWorlds.Arena'"
    /// </summary>
    public sealed class MV1249MothershipLookTests
    {
        /// <summary>Rec. 709 perceptual luminance — the same weights <c>RigBoardConformance.MeanLuminance</c>
        /// and <c>FrameContrastGate.Check</c> already use elsewhere in this codebase.</summary>
        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>The one new test this ticket is allowed (CC_AUTONOMY Rule 1): AC1 in full — the
        /// Mothership row resolves <see cref="BiomePalette.Ship"/>, and one representative area per the
        /// ticket's own deck range (Hangar 1-5, Assembly 6-12, Holds 13-18, Engineering 19-24, Command
        /// 25-30) resolves a DIFFERENT <see cref="BackyardLook"/> whose ambient sky luminance stays under
        /// 0.15 — "dark ship, not lavender" (ticket's own "do not re-raise").</summary>
        [Test]
        public void MothershipResolvesShipPaletteAndADistinctDarkLookPerDeck()
        {
            int index = WorldCatalog.IndexOf(WorldIds.Mothership);
            WorldDefinition row = WorldCatalog.Get(index);
            Assert.IsTrue(row.Palette.Equals(BiomePalette.Ship),
                "the Mothership's own row must resolve BiomePalette.Ship, not the Reef's borrowed palette");

            // One representative area per the ticket's own deck ranges.
            BackyardLook[] decks =
            {
                MothershipDecks.LookFor(1),    // Hangar
                MothershipDecks.LookFor(8),    // Assembly
                MothershipDecks.LookFor(15),   // Holds
                MothershipDecks.LookFor(22),   // Engineering
                MothershipDecks.LookFor(30),   // Command
            };
            string[] names = { "Hangar", "Assembly", "Holds", "Engineering", "Command" };

            for (int i = 0; i < decks.Length; i++)
            {
                float lum = Luminance(decks[i].AmbientSky);
                Assert.That(lum, Is.LessThan(0.15f),
                    $"{names[i]} deck's resolved AmbientSky luminance ({lum:F4}) must stay under 0.15");
            }

            for (int i = 0; i < decks.Length; i++)
                for (int j = i + 1; j < decks.Length; j++)
                    Assert.IsFalse(decks[i].Equals(decks[j]),
                        $"{names[i]} and {names[j]} must resolve to different BackyardLook values");
        }
    }
}
