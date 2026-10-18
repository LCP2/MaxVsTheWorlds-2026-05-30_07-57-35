using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1235 — <see cref="AreaAccumulationDirector.FillArea"/> read <see cref="WorldComposition.IsAuthored"/>
    /// as "the dial was not involved" and skipped <see cref="RusherCap"/> for any area that authors
    /// anything at all, including one that opts into <see cref="WorldComposition.blendWithDial"/>
    /// (MV-1220) — whose Rusher/Bruiser/... counts ARE the dial's own solve (<see cref="WorldComposition.BlendOnto"/>
    /// keeps every one of them), not hand-authored. MV-442's own rationale ("RusherCap only ever applies
    /// to a dial-derived solve") says the cap belongs here.
    ///
    /// One EditMode test (MV-465 Rule 1), asserting the RESOLVED queued-Rusher running total
    /// (<c>_rushersQueuedThisLevel</c>, Tier 2) — never <see cref="RusherCap.PerLevel"/> itself as a
    /// bare self-assertion (Tier 1); it is only ever used here as the expected value a resolved count is
    /// compared against, the same idiom <see cref="MV365ArenaCompositionTests"/>'s own director-level
    /// RusherCap tests already use.
    ///
    /// AC2 (an area with a fully authored composition and no <c>blendWithDial</c> still skips the cap,
    /// MV-442 unchanged) is already covered by
    /// <see cref="MV365ArenaCompositionTests.RusherCap_NeverClampsAnAuthoredArea_EvenWhenTwoOfThemOverflowTheCapCombined_ThroughTheDirector"/>
    /// — not duplicated here.
    /// </summary>
    public sealed class MV1235RusherCapBlendWithDialTests
    {
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

        private static int RushersQueuedThisLevel(AreaAccumulationDirector director) =>
            (int)typeof(AreaAccumulationDirector)
                .GetField("_rushersQueuedThisLevel", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(director);

        /// <summary>One combat area (a1) whose authored composition opts into
        /// <see cref="WorldComposition.blendWithDial"/> ({dreadnought: 1}) on top of a dial solve tuned
        /// (baseThreat 22, no toughness curve so tank-share/specials are all off, same "null toughness"
        /// shape <see cref="MV365ArenaCompositionTests.TwoAreaRusherOverflowWorld"/> uses) to want 12
        /// Rushers: <see cref="DifficultyEngine.SolveComposition"/>'s light-purse split gives
        /// bruiser = round(22*0.5/2.5) = round(4.4) = 4, then rusher = round(22 - 4*2.5) = round(12) = 12
        /// — the same arithmetic the ticket's own Reef measurements use, picked so the dial Rusher count
        /// (12) exceeds <see cref="RusherCap.PerLevel"/> (10) on a single, otherwise-empty run.</summary>
        private static WorldConfig OneBlendedAreaWorld()
        {
            return new WorldConfig
            {
                dials = new WorldDials { areaCount = 1, baseThreat = 22f, threatGrowth = 0f, pacingRhythm = new[] { 1f } },
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "stub", index = 0, role = "entry",
                        origin = new WorldAreaOrigin { x = -2f, z = -6f }, size = new WorldAreaSize { w = 4f, d = 6f },
                    },
                    new WorldArea
                    {
                        id = "a1", index = 1, role = "normal", garrisonDensity = "none",
                        origin = new WorldAreaOrigin { x = -10f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                        composition = new WorldComposition { dreadnought = 1, blendWithDial = true },
                    },
                    new WorldArea
                    {
                        id = "boss", index = 2, role = "boss+exit",
                        origin = new WorldAreaOrigin { x = -10f, z = 20f }, size = new WorldAreaSize { w = 20f, d = 20f },
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
                        id = "bg", width = 3f, opensWith = "all-sheds-destroyed",
                        from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                    },
                },
            };
        }

        [Test]
        public void FillArea_StillCapsRushers_WhenTheAuthoredCompositionOptsIntoBlendWithDial()
        {
            WorldConfig cfg = OneBlendedAreaWorld();

            DifficultyEngine.Composition unclamped = cfg.SolveComposition(1);
            Assert.Greater(unclamped.Rusher, RusherCap.PerLevel,
                "test setup must want more dial Rushers than the cap allows, or a pass here proves nothing");

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>());

            int queuedRushers = RushersQueuedThisLevel(director);
            Assert.AreEqual(RusherCap.PerLevel, queuedRushers,
                "a blendWithDial area's Rusher count IS the dial-derived solve (MV-1220's own BlendOnto " +
                "keeps dialSolved.Rusher), so it must still be capped like any other dial-derived area, " +
                "not read as exempt just because it authors something else (here, dreadnought=1) — " +
                $"queued {queuedRushers} of an uncapped dial solve of {unclamped.Rusher}");
        }
    }
}
