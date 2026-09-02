using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-656 — Lee, from play: opening an area's gate woke its ENTIRE pre-placed garrison at once,
    /// with no sight or on-screen test at all, because <see cref="AreaAccumulationDirector.ActivateGarrisonFor"/>
    /// called <c>e.Activate()</c> right after <c>e.Retoughen(archetype)</c>. The fix removes that
    /// <c>Activate()</c> call; every garrison robot, on every path, now wakes only through its own
    /// <c>TickDormant -> AmbushWake.ShouldWake</c> check (MV363DormantRobotTests already covers that
    /// machinery and is not re-tested here).
    ///
    /// Tier 2 (resolved values): asserts <see cref="RobotEnemy.Current"/> stays Dormant and
    /// <see cref="RobotEnemy.HealthCurrent"/> reflects the live
    /// <see cref="DifficultyDirector.ToughnessMultiplier"/> after <c>ActivateGarrisonFor</c> runs over a
    /// pre-placed set — proving the retoughen half still happens while the wake half no longer does.
    /// Reuses <c>TwoAreaWorld</c>'s shape from MV514GarrisonHeadStartTests (stub -> a1 -> a2(garrisoned) -> boss).
    /// </summary>
    public sealed class MV656GarrisonStaysDormantOnGateBreakTests
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
        public void ActivateGarrisonFor_RetoughensButNeverWakes_ThePrePlacedGarrison()
        {
            WorldConfig cfg = TwoAreaWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            MapZone area2 = map.Zone("area2");
            Assert.IsNotNull(area2);

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>()); // FillArea(1) pre-places area 2's garrison, dormant

            var prePlaced = new System.Collections.Generic.List<(RobotEnemy robot, float healthAtPlacement)>();
            foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
            {
                Vector3 p = r.transform.position;
                if (area2.Contains(p.x, p.z)) prePlaced.Add((r, r.HealthCurrent));
            }
            Assert.Greater(prePlaced.Count, 0, "area 2 must actually have had a garrison pre-placed to check");

            // Escalate difficulty AFTER placement but BEFORE area 2's gate breaks - the same MV-514 trap:
            // proving the retoughen half still resolves off the multiplier live at wake time, not placement time.
            DifficultyDirector.Tick(DifficultyDirector.AuthoredRunLengthSeconds);
            float liveMultiplier = DifficultyDirector.ToughnessMultiplier;
            Assert.Greater(liveMultiplier, 1f, "the escalation must actually have moved for this test to prove anything");

            director.EnterArea(2); // area 2's gate breaks -> ActivateGarrisonFor runs over the pre-placed set

            foreach (var (robot, healthAtPlacement) in prePlaced)
            {
                Assert.AreEqual(RobotEnemy.State.Dormant, robot.Current,
                    "AC2: opening the gate must not by itself wake a pre-placed garrison member");
                Assert.AreEqual(healthAtPlacement * liveMultiplier, robot.HealthCurrent, 0.01f,
                    "the retoughen half of ActivateGarrisonFor must still resolve off the toughness multiplier live now");
            }
        }
    }
}
