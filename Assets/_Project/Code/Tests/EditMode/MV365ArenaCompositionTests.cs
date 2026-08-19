using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-365 — arena composition as designed scenarios, not a Rusher volume knob. Pinned against the
    /// ticket's own acceptance criteria. EditMode only (contract: PlayMode tests are never authored in
    /// this repo) — see <see cref="AreaAccumulationWorldConfigTests"/>'s note on why
    /// <see cref="AreaAccumulationDirector.ActiveCount"/>/<c>QueuedCount</c>, not
    /// <see cref="RobotEnemy.Active"/>, are what this class asserts against.
    /// </summary>
    public sealed class MV365ArenaCompositionTests
    {
        // ------------------------------------------------------------------- AC5: authored + tunable

        // --- AC5: composition is authored per area and tunable without a code change ----------------

        [Test]
        public void SolveComposition_ReturnsTheAuthoredCompositionVerbatim_NotADialSolve()
        {
            var cfg = new WorldConfig
            {
                dials = new WorldDials { areaCount = 1, baseThreat = 999f, threatGrowth = 5f, pacingRhythm = new[] { 1f } },
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "a1", index = 1, role = "normal",
                        composition = new WorldComposition { rusher = 2, bruiser = 1, gunner = 5 },
                    },
                },
            };

            DifficultyEngine.Composition solved = cfg.SolveComposition(1);

            // An enormous baseThreat/threatGrowth would blow this composition way up if the dial
            // solver ran at all — getting the authored numbers back exactly proves the short-circuit,
            // not a coincidence of the dials happening to agree.
            Assert.AreEqual(2, solved.Rusher);
            Assert.AreEqual(1, solved.Bruiser);
            Assert.AreEqual(5, solved.Gunner);
            Assert.AreEqual(0, solved.Heavy);
            Assert.AreEqual(0, solved.Brute);
            Assert.AreEqual(0, solved.Bomber);
            Assert.AreEqual(0, solved.Blinker);
        }

        [Test]
        public void SolveComposition_FallsBackToTheDialSolver_WhenNoCompositionIsAuthored()
        {
            var cfg = new WorldConfig
            {
                dials = new WorldDials
                {
                    areaCount = 1, baseThreat = 14f, threatGrowth = 0.1f, pacingRhythm = new[] { 1f },
                    toughnessCurve = new WorldToughnessCurve(),
                },
                areas = new[] { new WorldArea { id = "a1", index = 1, role = "normal" } },
            };

            DifficultyEngine.Composition solved = cfg.SolveComposition(1);

            Assert.Greater(solved.TotalCount, 0, "an un-authored area must still solve via the budget engine");
        }

        // ------------------------------------------------------------------- RusherCap pure math

        // --- DECISION (Lee, 13 Aug 2026): Rushers capped at 10 per level; not a floor on other kinds -

        [Test]
        public void RusherCap_LeavesCompositionUntouched_WhenUnderTheCap()
        {
            var composition = new DifficultyEngine.Composition(rusher: 4, bruiser: 2, heavy: 0, brute: 0);

            DifficultyEngine.Composition clamped = RusherCap.Apply(composition, alreadyUsed: 3);

            Assert.AreEqual(4, clamped.Rusher, "3 used + 4 more = 7, still under the 10 cap");
            Assert.AreEqual(2, clamped.Bruiser, "non-Rusher kinds are never touched by the cap");
        }

        [Test]
        public void RusherCap_ClampsRusherOnly_WhenOverTheCap()
        {
            var composition = new DifficultyEngine.Composition(rusher: 8, bruiser: 3, heavy: 1, brute: 0, gunner: 2, bomber: 1, blinker: 1);

            DifficultyEngine.Composition clamped = RusherCap.Apply(composition, alreadyUsed: 6);

            Assert.AreEqual(4, clamped.Rusher, "only 10-6=4 Rushers remain under the cap");
            Assert.AreEqual(3, clamped.Bruiser);
            Assert.AreEqual(1, clamped.Heavy);
            Assert.AreEqual(2, clamped.Gunner);
            Assert.AreEqual(1, clamped.Bomber);
            Assert.AreEqual(1, clamped.Blinker);
        }

        [Test]
        public void RusherCap_ReturnsZeroRushers_WhenTheCapIsAlreadySpent()
        {
            var composition = new DifficultyEngine.Composition(rusher: 5, bruiser: 0, heavy: 0, brute: 0);

            DifficultyEngine.Composition clamped = RusherCap.Apply(composition, alreadyUsed: RusherCap.PerLevel);

            Assert.AreEqual(0, clamped.Rusher);
        }

        // ------------------------------------------------------------------- AreaAccumulationDirector wiring

        private GameObject _directorGo;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);

            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
        }

        /// <summary>A minimal 2-combat-area world (entry -> a1 -> a2 -> boss, same stacked-rooms shape
        /// as <c>WorldMapLoaderTests.SmallValidWorld</c>) whose two areas each author more Rushers than
        /// the whole run's cap allows on their own — proves the cap is actually wired through
        /// <see cref="AreaAccumulationDirector.FillArea"/>, not just correct in isolation.</summary>
        private static WorldConfig TwoAreaRusherOverflowWorld()
        {
            return new WorldConfig
            {
                dials = new WorldDials { areaCount = 2, baseThreat = 1f, threatGrowth = 0f, pacingRhythm = new[] { 1f, 1f } },
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "stub", index = 0, role = "entry",
                        origin = new WorldAreaOrigin { x = -2f, z = -6f }, size = new WorldAreaSize { w = 4f, d = 6f },
                    },
                    new WorldArea
                    {
                        id = "a1", index = 1, role = "normal",
                        origin = new WorldAreaOrigin { x = -10f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                        composition = new WorldComposition { rusher = 8 },
                    },
                    new WorldArea
                    {
                        id = "a2", index = 2, role = "normal",
                        origin = new WorldAreaOrigin { x = -10f, z = 20f }, size = new WorldAreaSize { w = 20f, d = 20f },
                        composition = new WorldComposition { rusher = 8 },
                    },
                    new WorldArea
                    {
                        id = "boss", index = 3, role = "boss+exit",
                        origin = new WorldAreaOrigin { x = -10f, z = 40f }, size = new WorldAreaSize { w = 20f, d = 20f },
                    },
                },
                gates = new[]
                {
                    new WorldGate
                    {
                        id = "g0", width = 3f, opensWith = "start",
                        from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                    },
                    new WorldGate
                    {
                        id = "g1", width = 3f, opensWith = "primary",
                        from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "a2", wall = "S", pos = 0.5f },
                    },
                    new WorldGate
                    {
                        id = "bg", width = 3f, opensWith = "all-sheds-destroyed",
                        from = new WorldGateEndpoint { area = "a2", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                    },
                },
            };
        }

        [Test]
        public void RusherCap_ClampsCumulativeRusherAcrossAreas_ThroughTheDirector()
        {
            WorldConfig cfg = TwoAreaRusherOverflowWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>());

            int afterArea1 = director.ActiveCount + director.QueuedCount;
            Assert.AreEqual(8, afterArea1, "area 1's 8 authored Rushers are all under the 10-per-level cap");

            director.EnterArea(2);
            int afterArea2 = director.ActiveCount + director.QueuedCount;
            Assert.AreEqual(RusherCap.PerLevel, afterArea2,
                "area 2's authored 8 Rushers must clamp to whatever remains of the run-wide cap (2 " +
                "more, not 8) — the cap is CUMULATIVE across the whole run, not reset per area");
        }

        // ------------------------------------------------------------------- world1_config.json ACs

        private static WorldConfig LoadWorld1() => WorldLibrary.Load(WorldLibrary.World1);

        // --- AC1 (superseded by MV-442, Lee's 2026-08-19 redraw): Arena 1 now holds 5 robots --------

        [Test]
        public void World1_Area1HasExactlyFiveRobots()
        {
            DifficultyEngine.Composition area1 = LoadWorld1().SolveComposition(1);
            Assert.AreEqual(5, area1.TotalCount);
        }

        // --- AC2/AC4: Arena 2 does not read as "more Rushers than Arena 1"; escalates via new kinds -

        [Test]
        public void World1_Area2DoesNotAddMoreRushersThanArea1()
        {
            WorldConfig cfg = LoadWorld1();
            DifficultyEngine.Composition area1 = cfg.SolveComposition(1);
            DifficultyEngine.Composition area2 = cfg.SolveComposition(2);

            Assert.LessOrEqual(area2.Rusher, area1.Rusher,
                "area 2 must not simply add more Rushers than area 1 (AC2/AC4)");
            Assert.Greater(area2.TotalCount, area1.TotalCount,
                "area 2 must still read as an escalation overall, just not a Rusher one");
            Assert.Greater(area2.Gunner, 0, "area 2's growth comes from a new kind (Gunner), not Rusher volume");
        }

        // --- AC3: at least the three example scenarios exist, differing in kind, not just count -----

        [Test]
        public void World1_RangedPressureScenario_PairsRushersWithAboutFiveGunners()
        {
            // Area 4, per world1_config.json's authored composition and notes. MV-442 (Lee's
            // 2026-08-19 hedge-maze redraw) raised Area 4 from 2 Rusher to 5 — "a couple of Rushers"
            // is no longer the framing, the maze itself is what makes standing still costly now.
            DifficultyEngine.Composition area4 = LoadWorld1().SolveComposition(4);

            Assert.GreaterOrEqual(area4.Gunner, 4, "the ranged-pressure room needs ~5 Gunners");
            Assert.AreEqual(5, area4.Rusher, "MV-442 authored exactly 5 Rushers for area 4");
        }

        [Test]
        public void World1_CenterDenialScenario_HasABombardBarrageAndIsTaggedForPlacement()
        {
            // Area 5, per world1_config.json's authored composition, notes and scenario tag.
            WorldConfig cfg = LoadWorld1();
            WorldArea area5 = cfg.AreaByIndex(5);
            DifficultyEngine.Composition composition = cfg.SolveComposition(5);

            Assert.AreEqual("centerDenial", area5.scenario,
                "the missile-barrage room must be tagged so AreaAccumulationDirector biases Bomber " +
                "spawns toward the centre");
            Assert.Greater(composition.Bomber, 0, "the room needs an actual missile barrage");
            Assert.Greater(composition.TotalCount - composition.Bomber, 0,
                "the barrage must be 'surrounded by robots', not Bomber-only");
        }

        [Test]
        public void World1_BlinkerScenario_IsBuiltAroundTeleportingBlinkers()
        {
            // Area 7, per world1_config.json's authored composition and notes.
            DifficultyEngine.Composition area7 = LoadWorld1().SolveComposition(7);

            Assert.GreaterOrEqual(area7.Blinker, 4, "the Blinker set-piece needs enough Blinkers to read as built around them");
        }

        // --- DECISION: Rushers hard-capped at 10 across the whole (18-area, MV-411) world -----------
        // MV-442 (Lee's 2026-08-19 a1/a4 redraw) authored more Rushers (14) than fit under the cap
        // on their own — RusherCap.Apply, driven through AreaAccumulationDirector.FillArea, is what
        // actually keeps a live run under 10 (proved directly, with a synthetic overflow world, by
        // RusherCap_ClampsCumulativeRusherAcrossAreas_ThroughTheDirector above). Per MV-442's "Known
        // and accepted" note, authored totals exceeding a design target is expected and not something
        // to fix by trimming robots — this test now just pins the authored total as a drift tripwire.

        [Test]
        public void World1_TotalAuthoredRushers_ClampedLiveByTheDirectorNotByAuthoring()
        {
            WorldConfig cfg = LoadWorld1();

            int totalRushers = 0;
            for (int area = 1; area <= cfg.dials.areaCount; area++)
                totalRushers += cfg.SolveComposition(area).Rusher;

            Assert.AreEqual(14, totalRushers,
                "world1_config.json's authored Rusher total changed — if it dropped back under " +
                $"RusherCap.PerLevel ({RusherCap.PerLevel}), update this expectation to match");
        }

        // --- AC6: every arena remains completable with the new authored compositions/scenario tag ---

        [Test]
        public void World1_StillValidatesAfterAuthoredCompositions()
        {
            WorldConfig cfg = LoadWorld1();
            Assert.IsTrue(MapValidation.ValidateWorldConfig(cfg, out string reason), reason);

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string mapReason), mapReason);
            Assert.IsTrue(MapValidation.Validate(map, out string validateReason), validateReason);
        }
    }
}
