using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Save;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1096 — Lee (device, TestFlight v0.11.7, 2026-10-06): "I resumed and I think I was on 10 up
    /// before and have been taken to 10 lower and now moving through empty areas 11, 12 etc." Root cause
    /// (ticket's own, not re-derived here): the checkpoint stored only an area INDEX, and
    /// <c>WorldRunner.ResumeCheckpoint</c> resolved a respawn position off <c>_gateIntoArea[areaIndex]</c>
    /// — a dictionary with exactly ONE gate per area. World 2's a10/a11/a12 are each walked twice (a
    /// floor leg through one gate, a deck leg later through a genuinely different one), so a checkpoint
    /// captured on the deck pass resumed through whichever gate happened to win that dictionary's last
    /// write, with every earlier area already cleared — exactly the reported "walking the whole empty
    /// floor route again".
    ///
    /// Fix: the checkpoint now also carries the id of the gate Max actually entered through
    /// (<see cref="SaveSlotData.CheckpointGateId"/>), which fixes the level/visit a bare area index can't.
    /// <see cref="WorldRunner.ResumeCheckpoint(int, string)"/> resolves that gate directly (not through
    /// <c>_gateIntoArea</c>) and lands Max just inside it, on its own level;
    /// <see cref="AreaAccumulationDirector.RestoreAreaAtLevel"/> restores only that level's own authored
    /// garrison for an in-place-deck area, leaving the other level exactly as a fresh cold boot leaves it.
    ///
    /// Drives the real entry points against the real shipped World 2 config — <c>MapRuntime.Build</c>,
    /// <c>WorldRunner.Configure</c>/<c>ResumeCheckpoint</c>, <c>AreaAccumulationDirector.Configure</c> —
    /// same idiom as <c>MV951ResumeRestoresWholeRunTests</c>/<c>Mv909ResumeAreaGateLatchTests</c>, never a
    /// hand-rolled shortcut. Must fail on d30d293 (current main at pickup): <c>ResumeCheckpoint</c> takes
    /// only an <c>int</c> there, so this file does not even compile against that commit — the fail-first
    /// proof is a compile error, not a runtime assertion (there is no gate-aware overload to call).
    /// </summary>
    public sealed class MV1096GateCheckpointResumeTests
    {
        private GameObject _host;
        private GameObject _playerGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            DestroyAllAreaRobotsRoots();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            DestroyAllAreaRobotsRoots();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_host != null) Object.DestroyImmediate(_host);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
        }

        /// <summary>MV-1096: this test builds THREE fresh <c>AreaAccumulationDirector</c> instances in
        /// one method (one per sub-check), each lazily creating its OWN scene-root "Area Robots" parent
        /// the first time it places a robot (<c>AreaAccumulationDirector.Take</c>) — unlike every other
        /// field this class resets, that parent is never reparented under this test's own <c>_host</c>,
        /// so <c>GameObject.Find("Area Robots")</c> (the single-root idiom every other full-World-2-build
        /// test in this suite uses) only ever destroys ONE of potentially several stale roots. Destroys
        /// ALL of them by name, not just the first match — the measured leak this ticket's own run
        /// caught (a10's population read 40 instead of 16, the previous sub-check's 16 plus this one's
        /// own still in the scene).</summary>
        private static void DestroyAllAreaRobotsRoots()
        {
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && t.parent == null && t.name == "Area Robots")
                    Object.DestroyImmediate(t.gameObject);
        }

        private sealed class Scenario
        {
            public WorldConfig Cfg;
            public MapData Map;
            public MapBuild Built;
            public AreaAccumulationDirector AreaDirector;
            public WorldRunner Runner;
        }

        /// <summary>A fresh World 2 cold boot (same build order as <c>BackyardPath.Awake</c>:
        /// <c>MapRuntime.Build</c>, then the directors) — never reused across sub-checks, since a gate
        /// this ticket's own <see cref="AreaGate.ForceOpen"/> opens must start each sub-check intact.</summary>
        private Scenario BuildFreshWorld2(GameObject host)
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see MV890AreaGateDressingPairingTests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            MapBuild built = MapRuntime.Build(map, host.transform);

            var areaGo = new GameObject("Area Accumulation");
            areaGo.transform.SetParent(host.transform);
            var areaDirector = areaGo.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg, worldIndex: 1);
            areaDirector.Configure(map, built.Cover);

            var runnerGo = new GameObject("WorldRunner Test Root");
            runnerGo.transform.SetParent(host.transform);
            var runner = runnerGo.AddComponent<WorldRunner>();
            runner.Configure(cfg, map, built, areaDirector);

            return new Scenario { Cfg = cfg, Map = map, Built = built, AreaDirector = areaDirector, Runner = runner };
        }

        private GameObject SpawnPlayer(Vector3 at)
        {
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = at;
            return _playerGo;
        }

        private static Vector3 GatePosition(MapBuild built, string gateId)
        {
            Assert.IsTrue(built.Actors.TryGetValue(gateId, out GameObject gateGo) && gateGo != null,
                $"setup failure: World 2 must build a gate entity for '{gateId}'");
            return gateGo.transform.position;
        }

        /// <summary>Per-entry, resolved-value proof that every authored garrison entry at
        /// <paramref name="level"/> in <paramref name="areaIndex"/> is a live robot standing at its
        /// authored (x,z) — Tier 2, same granularity as <c>MV1049DeckRevisitGarrisonTests.AssertDeckGarrisonPresent</c>,
        /// never a bare count (Testing policy Rule 3).</summary>
        private static void AssertEveryEntryLive(WorldConfig cfg, int areaIndex, int level, bool expectDormant)
        {
            WorldArea area = cfg.AreaByIndex(areaIndex);
            Assert.IsNotNull(area, $"setup failure: area{areaIndex} missing from World 2's own config");

            WorldGarrisonEntry[] entries = area.GarrisonForVisit(level);
            Assert.That(entries.Length, Is.GreaterThan(0),
                $"setup failure: area{areaIndex} authors no level-{level} garrison entries — proves nothing");

            foreach (WorldGarrisonEntry entry in entries)
            {
                RobotEnemy r = LiveRobotAt(areaIndex, entry.x, entry.z);
                Assert.IsNotNull(r,
                    $"MV-1096: area{areaIndex}'s authored level-{level} entry ({entry.kind} at " +
                    $"{entry.x},{entry.z}) must be a live robot after the gate-identified resume — found none");

                if (expectDormant)
                    Assert.IsTrue(r.IsDormant,
                        $"MV-1096: area{areaIndex}'s level-{level} entry at ({entry.x},{entry.z}) must land Dormant");
            }
        }

        private static RobotEnemy LiveRobotAt(int areaIndex, float x, float z)
        {
            RobotEnemy best = null;
            foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
            {
                if (r == null || !r.IsAlive || r.AreaIndex != areaIndex) continue;
                Vector3 p = r.transform.position;
                if (Mathf.Abs(p.x - x) > 0.05f || Mathf.Abs(p.z - z) > 0.05f) continue;
                if (best == null || p.y > best.transform.position.y) best = r;
            }
            return best;
        }

        [Test]
        public void ColdBootResume_UsesTheEnteredGateIdentity_DeckFloorAndNoGateFieldAlike()
        {
            // ---- sub-check 1: resumed through g35 (a11 -> a10, the DECK gate) -----------------------
            _host = new GameObject("MV1096 Host 1");
            Scenario s1 = BuildFreshWorld2(_host);
            SpawnPlayer(s1.Map.Zone("area1").Center);

            s1.Runner.ResumeCheckpoint(10, "g35");

            Vector3 maxPos = _playerGo.transform.position;
            Assert.IsTrue(s1.Map.IsOnDeck(maxPos.x, maxPos.y, maxPos.z),
                "MV-1096 AC1: resuming through g35 (the deck gate) must land Max on a10's deck");
            Assert.That(maxPos.y, Is.EqualTo(2.5f).Within(0.2f),
                "MV-1096 AC1: resuming through g35 must land Max within 0.2m of the 2.5m deck height");
            float distToG35 = Vector3.Distance(maxPos, GatePosition(s1.Built, "g35"));
            Assert.That(distToG35, Is.LessThanOrEqualTo(4f),
                $"MV-1096 AC1: Max must land within 4m of g35's door mouth — measured {distToG35:F2}m");

            AssertEveryEntryLive(s1.Cfg, areaIndex: 10, level: 1, expectDormant: true);
            Assert.That(s1.AreaDirector.ActiveCountForArea(10), Is.EqualTo(16),
                "MV-1096 AC1: a10's live population after a deck-gate resume must be exactly its authored " +
                "16 level-1 entries — any more means a level-0 (floor) robot leaked in too");

            Object.DestroyImmediate(_host);
            _host = null;
            DestroyAllAreaRobotsRoots();
            RobotEnemy.ResetRegistry();

            // ---- sub-check 2: resumed through g27 (a9 -> a10, the FLOOR gate) -----------------------
            _host = new GameObject("MV1096 Host 2");
            Scenario s2 = BuildFreshWorld2(_host);
            SpawnPlayer(s2.Map.Zone("area1").Center);

            s2.Runner.ResumeCheckpoint(10, "g27");

            Vector3 floorPos = _playerGo.transform.position;
            Assert.IsFalse(s2.Map.IsOnDeck(floorPos.x, floorPos.y, floorPos.z),
                "MV-1096 AC2: resuming through g27 (the floor gate) must NOT land Max on a10's deck");
            float distToG27 = Vector3.Distance(floorPos, GatePosition(s2.Built, "g27"));
            Assert.That(distToG27, Is.LessThanOrEqualTo(4f),
                $"MV-1096 AC2: Max must land within 4m of g27's door mouth — measured {distToG27:F2}m");

            AssertEveryEntryLive(s2.Cfg, areaIndex: 10, level: 0, expectDormant: false);

            Object.DestroyImmediate(_host);
            _host = null;
            DestroyAllAreaRobotsRoots();
            RobotEnemy.ResetRegistry();

            // ---- sub-check 3: a save with no gate field resumes exactly as it did before this ticket -
            _host = new GameObject("MV1096 Host 3");
            Scenario s3 = BuildFreshWorld2(_host);
            SpawnPlayer(s3.Map.Zone("area1").Center);

            s3.Runner.ResumeCheckpoint(10);   // no gateId — the pre-MV-1096 call shape

            Assert.That(s3.AreaDirector.CurrentArea, Is.EqualTo(9),
                "MV-1096 AC3: with no gate field, a resume into area 10 must still land CurrentArea at " +
                "its route predecessor (area 9) exactly as it did before this ticket");
            Assert.That(s3.AreaDirector.PhysicalArea, Is.EqualTo(9),
                "MV-1096 AC3: with no gate field, the physical tracker must land at area 9 too, unchanged");
        }
    }
}
