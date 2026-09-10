using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-764: World 2 authored a "pipe" dressing word and World 3 authored "crate" — neither is a
    /// <see cref="CoverDressing"/> member, so <see cref="MapEnums.Dressing"/> silently fell every one
    /// of those pieces back to <see cref="CoverDressing.None"/> (MapData.cs:349, 371-376). Fails on the
    /// MV-755 merge commit (a63619c) — world2_config.json authors "pipe" on 21 cover pieces and
    /// world3_config.json authors "crate" on 41, so every one of those resolves to None and World 2's
    /// cover reads as a single repeated silt-sack prop.
    /// </summary>
    public sealed class MV764CoverVocabularyTests
    {
        /// <summary>Mirrors <c>MapEnums.Parse</c>'s cleaning rule (strip separators, case-insensitive)
        /// so this test can tell "authored none/empty" (a valid, deliberate idiom) apart from "authored
        /// a word the enum has never heard of" (a typo silently swallowed to the same fallback) without
        /// reaching into the production parser's private method.</summary>
        private static bool ResolvesToRealMember(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return true; // documented "not authored" idiom
            string cleaned = raw.Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);
            return Enum.TryParse(cleaned, ignoreCase: true, out CoverDressing _);
        }

        private static IEnumerable<(WorldArea area, WorldCover cover)> AllCover(WorldConfig cfg)
        {
            foreach (WorldArea area in cfg.areas)
            foreach (WorldCover cover in area.cover)
                yield return (area, cover);
        }

        [Test]
        public void CoverVocabulary_ResolvesCleanly_AndWorld2SpreadsAcrossKinds()
        {
            // Every config under Resources/Worlds/: every authored dressing string must resolve to a
            // real CoverDressing member (fails today on "pipe" x21 in World 2 and "crate" x41 in World 3).
            foreach (string key in WorldLibrary.Keys)
            {
                WorldConfig cfg = WorldLibrary.Load(key);
                Assert.IsNotNull(cfg, $"{key} failed to load");

                var badPieces = AllCover(cfg)
                    .Where(t => !ResolvesToRealMember(t.cover.dressing))
                    .Select(t => $"{t.area.id}/{t.cover.id}=\"{t.cover.dressing}\"")
                    .ToArray();

                Assert.IsEmpty(badPieces,
                    $"{key} authors a dressing string the enum has never heard of: {string.Join(", ", badPieces)}");
            }

            WorldConfig world2 = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(world2, "world2_config.json failed to load");

            var resolved = AllCover(world2)
                .Select(t => (t.area, dressing: MapEnums.Dressing(t.cover.dressing)))
                .ToArray();

            // Distinct kinds and per-area variety.
            HashSet<CoverDressing> distinctKinds = resolved.Select(t => t.dressing).ToHashSet();
            Assert.GreaterOrEqual(distinctKinds.Count, 4,
                $"World 2 must span at least 4 distinct CoverDressing values, got {distinctKinds.Count}: {string.Join(", ", distinctKinds)}");

            var monotoneAreas = resolved
                .GroupBy(t => t.area.id)
                .Where(g => g.Count() >= 3 && g.Select(t => t.dressing).Distinct().Count() == 1)
                .Select(g => g.Key)
                .ToArray();

            Assert.IsEmpty(monotoneAreas,
                $"areas with 3+ cover pieces must not resolve to a single kind: {string.Join(", ", monotoneAreas)}");

            // Sightline guard: the Hedge set must be exactly the four see-through screen pieces, as an
            // id-set assertion, not a count.
            HashSet<string> hedgeIds = AllCover(world2)
                .Where(t => MapEnums.Dressing(t.cover.dressing) == CoverDressing.Hedge)
                .Select(t => t.cover.id)
                .ToHashSet();

            var expectedHedgeIds = new HashSet<string> { "a2_cover3", "a10_cover1", "a10_cover2", "a10_cover3" };
            Assert.AreEqual(expectedHedgeIds, hedgeIds,
                $"World 2's Hedge (see-through) set must be exactly the sightline rooms, got: {string.Join(", ", hedgeIds)}");
        }
    }
}
