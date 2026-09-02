using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-514 — Lee, from play: robots in the NEXT area popped into existence the instant its gate
    /// broke, because nothing existed there before that moment (only a garrisoned shed area or area 1
    /// itself had anything standing). The fix: the moment area N is entered, area N+1's garrison is
    /// placed immediately, dormant — visible through the still-closed gate rather than materialising
    /// once it opens — and only RETOUGHENED when that gate actually breaks (MV-656 removed the wake
    /// that used to happen alongside it — a gate breaking is not a sight event), using WHATEVER
    /// <see cref="DifficultyDirector.ToughnessMultiplier"/> is live at that moment, not the one back
    /// when it was merely placed (the ticket's own "trap": freezing it at placement time would
    /// silently ease every area after the first as the run escalates).
    ///
    /// One test, two prongs, per the project's one-new-test policy: AC1 (pre-placed, dormant, not
    /// counted in the live cap) and AC2 (toughness resolved at retoughen time, not placement time)
    /// together are what stop the silent difficulty regression the ticket calls out as the real risk
    /// here; AC4 (area 2 specifically has robots before its gate opens) is exercised as the same case,
    /// area 2 being this test's own area N+1. AC3 (a dormant robot doesn't fire/chase/damage) is not
    /// re-tested — it is inherited unchanged from MV-363/MV-478's own already-covered Dormant/Activate
    /// state machine (MV363DormantRobotTests), which this ticket reuses rather than replaces. Whether
    /// the gate breaking itself wakes a pre-placed member is MV-656's own concern, covered there.
    ///
    /// EditMode only, same reflection-free idiom as AreaAccumulationDirectorGarrisonAndPlacementTests
    /// (drives the director through its own public API - Configure/EnterArea - no private Tick reflection
    /// needed here).
    /// </summary>
    public sealed class MV514GarrisonHeadStartTests
    {
        private GameObject _directorGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            DifficultyDirector.Reset();
            RobotEnemy.ResetRegistry();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            DifficultyDirector.Reset();
            DevTuning.Reset();
        }

        /// <summary>Same stub -> a1 -> a2 -> boss shape as MV417OverflowPlacementTests.TwoAreaWorld,
        /// with a2 given a garrison so its head start is exercisable.</summary>
        private static WorldConfig TwoAreaWorld()
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
                        composition = new WorldComposition { rusher = 2 },
                    },
                    new WorldArea
                    {
                        id = "a2", index = 2, role = "normal", garrisonDensity = "normal", // NormalShare=0.6 -> round(5*0.6)=3
                        origin = new WorldAreaOrigin { x = -10f, z = 20f }, size = new WorldAreaSize { w = 20f, d = 20f },
                        composition = new WorldComposition { rusher = 5 },
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
                        id = "g1", width = 3f, opensWith = "start",
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
        public void EnteringArea1_PrePlacesArea2sGarrisonDormant_AndOnlyToughensItWhenArea2sGateBreaks()
        {
            WorldConfig cfg = TwoAreaWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            MapZone area2 = map.Zone("area2");
            Assert.IsNotNull(area2);

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>()); // FillArea(1): a2 has not been entered

            // AC4 (pinned) / AC1: area 2's garrison already exists and is dormant, before its own gate
            // has ever broken - the exact "visible through the closed gate" case Lee reported.
            RobotEnemy[] beforeGateBreaks = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None);
            RobotEnemy pre = null;
            foreach (RobotEnemy r in beforeGateBreaks)
            {
                Vector3 p = r.transform.position;
                if (area2.Contains(p.x, p.z)) { pre = r; break; }
            }
            Assert.IsNotNull(pre, "area 2's garrison must already be standing there before its gate breaks");
            Assert.IsTrue(pre.IsDormant, "a pre-placed garrison robot must be dormant, not already fighting");

            // AC1: not counted in the live cap while dormant - area 1 (2 Rushers, no garrison) is the
            // only thing FillArea(1) should have released/queued so far.
            Assert.AreEqual(2, director.ActiveCount + director.QueuedCount,
                "a pre-placed dormant garrison must not add to the live queue's count until its area activates");

            float healthAtPlacement = pre.HealthCurrent;

            // Escalate the run's difficulty AFTER placement but BEFORE area 2's gate breaks - the
            // trap this ticket calls out: freezing toughness at placement time would miss this entirely.
            DifficultyDirector.Tick(DifficultyDirector.AuthoredRunLengthSeconds);
            float highMultiplier = DifficultyDirector.ToughnessMultiplier;
            Assert.Greater(highMultiplier, 1f, "the escalation must actually have moved for this test to prove anything");

            // Area 2's gate breaks (BackyardPath wires AreaGate.Opened -> EnterArea(nextArea) - this is
            // that same call, driven directly since this test exercises the director in isolation).
            director.EnterArea(2);

            // MV-656: the gate breaking is not itself a sight event, so this must stay Dormant - only
            // TickDormant/AmbushWake wakes it now, same as every other path (see
            // MV656GarrisonStaysDormantOnGateBreakTests). The retoughen half below is unchanged.
            Assert.IsTrue(pre.IsDormant, "opening the gate must not by itself wake a pre-placed garrison member");
            Assert.AreEqual(healthAtPlacement * highMultiplier, pre.HealthCurrent, 0.01f,
                "AC2: resolved health must reflect the multiplier live NOW, at retoughen time - not the one in " +
                "effect back when this robot was merely placed, dormant, a whole area earlier");
        }
    }
}
