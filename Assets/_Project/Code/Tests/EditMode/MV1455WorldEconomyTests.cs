using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1455 (Lee, 25 Nov, City): the world's Parts must max every RIG node by its end, and a robot must pay
    /// the same in a3 as in a25. Tier 2 only (resolved values): the model's cumulative Parts against the real
    /// City board's full-upgrade cost, and the Parts / rockets 10 Bruisers actually drop through
    /// <c>PickupDirector.OnRobotDied</c> (the real kill path) in the City (a3 vs a25) and, unchanged, in World 2.
    /// Guards MV-1455.
    /// </summary>
    public sealed class MV1455WorldEconomyTests
    {
        private GameObject _pickupGo, _areaGo;

        [SetUp]
        public void SetUp()
        {
            PickupWallet.Reset();
            DevTuning.Reset();
            RigBoard.ResetForTests();
            Cleanup();
        }

        [TearDown]
        public void TearDown()
        {
            Cleanup();
            PickupWallet.Reset();
            DevTuning.Reset();
            RigBoard.ResetForTests();
        }

        private void Cleanup()
        {
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            foreach (var a in Object.FindObjectsByType<AreaAccumulationDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(a.gameObject);
            _pickupGo = null;
            _areaGo = null;
        }

        [Test]
        public void CityParts_MaxTheBoardByA30_ArriveSteadily_AndPayTheSamePerRobotEverywhere()
        {
            int city = WorldCatalog.IndexOf(WorldIds.City);
            WorldEconomyModel model = WorldEconomyModel.Compute(city);
            var sources = new List<string>();
            foreach (var kv in model.SourceTotals) sources.Add($"{kv.Key}={kv.Value:0.#}");
            Debug.Log($"[MV-1455] City: FullUpgradeCost {model.FullUpgradeCost}, large kills {model.LargeKills}, " +
                      $"by a15 {model.CumulativeThrough(15):0.#} ({model.CoverageThrough(15):P1}), " +
                      $"by a30 {model.CumulativeThrough(30):0.#} ({model.CoverageThrough(30):P1}); " +
                      $"rate {WorldCatalog.Get(city).PartsPerLargeKill}; sources: {string.Join(", ", sources)}");

            Assert.That(model.CoverageThrough(30), Is.InRange(1.00f, 1.10f),
                $"City Parts by a30 ({model.CumulativeThrough(30)}) must be 100-110% of FullUpgradeCost {model.FullUpgradeCost}");
            Assert.That(model.CoverageThrough(15), Is.InRange(0.40f, 0.55f),
                $"City Parts by a15 ({model.CumulativeThrough(15)}) must be 40-55% of FullUpgradeCost {model.FullUpgradeCost}");

            RigBoard.ResetForTests();

            (int parts3, int rockets3) = TenBruisers(city, area: 3);
            (int parts25, int rockets25) = TenBruisers(city, area: 25);
            Assert.That(parts25, Is.InRange(parts3 - 1, parts3 + 1), $"City a3 dropped {parts3} Parts, a25 {parts25}");
            Assert.That(rockets25, Is.InRange(rockets3 - 1, rockets3 + 1), $"City a3 dropped {rockets3} rockets, a25 {rockets25}");
            Assert.That(rockets3, Is.InRange(7, 9), "10 City kills at 0.8 rockets each");

            // World 2 is untouched: today's per-area budget CellsForArea(5) = 9 Parts spread over the 10 kills
            // (0.9 each) and one rocket per Part dropped. The accumulator runs in float, so 0.9 x 10 floors to 8
            // Parts / 8 rockets today (the ninth lands on the 11th kill) - the values the unchanged path gives.
            (int partsW2, int rocketsW2) = TenBruisers(WorldCatalog.IndexOf(WorldIds.Stormdrain), area: 5);
            Assert.AreEqual(8, partsW2, "World 2 a5 Parts from 10 kills");
            Assert.AreEqual(8, rocketsW2, "World 2 a5 rockets from 10 kills");
        }

        private (int parts, int rockets) TenBruisers(int worldIndex, int area)
        {
            Cleanup();
            PickupWallet.Reset();
            _pickupGo = new GameObject("PickupDirector");
            var director = _pickupGo.AddComponent<PickupDirector>();
            _areaGo = new GameObject("AreaAccumulationDirector");
            var areaDirector = _areaGo.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(null, worldIndex);
            areaDirector.SetCurrentArea(area);
            var dict = (Dictionary<int, int>)typeof(AreaAccumulationDirector)
                .GetField("_largeCountByArea", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(areaDirector);
            dict[area] = 10;

            MethodInfo died = typeof(PickupDirector).GetMethod("OnRobotDied", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < 10; i++) died.Invoke(director, new object[] { new Vector3(i * 3f, 0f, 0f), EnemyKind.Bruiser });

            var live = (List<Pickup>)typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(director);
            int parts = 0, rockets = 0;
            foreach (Pickup p in live)
            {
                if (p.Kind == PickupKind.PowerCell) parts++;
                else if (p.Kind == PickupKind.PowerCellSecondary) rockets++;
            }
            return (parts, rockets);
        }
    }
}
