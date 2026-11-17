using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>MV-1384 (Lee, staging, 17 Nov): W4 (the Reef) area 1 dropped 3 Supercells. The rule is one
    /// Supercell per area at most, from the area's last Bruiser. Guards MV-1384.</summary>
    public sealed class MV1384ReefArea1SupercellCapTests
    {
        private GameObject _pickupGo, _areaGo;
        private PickupDirector _pickupDirector;
        private AreaAccumulationDirector _areaDirector;

        [SetUp]
        public void SetUp()
        {
            PickupWallet.Reset();
            DevTuning.Reset();
            DeathRunState.Reset();
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None)) Object.DestroyImmediate(d.gameObject);
            foreach (var a in Object.FindObjectsByType<AreaAccumulationDirector>(FindObjectsSortMode.None)) Object.DestroyImmediate(a.gameObject);
            _pickupGo = new GameObject("PickupDirector");
            _pickupDirector = _pickupGo.AddComponent<PickupDirector>();
            _areaGo = new GameObject("AreaAccumulationDirector");
            _areaDirector = _areaGo.AddComponent<AreaAccumulationDirector>();
            _areaDirector.ConfigureWorld(null, worldIndex: WorldCatalog.IndexOf(WorldIds.Reef));
            _areaDirector.SetCurrentArea(1);
            var dict = (Dictionary<int, int>)typeof(AreaAccumulationDirector)
                .GetField("_bruiserCountByArea", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_areaDirector);
            dict[1] = 1;   // a1 authors exactly one Salvage Crab Bruiser
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
            if (_pickupGo != null) Object.DestroyImmediate(_pickupGo);
            if (_areaGo != null) Object.DestroyImmediate(_areaGo);
            PickupWallet.Reset();
            DevTuning.Reset();
            DeathRunState.Reset();
        }

        // Guards MV-1384
        [Test]
        public void LastBruiserShedCacheAndEliteBonus_SpawnAtMostOneSupercellForArea1()
        {
            var t = typeof(PickupDirector);
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            t.GetMethod("OnRobotDied", flags).Invoke(_pickupDirector, new object[] { Vector3.zero, EnemyKind.Bruiser });
            t.GetMethod("OnFactoryDestroyed", flags).Invoke(_pickupDirector, new object[] { new Vector3(5f, 0f, 0f) });
            _pickupDirector.PlaceEliteSupercell(new Vector3(9f, 0f, 0f));
            var live = (List<Pickup>)t.GetField("_live", flags).GetValue(_pickupDirector);
            int supercells = 0;
            foreach (var p in live) if (p.Kind == PickupKind.Supercell) supercells++;
            Assert.That(supercells, Is.LessThanOrEqualTo(1), "Reef area 1 must spawn at most one Supercell, got " + supercells);
        }
    }
}
