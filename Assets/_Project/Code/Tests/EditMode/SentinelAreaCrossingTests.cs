using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-396: a deployed sentinel used to vanish the instant a gate BROKE
    /// (<see cref="AreaGate.Opened"/>), even while Max was still standing in the area it guarded — the
    /// gate-open signal fires early, well before the player has actually walked through the doorway (it
    /// exists to give a room's population a head start, MV-245). The fix moves
    /// <see cref="Sentinel.DestroyAllActive"/> onto <see cref="AreaAccumulationDirector.PlayerCrossedIntoArea"/>,
    /// which only ever advances off Max's own position. This proves the two signals are genuinely
    /// decoupled: <see cref="AreaAccumulationDirector.EnterArea"/> (what a gate's <c>Opened</c> now
    /// drives) must NOT trip the crossing event on its own, only a real position change does.
    /// </summary>
    public sealed class SentinelAreaCrossingTests
    {
        private GameObject _directorGo;
        private GameObject _playerGo;

        [SetUp]
        public void SetUp() => Sentinel.ResetRegistry();

        [TearDown]
        public void TearDown()
        {
            Sentinel.ResetRegistry();
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        private static void InvokeUpdate(AreaAccumulationDirector director)
        {
            typeof(AreaAccumulationDirector).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, null);
        }

        [Test]
        public void ADeployedSentinelSurvivesAGateOpeningUntilMaxActuallyWalksIntoTheNextArea()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            MapZone area1 = map.Zone("area1");
            MapZone area2 = map.Zone("area2");
            Assert.IsNotNull(area1, "world1's map must have an area1 zone for this test to place Max in");
            Assert.IsNotNull(area2, "world1's map must have an area2 zone for this test to place Max in");

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>());

            // Same wiring BackyardPath.Awake() does (MV-396): the sentinel wipe hangs off the real
            // position-crossing event, not any AreaGate's Opened.
            director.PlayerCrossedIntoArea += _ => Sentinel.DestroyAllActive();

            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = area1.Center;

            var sentinelGo = new GameObject("Sentinel");
            sentinelGo.AddComponent<Sentinel>().Init(area1.Center, 200f, range: 7f, fireInterval: 0.6f,
                moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
            Assert.That(Sentinel.Active.Count, Is.EqualTo(1));

            InvokeUpdate(director); // establishes _target; Max hasn't moved, no crossing yet
            Assert.That(Sentinel.Active.Count, Is.EqualTo(1),
                "a stationary player driving one Update must not clear a freshly deployed sentinel");

            // Simulate the gate into area 2 breaking (what AreaGate.Opened -> EnterArea now does) while
            // Max is still physically standing in area 1.
            director.EnterArea(2);
            InvokeUpdate(director);

            Assert.That(Sentinel.Active.Count, Is.EqualTo(1),
                "MV-396: the gate merely opening (EnterArea's early population advance) must not clear " +
                "a sentinel while Max hasn't actually crossed into the next area yet");

            // Now Max actually walks through the doorway.
            _playerGo.transform.position = area2.Center;
            InvokeUpdate(director);

            Assert.That(Sentinel.Active.Count, Is.EqualTo(0),
                "the sentinel must clear once Max has actually crossed into the next area, per MV-362");
        }
    }
}
