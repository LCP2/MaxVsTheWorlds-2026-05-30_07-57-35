using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1292 — Shield Projector/Jammer placement across Worlds 3-5 (City, Reef, Mothership).
    /// Fails on a base commit that predates this ticket's edit — no area in any of the three
    /// configs authors a projector/jammer at all, so every count-based assertion below reads 0
    /// against a floor of 6/5. Tier 2 (resolved values): every count read through
    /// <see cref="WorldConfig.SolveComposition"/> — the runtime loader's own engine-resolved
    /// composition — never a raw JSON echo.
    ///
    /// ONE new EditMode test (MV-465 Rule 1) covering all three LOADED configs, per the ticket's
    /// own AC1: placement-rule checks (count, earliest index, boss/final exclusion, max gap) plus a
    /// proof that every OTHER kind's resolved count in every touched area is unchanged from the base
    /// commit. City/Mothership touched areas were already fully authored, exclusive compositions
    /// (no dial involved), so the baseline there is the literal per-kind counts captured from the
    /// base commit's JSON. Every Reef (world3_config.json) touched area is dial-blended — either it
    /// had no composition authored at all before this ticket, or (a6/a24) it already blended one
    /// authored-only extra kind (Dreadnought) onto the dial solve — so there the base-7 kinds are
    /// checked by re-deriving the dial solve independently (the same inputs
    /// <see cref="WorldConfig.SolveComposition"/> itself feeds <see cref="DifficultyEngine.SolveComposition"/>)
    /// rather than against a literal number, plus a6/a24's own pre-existing Dreadnought count is
    /// checked literally since that one field is itself authored, not dial-derived. The dial
    /// re-derivation specifically catches the regression this schema allows: a composition authored
    /// with the new kind but <see cref="WorldComposition.blendWithDial"/> left false would silently
    /// replace the area's entire dial-derived cohort instead of adding to it
    /// (<see cref="WorldConfig.SolveComposition"/>'s <c>IsAuthoredExclusive</c> branch) — exactly what
    /// "existing robots in those areas are unchanged" forbids.
    /// </summary>
    public sealed class MV1292ProjectorJammerPlacementTests
    {
        // MV-1477: the "every OTHER kind is unchanged from the base commit" baselines (Mothership literal counts, Reef
        // dial re-derivation) are gone - MV-1477 deliberately cut Worlds 3-5's garrisons (and with them the authored
        // compositions) by ~30%, so those counts are no longer the MV-1292 base. The placement rules stay.

        [Test]
        public void ProjectorAndJammerPlacement_MeetsEveryRule_AcrossReefAndMothership()
        {
            // MV-1430: the City no longer authors a Projector or Jammer (Lee, 23 Nov) - it is not checked here.
            CheckWorld(WorldLibrary.World3);
            CheckWorld(WorldLibrary.Mothership);
        }

        private static void CheckWorld(string worldKey)
        {
            WorldConfig cfg = WorldLibrary.Load(worldKey);
            Assert.IsNotNull(cfg, $"{worldKey}: must load and pass MapValidation (garrison placements must clear cover/sheds)");

            int finalIndex = cfg.areas.Max(a => a.index);
            var bossOrFinalIndices = new HashSet<int>(
                cfg.areas.Where(a => a.IsBossRole || a.index == finalIndex).Select(a => a.index));

            var projectorAreas = new List<int>();
            var jammerAreas = new List<int>();

            foreach (WorldArea a in cfg.areas)
            {
                DifficultyEngine.Composition solved = cfg.SolveComposition(a.index);
                bool hasEither = solved.Projector > 0 || solved.Jammer > 0;
                if (solved.Projector > 0) projectorAreas.Add(a.index);
                if (solved.Jammer > 0) jammerAreas.Add(a.index);

                if (!hasEither) continue;

                Assert.IsFalse(bossOrFinalIndices.Contains(a.index),
                    $"{worldKey} a{a.index}: never in a boss area or the world's final area");
                Assert.GreaterOrEqual(a.index, 4, $"{worldKey} a{a.index}: no earlier than area 4");
            }

            Assert.GreaterOrEqual(projectorAreas.Count, 6, $"{worldKey}: needs at least 6 Projector areas");
            Assert.GreaterOrEqual(jammerAreas.Count, 5, $"{worldKey}: needs at least 5 Jammer areas");

            List<int> union = projectorAreas.Concat(jammerAreas).Distinct().OrderBy(i => i).ToList();
            for (int i = 1; i < union.Count; i++)
            {
                Assert.LessOrEqual(union[i] - union[i - 1], 6,
                    $"{worldKey}: gap between area {union[i - 1]} and area {union[i]} exceeds 6");
            }
        }
    }
}
