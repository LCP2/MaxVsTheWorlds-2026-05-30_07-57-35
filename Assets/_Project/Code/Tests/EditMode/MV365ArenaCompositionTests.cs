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
            Assert.AreEqual(0, solved.Launcher);
            Assert.AreEqual(0, solved.Blinker);
        }

        /// <summary>MV-539 AC3 — the "bolter" composition key resolves all the way through
        /// <see cref="WorldComposition"/> → <see cref="DifficultyEngine.Composition"/> rather than being
        /// silently dropped the way an unknown key would be (MV-500's "bomber" incident).</summary>
        [Test]
        public void SolveComposition_ResolvesAnAuthoredBolterCount()
        {
            var cfg = new WorldConfig
            {
                dials = new WorldDials { areaCount = 1, baseThreat = 999f, threatGrowth = 5f, pacingRhythm = new[] { 1f } },
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "a1", index = 1, role = "normal",
                        composition = new WorldComposition { rusher = 2, bolter = 5 },
                    },
                },
            };

            DifficultyEngine.Composition solved = cfg.SolveComposition(1);

            Assert.AreEqual(2, solved.Rusher);
            Assert.AreEqual(5, solved.Bolter,
                "the 'bolter' composition key did not resolve through to the engine composition");
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
            var composition = new DifficultyEngine.Composition(rusher: 8, bruiser: 3, heavy: 1, brute: 0, gunner: 2, launcher: 1, blinker: 1);

            DifficultyEngine.Composition clamped = RusherCap.Apply(composition, alreadyUsed: 6);

            Assert.AreEqual(4, clamped.Rusher, "only 10-6=4 Rushers remain under the cap");
            Assert.AreEqual(3, clamped.Bruiser);
            Assert.AreEqual(1, clamped.Heavy);
            Assert.AreEqual(2, clamped.Gunner);
            Assert.AreEqual(1, clamped.Launcher);
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

        // MV-442 Part 4 (Lee, 2026-08-19 — supersedes the original MV-365 decision this test used to
        // pin): "an authored composition IS the answer for this area" now holds for the Rusher cap too
        // — RusherCap only ever trims a DIAL-DERIVED composition. An authored one queues in full no
        // matter how much of the run-wide cap it or its predecessors have already spent.

        [Test]
        public void RusherCap_NeverClampsAnAuthoredArea_EvenWhenTwoOfThemOverflowTheCapCombined_ThroughTheDirector()
        {
            WorldConfig cfg = TwoAreaRusherOverflowWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>());

            int afterArea1 = director.ActiveCount + director.QueuedCount;
            Assert.AreEqual(8, afterArea1, "area 1's 8 authored Rushers must queue in full");

            director.EnterArea(2);
            int afterArea2 = director.ActiveCount + director.QueuedCount;
            Assert.AreEqual(16, afterArea2,
                "area 2's 8 authored Rushers must ALSO queue in full — even though the combined " +
                "total (16) overflows the 10-per-level cap, an authored area is never trimmed");
        }

        [Test]
        public void RusherCap_StillClampsADialDerivedAreaFollowingAnAuthoredOne_ThroughTheDirector()
        {
            WorldConfig cfg = AuthoredThenDerivedWorld();

            // Confirm the test's own setup actually exercises the clamp: area 2's dial-derived solve
            // must want more Rushers than the cap has left after area 1's authored 8, or a pass here
            // wouldn't prove anything.
            DifficultyEngine.Composition unclampedArea2 = cfg.SolveComposition(2);
            Assert.Greater(unclampedArea2.Rusher, RusherCap.PerLevel - 8,
                "test setup must want more Rushers than the 2 left under the cap");

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>());

            int afterArea1 = director.ActiveCount + director.QueuedCount;
            Assert.AreEqual(8, afterArea1,
                "area 1's 8 authored Rushers must queue in full, still advancing the running Rusher " +
                "total so a later dial-derived area is capped correctly");

            director.EnterArea(2);
            int afterArea2 = director.ActiveCount + director.QueuedCount;

            int expectedClampedRusher = Mathf.Min(unclampedArea2.Rusher, RusherCap.PerLevel - 8);
            int expectedArea2Total = unclampedArea2.TotalCount - unclampedArea2.Rusher + expectedClampedRusher;

            Assert.AreEqual(afterArea1 + expectedArea2Total, afterArea2,
                "area 2 is dial-derived, not authored — RusherCap must still clamp its Rushers to " +
                $"whatever remains of the cap ({RusherCap.PerLevel - 8}) given the running total " +
                "area 1's authored Rushers already advanced it to");
        }

        [Test]
        public void RusherCap_Apply_ClampsToWhateverRemainsUnderThePerLevelCap()
        {
            var composition = new DifficultyEngine.Composition(rusher: 8, bruiser: 3, heavy: 0, brute: 0);
            DifficultyEngine.Composition clamped = RusherCap.Apply(composition, alreadyUsed: 6);

            Assert.AreEqual(4, clamped.Rusher, "only 4 Rushers remain under the 10-per-level cap once 6 are already used");
            Assert.AreEqual(3, clamped.Bruiser, "RusherCap only ever trims Rusher — every other kind passes through untouched");
        }

        /// <summary>The same shape as <see cref="TwoAreaRusherOverflowWorld"/>, but area 2 carries no
        /// authored composition (dial-derived) and a high enough <c>baseThreat</c> that its solve wants
        /// far more Rushers than the 2 left under the cap after area 1's authored 8 — proves
        /// <see cref="RusherCap"/> still clamps a dial-derived area even immediately after an authored
        /// one that was never trimmed.</summary>
        private static WorldConfig AuthoredThenDerivedWorld()
        {
            WorldConfig cfg = TwoAreaRusherOverflowWorld();
            cfg.areas[2].composition = null;
            cfg.dials.baseThreat = 30f;
            return cfg;
        }

        // ------------------------------------------------------------------- world1_config.json ACs

        private static WorldConfig LoadWorld1() => WorldLibrary.Load(WorldLibrary.World1);

        // --- AC1 (superseded by MV-564, v4's 30-area redraw): Arena 1 now holds 4 robots ------------

        [Test]
        public void World1_Area1HasExactlyFourRobots()
        {
            DifficultyEngine.Composition area1 = LoadWorld1().SolveComposition(1);
            Assert.AreEqual(4, area1.TotalCount);
        }

        // --- AC2/AC4: Arena 2 does not read as "more Rushers than Arena 1"; escalates via new kinds -

        [Test]
        public void World1_Area2EscalatesOverArea1()
        {
            // MV-568's original invariant ("area 2 must not simply add more Rushers than area 1") was
            // retired by Lee's V12 redraw (2026-09-01): area 2 now escalates via BOTH more Rushers (4->10)
            // AND a new kind (Blinker). The two checks that still hold are kept; the Rusher ceiling is not.
            WorldConfig cfg = LoadWorld1();
            DifficultyEngine.Composition area1 = cfg.SolveComposition(1);
            DifficultyEngine.Composition area2 = cfg.SolveComposition(2);

            Assert.Greater(area2.TotalCount, area1.TotalCount,
                "area 2 must still read as an escalation overall");
            Assert.Greater(area2.Blinker, 0, "area 2's growth still includes a new kind (Blinker)");
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

        // --- SUPERSEDED: MV-564's v4 redraw drops the centerDenial tag from area 5 (composition is ---
        // 4 Rusher + 4 Bolter with zero Launcher, so the tag was biasing a spawn kind the room never
        // authors) and no v4 area carries a scenario tag. World1_CenterDenialScenario_
        // HasABombardBarrageAndIsTaggedForPlacement removed with it.

        [Test]
        public void World1_BlinkerScenario_IsBuiltAroundTeleportingBlinkers()
        {
            // Area 7, per world1_config.json's authored composition and notes.
            DifficultyEngine.Composition area7 = LoadWorld1().SolveComposition(7);

            Assert.GreaterOrEqual(area7.Blinker, 4, "the Blinker set-piece needs enough Blinkers to read as built around them");
        }

        // --- SUPERSEDED DECISION: Rushers were briefly hard-capped at 10 across the whole (18-area, ---
        // --- MV-411) world, authoring-time and runtime alike. MV-442 Part 4 (Lee, 2026-08-19) --------
        // withdrew the authoring-time half of that after his a1/a4 redraw pushed world1's authored
        // total to 14: "I want the exact number of robots that I specify." An authored area (MV-365)
        // is now NEVER trimmed by RusherCap, at any point — see RusherCap_NeverClampsAnAuthoredArea_
        // EvenWhenTwoOfThemOverflowTheCapCombined_ThroughTheDirector above, which proves that directly.
        // RusherCap.Apply / AreaAccumulationDirector.ClampRusherCap still clamp a DIAL-DERIVED
        // composition (see RusherCap_StillClampsADialDerivedAreaFollowingAnAuthoredOne_ThroughTheDirector
        // above) — world1_config.json just doesn't have one, every one of its areas is authored.
        // MV-564's 30-area redraw raised the world total to 20; MV-568's a3 edit (3 -> 5 rusher) raised
        // it again to 22; MV-598's v9 designer redraw dropped a5's Rusher 4 -> 2, landing it at 20.

        [Test]
        public void World1_AuthoredRusherTotalExceedsThePerLevelCap_AndIsNeverTrimmedForIt()
        {
            WorldConfig cfg = LoadWorld1();

            int totalRushers = 0;
            for (int area = 1; area <= cfg.dials.areaCount; area++)
                totalRushers += cfg.SolveComposition(area).Rusher;

            Assert.AreEqual(23, totalRushers,
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
