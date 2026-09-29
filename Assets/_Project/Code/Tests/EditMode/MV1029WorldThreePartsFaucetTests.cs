using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1029 (Lee, TestFlight, 29 Sep 2026): World 3's parts faucet was world-agnostic —
    /// <see cref="CellEconomyTuning.CellsForArea"/> paid the same per-area budget World 3 as World 1,
    /// however many robots World 3 throws at Max, so upgrades couldn't keep pace. The fix raises
    /// World 3's faucet alone (<see cref="CellEconomyTuning.WorldPartsMultiplier"/>), leaving prices
    /// and World 1/2's own faucet untouched.
    ///
    /// Feeds every large kill of World 3 area 5 through <see cref="PickupDirector"/>'s private
    /// <c>OnRobotDied</c>/<c>ResolveCellDrop</c> (same reflection idiom as
    /// <see cref="MV672PowerCellSecondaryDropTests"/>) and asserts the resolved total lands at
    /// 2 x <see cref="CellEconomyTuning.CellsForArea"/>(5), within the per-area accumulator's own
    /// rounding tolerance — chosen so the 6 kills divide the doubled 18-part budget evenly (3/kill),
    /// leaving no fractional remainder to widen the tolerance further.
    ///
    /// Guards MV-1029.
    /// </summary>
    public sealed class MV1029WorldThreePartsFaucetTests
    {
        private const int WorldThreeIndex = 2;   // 0-based: World 1 = 0, World 2 = 1, World 3 = 2
        private const int AreaFive = 5;
        private const int LargeKillsInArea = 6;   // divides 2 x CellsForArea(5) == 18 evenly

        private GameObject _pickupGo;
        private PickupDirector _pickupDirector;
        private GameObject _areaGo;
        private AreaAccumulationDirector _areaDirector;

        [SetUp]
        public void SetUp()
        {
            PickupWallet.Reset();
            DevTuning.Reset();
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            foreach (var a in Object.FindObjectsByType<AreaAccumulationDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(a.gameObject);

            _pickupGo = new GameObject("PickupDirector");
            _pickupDirector = _pickupGo.AddComponent<PickupDirector>();

            _areaGo = new GameObject("AreaAccumulationDirector");
            _areaDirector = _areaGo.AddComponent<AreaAccumulationDirector>();
            _areaDirector.ConfigureWorld(null, worldIndex: WorldThreeIndex);
            _areaDirector.SetCurrentArea(AreaFive);
            SetLargeCountForArea(_areaDirector, AreaFive, LargeKillsInArea);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            if (_pickupGo != null) Object.DestroyImmediate(_pickupGo);
            if (_areaGo != null) Object.DestroyImmediate(_areaGo);
            PickupWallet.Reset();
            DevTuning.Reset();
        }

        // Guards MV-1029
        [Test]
        public void ResolveCellDrop_InWorld3Area5_GrantsDoubleTheAuthoredPerAreaBudget()
        {
            for (int i = 0; i < LargeKillsInArea; i++)
                KillOneLargeRobot(_pickupDirector, new Vector3(i, 0f, 0f));

            int totalCellsDropped = CountLivePowerCells(_pickupDirector);
            float expected = 2f * CellEconomyTuning.CellsForArea(AreaFive);

            Assert.That(totalCellsDropped, Is.InRange(expected - 1, expected + 1),
                $"World 3 area {AreaFive}'s {LargeKillsInArea} large kills must grant ~{expected} parts " +
                $"(2x the authored per-area budget {CellEconomyTuning.CellsForArea(AreaFive)}), got {totalCellsDropped}");
        }

        private static void KillOneLargeRobot(PickupDirector director, Vector3 pos) =>
            typeof(PickupDirector).GetMethod("OnRobotDied", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { pos, EnemyKind.Heavy });   // large, not Bruiser (avoids the Supercell branch)

        private static int CountLivePowerCells(PickupDirector director)
        {
            var live = (List<Pickup>)typeof(PickupDirector)
                .GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(director);
            int count = 0;
            foreach (var p in live) if (p.Kind == PickupKind.PowerCell) count++;
            return count;
        }

        private static void SetLargeCountForArea(AreaAccumulationDirector director, int areaIndex, int count)
        {
            var dict = (Dictionary<int, int>)typeof(AreaAccumulationDirector)
                .GetField("_largeCountByArea", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(director);
            dict[areaIndex] = count;
        }
    }
}
