using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1462 (Lee, 25 Nov, W4): the Reef's Parts must max every RIG node by a30 a flat rate cannot also reach 40-55% by a15 (MV-1462 AC: report it, do not invent a mechanism; the a15 figure is logged).
    /// Tier 2: the real WorldEconomyModel on the live world3 config against the Reef board's FullUpgradeCost.
    /// Guards MV-1462.
    /// </summary>
    public sealed class MV1462ReefEconomyTests
    {
        [SetUp] public void SetUp() { RigBoard.ResetForTests(); }
        [TearDown] public void TearDown() { RigBoard.ResetForTests(); }

        [Test]
        public void ReefParts_MaxTheBoardByA30()
        {
            int reef = WorldCatalog.IndexOf(WorldIds.Reef);
            WorldEconomyModel model = WorldEconomyModel.Compute(reef);
            var sources = new List<string>();
            foreach (var kv in model.SourceTotals) sources.Add($"{kv.Key}={kv.Value:0.#}");
            Debug.Log($"[MV-1462] Reef: rate {WorldCatalog.Get(reef).PartsPerLargeKill}, FullUpgradeCost {model.FullUpgradeCost}, " +
                      $"large kills {model.LargeKills}, a15 {model.CumulativeThrough(15):0.#} ({model.CoverageThrough(15):P1}), " +
                      $"a30 {model.CumulativeThrough(30):0.#} ({model.CoverageThrough(30):P1}); sources: {string.Join(", ", sources)}");

            Assert.That(model.CoverageThrough(30), Is.InRange(1.00f, 1.10f),
                $"Reef Parts by a30 ({model.CumulativeThrough(30)}) must be 100-110% of FullUpgradeCost {model.FullUpgradeCost}");
        }
    }
}
