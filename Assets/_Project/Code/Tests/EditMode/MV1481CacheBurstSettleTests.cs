using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1481: a Parts Cache burst scatters, lands, settles 1 s, and only then can Part Magneto / walk-over take it.
    /// One test (Rule 1), resolved state only (Tier 2): real World 2 map, real PickupDirector.Tick, e_mag at level 5, Max 2 m from the cache.
    ///   AC1 - burst + 0.4 s: nothing collected and nothing yet at Max; burst + 1.2 s: everything landed, nothing collected;
    ///         burst + 3.5 s: everything collected by the Magneto pull.
    ///   AC2 - landed spots are 1.5-3.5 m from the cache centre, >= 0.3 m apart, and walkable.
    /// Fails on the base commit (b15ac13): the pickups fly straight to Max, so they are already collected at burst + 0.4 s.
    /// </summary>
    public sealed class MV1481CacheBurstSettleTests
    {
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _pathGo, _directorGo, _maxGo;
        private PickupDirector _director;
        private MapData _map;

        [SetUp]
        public void SetUp()
        {
            EnemyNavigation.Reset();
            PickupWallet.Reset();
            RigBoard.UseWorld(WorldCatalog.IndexOf(WorldIds.Stormdrain));
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out _map, out string reason), reason);
            _pathGo = new GameObject("MV1481-backyard-path");
            BackyardPathMapField.SetValue(_pathGo.AddComponent<BackyardPath>(), _map);
            _directorGo = new GameObject("PickupDirector");
            _director = _directorGo.AddComponent<PickupDirector>();
            _maxGo = new GameObject("Max") { tag = "Player" };
            RigState.UnlockCategory("ENERGY");
            Assert.IsTrue(RigState.AcquireCap("e_cel"));
            Assert.IsTrue(RigState.AcquireCap("e_mag"));
            for (int i = 0; i < 4; i++) Assert.IsTrue(RigState.RaiseLevel("e_mag"));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_maxGo != null) Object.DestroyImmediate(_maxGo);
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
            EnemyNavigation.Reset();
            PickupWallet.Reset();
            RigBoard.ResetForTests();
        }

        private void Tick(int steps) { for (int i = 0; i < steps; i++) _director.Tick(0.1f); }

        [Test]
        public void CacheBurst_ScattersLandsAndSettlesOneSecond_BeforeMagnetoPullsIt()
        {
            Random.InitState(1481);
            var cache = new Vector3(19f, 0f, 110f);   // inside World 2 a1 "Outfall Steps" (x8-30, z100-120)
            _maxGo.transform.position = cache + new Vector3(2f, 0f, 0f);
            Assert.IsTrue(_map.IsWalkable(cache, _maxGo.transform.position));

            List<Pickup> burst = _director.SpawnCacheBurst(cache, PickupWallet.SupercellCellValue * 3 + 2);
            Assert.GreaterOrEqual(burst.Count, 4, "the burst spawned pickups");

            Tick(4);   // burst + 0.4 s
            foreach (Pickup p in burst)
            {
                Assert.IsTrue(p.gameObject.activeSelf, "burst + 0.4 s: not collected");
                Assert.Greater(Vector2.Distance(Flat(p.transform.position), Flat(_maxGo.transform.position)), 0.6f,
                    "burst + 0.4 s: not yet at Max");
            }

            Tick(8);   // burst + 1.2 s
            var spots = new List<Vector2>();
            foreach (Pickup p in burst)
            {
                Assert.IsTrue(p.gameObject.activeSelf, "burst + 1.2 s: still not collected");
                Vector2 at = Flat(p.transform.position);
                float r = Vector2.Distance(at, Flat(cache));
                Assert.That(r, Is.InRange(1.5f - 0.01f, 3.5f + 0.01f), "landed 1.5-3.5 m from the cache centre");
                Assert.IsTrue(_map.IsWalkable(cache, p.transform.position), "landed on walkable floor");
                foreach (Vector2 other in spots)
                    Assert.GreaterOrEqual(Vector2.Distance(at, other), 0.3f - 0.001f, "landed >= 0.3 m apart");
                spots.Add(at);
            }

            Tick(23);   // burst + 3.5 s
            foreach (Pickup p in burst)
                Assert.IsFalse(p.gameObject.activeSelf, "burst + 3.5 s: collected by the Magneto pull");
        }

        private static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
    }
}
