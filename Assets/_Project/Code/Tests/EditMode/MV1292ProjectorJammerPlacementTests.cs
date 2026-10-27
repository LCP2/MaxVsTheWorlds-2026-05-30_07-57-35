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
        private sealed class ExpectedComposition
        {
            public int Rusher, Bruiser, Heavy, Brute, Gunner, Launcher, Blinker, Bolter, Lurker,
                       Sludger, Charger, Turret, Dreadnought, Skitter, Breaker, Tractor, Welder;
        }

        // City: every touched area already carried a full, exclusive (non-blended) composition --
        // literal per-kind counts captured from the base commit (pre-MV-1292).
        private static readonly Dictionary<int, ExpectedComposition> CityBaseline = new Dictionary<int, ExpectedComposition>
        {
            [4] = new ExpectedComposition { Rusher = 10, Bruiser = 11, Turret = 2 },
            [6] = new ExpectedComposition { Rusher = 10, Bruiser = 5, Gunner = 7, Turret = 2 },
            [9] = new ExpectedComposition { Blinker = 2, Rusher = 10, Gunner = 1, Turret = 2, Bruiser = 5, Heavy = 3, Bolter = 2 },
            [11] = new ExpectedComposition { Rusher = 15, Bruiser = 3, Turret = 4, Bolter = 2, Launcher = 3, Gunner = 2, Heavy = 2, Brute = 4 },
            [14] = new ExpectedComposition { Brute = 1, Rusher = 11, Launcher = 1, Turret = 3, Bolter = 4, Gunner = 2, Bruiser = 2, Blinker = 1, Heavy = 1 },
            [16] = new ExpectedComposition { Rusher = 11, Turret = 3, Launcher = 2, Brute = 2, Gunner = 2, Heavy = 4, Bolter = 3 },
            [19] = new ExpectedComposition { Launcher = 3, Rusher = 16, Turret = 4, Bruiser = 5, Gunner = 2, Brute = 3, Blinker = 3, Bolter = 1, Heavy = 1 },
            [21] = new ExpectedComposition { Rusher = 12, Gunner = 5, Heavy = 2, Brute = 3, Blinker = 2, Turret = 3, Bruiser = 1, Launcher = 1 },
            [24] = new ExpectedComposition { Rusher = 17, Gunner = 6, Turret = 4, Blinker = 3, Launcher = 3, Bruiser = 2, Brute = 1, Bolter = 4 },
            [26] = new ExpectedComposition { Rusher = 13, Heavy = 2, Blinker = 4, Brute = 2, Turret = 3, Bolter = 1, Launcher = 4, Gunner = 1 },
            [29] = new ExpectedComposition { Gunner = 3, Launcher = 4, Blinker = 4, Rusher = 17, Bolter = 2, Turret = 4, Brute = 3, Bruiser = 3, Heavy = 1 },
        };

        // Mothership: same footing as City -- every touched area was already a full, exclusive
        // composition before this ticket.
        private static readonly Dictionary<int, ExpectedComposition> MothershipBaseline = new Dictionary<int, ExpectedComposition>
        {
            [4] = new ExpectedComposition { Rusher = 7, Turret = 3, Gunner = 4, Bruiser = 4, Launcher = 2, Blinker = 2 },
            [6] = new ExpectedComposition { Rusher = 6, Turret = 3, Gunner = 3, Bruiser = 3, Launcher = 2, Blinker = 2, Bolter = 2, Heavy = 2, Skitter = 3 },
            [9] = new ExpectedComposition { Rusher = 6, Turret = 3, Gunner = 3, Bruiser = 3, Launcher = 2, Blinker = 2, Bolter = 2, Heavy = 2, Skitter = 3, Brute = 2 },
            [11] = new ExpectedComposition { Rusher = 5, Turret = 2, Gunner = 3, Bruiser = 3, Launcher = 2, Blinker = 2, Bolter = 2, Heavy = 2, Skitter = 2, Brute = 1 },
            [14] = new ExpectedComposition { Rusher = 6, Turret = 2, Gunner = 3, Bruiser = 3, Launcher = 2, Blinker = 2, Bolter = 2, Heavy = 2, Skitter = 2, Brute = 2 },
            [16] = new ExpectedComposition { Rusher = 6, Turret = 3, Gunner = 3, Bruiser = 3, Launcher = 2, Blinker = 2, Bolter = 2, Heavy = 2, Skitter = 3, Brute = 2, Tractor = 2 },
            [19] = new ExpectedComposition { Rusher = 7, Turret = 3, Gunner = 4, Bruiser = 4, Launcher = 2, Blinker = 2, Bolter = 2, Heavy = 2, Skitter = 3, Brute = 2, Breaker = 1 },
            [21] = new ExpectedComposition { Rusher = 8, Turret = 3, Gunner = 4, Bruiser = 4, Launcher = 3, Blinker = 3, Bolter = 2, Heavy = 2, Skitter = 3, Brute = 2, Breaker = 2 },
            [24] = new ExpectedComposition { Rusher = 8, Turret = 3, Gunner = 4, Bruiser = 4, Launcher = 3, Blinker = 3, Bolter = 3, Heavy = 3, Skitter = 3, Brute = 2, Breaker = 2, Welder = 2 },
            [26] = new ExpectedComposition { Rusher = 9, Turret = 3, Gunner = 4, Bruiser = 4, Launcher = 3, Blinker = 3, Bolter = 3, Heavy = 3, Skitter = 3, Brute = 3, Breaker = 2 },
            [29] = new ExpectedComposition { Rusher = 9, Turret = 3, Gunner = 4, Bruiser = 4, Launcher = 3, Blinker = 3, Bolter = 3, Heavy = 3, Skitter = 3, Brute = 3, Breaker = 2 },
        };

        // Reef (world3_config.json): every touched area is dial-blended (either it had no
        // composition authored at all before this ticket, or -- a6/a24 -- it already blended one
        // authored-only extra kind onto the dial solve). So the base-7 kinds are checked by
        // re-deriving the dial solve for ALL eleven below, never against a literal baseline; only
        // a6/a24's own pre-existing Dreadnought needs a literal check, since that one field is
        // itself authored, not dial-derived.
        private static readonly Dictionary<int, int> World3DreadnoughtBaseline = new Dictionary<int, int>
        {
            [6] = 1,
            [24] = 1,
        };

        [Test]
        public void ProjectorAndJammerPlacement_MeetsEveryRule_AcrossAllThreeWorlds()
        {
            CheckWorld(WorldLibrary.City, CityBaseline, isDialBlended: false);
            CheckWorld(WorldLibrary.World3, null, isDialBlended: true);
            CheckWorld(WorldLibrary.Mothership, MothershipBaseline, isDialBlended: false);
        }

        private static void CheckWorld(string worldKey, Dictionary<int, ExpectedComposition> literalBaseline, bool isDialBlended)
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

                if (isDialBlended)
                {
                    AssertDialDerivedKindsUnchanged(worldKey, cfg, a.index, solved);
                    if (World3DreadnoughtBaseline.TryGetValue(a.index, out int expectedDreadnought))
                        Assert.AreEqual(expectedDreadnought, solved.Dreadnought, $"{worldKey} a{a.index}: Dreadnought count must be unchanged");
                }
                else if (literalBaseline.TryGetValue(a.index, out ExpectedComposition expected))
                {
                    AssertOtherKindsUnchanged(worldKey, a.index, expected, solved);
                }
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

        private static void AssertOtherKindsUnchanged(string worldKey, int areaIndex, ExpectedComposition expected, DifficultyEngine.Composition actual)
        {
            string ctx = $"{worldKey} a{areaIndex}";
            Assert.AreEqual(expected.Rusher, actual.Rusher, $"{ctx}: Rusher count must be unchanged");
            Assert.AreEqual(expected.Bruiser, actual.Bruiser, $"{ctx}: Bruiser count must be unchanged");
            Assert.AreEqual(expected.Heavy, actual.Heavy, $"{ctx}: Heavy count must be unchanged");
            Assert.AreEqual(expected.Brute, actual.Brute, $"{ctx}: Brute count must be unchanged");
            Assert.AreEqual(expected.Gunner, actual.Gunner, $"{ctx}: Gunner count must be unchanged");
            Assert.AreEqual(expected.Launcher, actual.Launcher, $"{ctx}: Launcher count must be unchanged");
            Assert.AreEqual(expected.Blinker, actual.Blinker, $"{ctx}: Blinker count must be unchanged");
            Assert.AreEqual(expected.Bolter, actual.Bolter, $"{ctx}: Bolter count must be unchanged");
            Assert.AreEqual(expected.Lurker, actual.Lurker, $"{ctx}: Lurker count must be unchanged");
            Assert.AreEqual(expected.Sludger, actual.Sludger, $"{ctx}: Sludger count must be unchanged");
            Assert.AreEqual(expected.Charger, actual.Charger, $"{ctx}: Charger count must be unchanged");
            Assert.AreEqual(expected.Turret, actual.Turret, $"{ctx}: Turret count must be unchanged");
            Assert.AreEqual(expected.Dreadnought, actual.Dreadnought, $"{ctx}: Dreadnought count must be unchanged");
            Assert.AreEqual(expected.Skitter, actual.Skitter, $"{ctx}: Skitter count must be unchanged");
            Assert.AreEqual(expected.Breaker, actual.Breaker, $"{ctx}: Breaker count must be unchanged");
            Assert.AreEqual(expected.Tractor, actual.Tractor, $"{ctx}: Tractor count must be unchanged");
            Assert.AreEqual(expected.Welder, actual.Welder, $"{ctx}: Welder count must be unchanged");
        }

        /// <summary>Re-derives the dial-only solve for <paramref name="areaIndex"/> with the exact same
        /// inputs <see cref="WorldConfig.SolveComposition"/> itself feeds <see cref="DifficultyEngine.SolveComposition"/>,
        /// then asserts the loaded config's base-7 kinds match it. Only reached for an area this
        /// ticket authored a brand-new <c>blendWithDial</c> composition onto (no literal baseline
        /// exists because none was authored before) — if blending had silently been skipped in favour
        /// of the exclusive-replace branch, every one of these would read back as 0 instead.</summary>
        private static void AssertDialDerivedKindsUnchanged(string worldKey, WorldConfig cfg, int areaIndex, DifficultyEngine.Composition actual)
        {
            WorldDials dials = cfg.dials;
            float baseThreat = DevTuning.Or(DevTuning.WorldBaseThreat, dials.baseThreat);
            float threatGrowth = DevTuning.Or(DevTuning.WorldThreatGrowth, dials.threatGrowth);
            float budget = DifficultyEngine.TargetBudget(areaIndex, baseThreat, threatGrowth, dials.EnginePacing);
            ToughnessCurve toughness = dials.toughnessCurve?.ToEngineCurve(dials.areaCount);
            DifficultyEngine.Composition dialSolved = DifficultyEngine.SolveComposition(areaIndex, budget, toughness);

            string ctx = $"{worldKey} a{areaIndex}";
            Assert.AreEqual(dialSolved.Rusher, actual.Rusher, $"{ctx}: dial-derived Rusher count must survive the blend");
            Assert.AreEqual(dialSolved.Bruiser, actual.Bruiser, $"{ctx}: dial-derived Bruiser count must survive the blend");
            Assert.AreEqual(dialSolved.Heavy, actual.Heavy, $"{ctx}: dial-derived Heavy count must survive the blend");
            Assert.AreEqual(dialSolved.Brute, actual.Brute, $"{ctx}: dial-derived Brute count must survive the blend");
            Assert.AreEqual(dialSolved.Gunner, actual.Gunner, $"{ctx}: dial-derived Gunner count must survive the blend");
            Assert.AreEqual(dialSolved.Launcher, actual.Launcher, $"{ctx}: dial-derived Launcher count must survive the blend");
            Assert.AreEqual(dialSolved.Blinker, actual.Blinker, $"{ctx}: dial-derived Blinker count must survive the blend");
        }
    }
}
