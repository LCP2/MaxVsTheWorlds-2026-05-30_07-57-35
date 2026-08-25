using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-559 — before this ticket, <see cref="WorldArea"/> could author HOW MANY robots a garrison
    /// has but never WHERE any specific one of them stands: <see cref="Garrison.SeedPositions"/> only
    /// ever produced an even ring, and <see cref="AreaAccumulationDirector"/>'s garrison placement took
    /// whatever kind the spawn queue handed back next for each ring slot. This test proves the one
    /// thing that actually changed: an authored <see cref="WorldArea.garrison"/> entry lands its exact
    /// KIND at its exact authored coordinate, still dormant, and any seed slots beyond the authored
    /// entries still fill from the ring exactly as before (same idiom as
    /// AreaAccumulationDirectorGarrisonAndPlacementTests — no reflection needed, drives the director
    /// through Configure).
    /// </summary>
    public sealed class MV559AuthoredGarrisonPositionsTests
    {
        private GameObject _directorGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
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
            DevTuning.Reset();
        }

        [Test]
        public void FillArea_PlacesTheAuthoredKindAtTheAuthoredSpot_AndFillsTheRestFromTheRing()
        {
            // a1: 20x20, composition {rusher:4, blinker:1}, garrisonDensity "normal" (share 0.6) ->
            // SeedCount = round(5 * 0.6) = 3. One authored garrison entry (the sole authored blinker)
            // plus 2 ring-fallback slots for the remaining rushers.
            var cfg = new WorldConfig
            {
                dials = new WorldDials { areaCount = 1, baseThreat = 1f, threatGrowth = 0f, pacingRhythm = new[] { 1f } },
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "stub", index = 0, role = "entry",
                        origin = new WorldAreaOrigin { x = -2f, z = -6f }, size = new WorldAreaSize { w = 4f, d = 6f },
                    },
                    new WorldArea
                    {
                        id = "a1", index = 1, role = "normal", garrisonDensity = "normal",
                        origin = new WorldAreaOrigin { x = -10f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                        composition = new WorldComposition { rusher = 4, blinker = 1 },
                        garrison = new[] { new WorldGarrisonEntry { kind = "blinker", x = 5f, z = 15f } },
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

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            // Starve the ordinary per-area cap to 1 (same idiom as
            // AreaAccumulationDirectorGarrisonAndPlacementTests.FillArea_SeedsGarrisonSynchronously) so
            // FillArea's own immediate top-up release can't add anything on top of the garrison's 3 -
            // garrison seeding bypasses the cap entirely, so this isolates ActiveCount to garrison only.
            DevTuning.MaxActiveRobots = 1f;

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>());

            Assert.AreEqual(3, director.ActiveCount, "SeedCount = round(5 * NormalShare 0.6) = 3");

            RobotEnemy[] active = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None);
            Assert.AreEqual(3, active.Length);

            RobotEnemy blinker = null;
            var rushers = new System.Collections.Generic.List<RobotEnemy>();
            foreach (RobotEnemy r in active)
            {
                if (r.Kind == EnemyKind.Blinker) blinker = r;
                else if (r.Kind == EnemyKind.Rusher) rushers.Add(r);
            }

            Assert.IsNotNull(blinker, "the authored garrison entry must produce a Blinker, not whatever the queue had next");
            Assert.AreEqual(2, rushers.Count, "the 2 remaining seed slots must still fill from the ring");

            Assert.AreEqual(5f, blinker.transform.position.x, 1e-3f, "the Blinker must land exactly on its authored x");
            Assert.AreEqual(15f, blinker.transform.position.z, 1e-3f, "the Blinker must land exactly on its authored z");
            Assert.IsTrue(blinker.IsDormant, "a garrison-seeded robot must start dormant, authored or ring-placed alike");

            // The plain ring for the 2 remaining slots, computed on an area that authors no garrison[]
            // at all — RingPositions only ever reads origin/size/cover/sheds, so this is exactly what
            // Garrison.SeedSlots fell back to internally for a1's own 2 unauthored slots.
            var a1NoGarrison = new WorldArea
            {
                id = "a1", index = 1, role = "normal",
                origin = new WorldAreaOrigin { x = -10f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
            };
            Vector3[] ringPositions = Garrison.SeedPositions(a1NoGarrison, 2);
            foreach (RobotEnemy rusher in rushers)
            {
                Assert.IsTrue(rusher.IsDormant, "ring-placed garrison members must still start dormant");

                bool onRing = false;
                foreach (Vector3 p in ringPositions)
                {
                    if (Mathf.Approximately(rusher.transform.position.x, p.x) && Mathf.Approximately(rusher.transform.position.z, p.z))
                    {
                        onRing = true;
                        break;
                    }
                }
                Assert.IsTrue(onRing, $"rusher at {rusher.transform.position} must land on the ring, not somewhere arbitrary");
            }
        }
    }
}
