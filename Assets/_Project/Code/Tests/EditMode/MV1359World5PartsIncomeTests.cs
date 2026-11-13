using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1359 (Lee, 13 Nov): "By the time I get to area 21 I'm fully upgraded." Full World 5 board
    /// coverage must arrive no earlier than area 27 and no later than area 30. Builds the real
    /// Mothership config, the real board prices and the real drop tuning, and walks cumulative expected
    /// board-currency (Parts) income area by area (Tier 2: resolved values only).
    ///
    /// Income per area: robot drops (CellsForArea x the world's PartsMultiplier; the per-area budget is
    /// independent of kill count), cradles x the world's cradle Parts payout, elite Supercells
    /// (min(eliteMaxPerArea, eliteChancePct x eligible garrison robots) x 10), the cadence Supercell
    /// (every SupercellCadenceAreas from area 1), challenge-pad Supercells at base reward (1 each) and the
    /// Helmsman's five Supercells at area 30. Cradle mid-build scrap replaces the payout, so it is excluded.
    /// Demand: every PRIMARY and SECONDARY level on the Mothership board plus the two sentinel slots.
    ///
    /// Fails on base a368077 (Parts multiplier 1.0, cradle payout 10): coverage lands at area 25.
    /// </summary>
    public sealed class MV1359World5PartsIncomeTests
    {
        private const int SupercellValue = PickupWallet.SupercellCellValue;
        private const int HelmsmanSupercells = 5;   // BossVictoryPayoff.partCount default; unchanged by this ticket

        [SetUp]
        public void SetUp() => RigBoard.ResetForTests();

        [TearDown]
        public void TearDown() => RigBoard.ResetForTests();

        [Test]
        public void World5_FullBoardCoverageFirstReachedBetweenArea27And30()
        {
            int w5 = WorldCatalog.IndexOf(WorldIds.Mothership);
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.Mothership);
            Assert.IsNotNull(cfg);

            float demand = Demand(w5);
            float[] cumulative = Cumulative(cfg, w5, out int firstFull, demand);

            Debug.Log($"[MV-1359] W5 demand {demand}; coverage first reached at area {firstFull}; " +
                      $"cumulative at 1/10/20/25/27/30 = {cumulative[1]}/{cumulative[10]}/{cumulative[20]}/{cumulative[25]}/{cumulative[27]}/{cumulative[30]}");
            Assert.That(firstFull, Is.InRange(27, 30),
                $"World 5 cumulative Parts income ({cumulative[firstFull > 0 ? firstFull : 30]}) first covers demand {demand} at area {firstFull}; must be 27-30");

            // AC3: World 4 (the Reef) is untouched -- same model, quoted for the before/after comparison.
            int w4 = WorldCatalog.IndexOf(WorldIds.Reef);
            WorldConfig reef = WorldLibrary.Load(WorldCatalog.Get(w4).ConfigKey);
            float[] reefCum = Cumulative(reef, w4, out int reefFull, Demand(w4));
            Debug.Log($"[MV-1359] W4 model: parts x{CellEconomyTuning.WorldPartsMultiplier(w4)}, cradle payout {CradlePayout(w4)}, " +
                      $"coverage at area {reefFull}, cumulative at 30 = {reefCum[30]}");
            Assert.AreEqual(AssemblyCradle.PayoutParts, CradlePayout(w4), "World 4 cradle Parts payout must stay the shared default");
        }

        private static float Demand(int worldIndex)
        {
            RigBoard.UseWorld(worldIndex);
            float total = 0f;
            foreach (string id in RigBoard.AllIds)
            {
                string cat = RigBoard.Category(id);
                if (cat != "PRIMARY" && cat != "SECONDARY" && id != "u_slt") continue;
                int start = RigBoard.StartLevel(id), max = RigBoard.MaxLevel(id);
                if (start == 0) total += CellSpend.UnlockCostFor(id);
                for (int level = Mathf.Max(start, 1); level < max; level++) total += CellSpend.UpgradeCostFor(id, level);
            }
            return total;
        }

        private static float[] Cumulative(WorldConfig cfg, int worldIndex, out int firstFull, float demand)
        {
            var cum = new float[31];
            firstFull = -1;
            float running = 0f;
            foreach (WorldArea a in cfg.areas)
            {
                if (a.index < 1 || a.index > 30) continue;
                running += CellEconomyTuning.CellsForArea(a.index) * CellEconomyTuning.WorldPartsMultiplier(worldIndex);
                running += a.cradles.Length * CradlePayout(worldIndex);
                int eligible = 0;
                foreach (WorldGarrisonEntry g in a.garrison)
                    if (EnemyKindNames.TryParse(g.kind, out EnemyKind k) && EliteRoll.IsEligibleKind(k)) eligible++;
                running += Mathf.Min(cfg.dials.eliteMaxPerArea, cfg.dials.eliteChancePct / 100f * eligible) * SupercellValue;
                if ((a.index - 1) % WorldCatalog.Get(worldIndex).SupercellCadenceAreas == 0) running += SupercellValue;
                running += a.challenges.Length * SupercellValue;
                if (a.index == 30) running += HelmsmanSupercells * SupercellValue;
                cum[a.index] = running;
                if (firstFull < 0 && running >= demand) firstFull = a.index;
            }
            return cum;
        }

        // Compiles on base too (no PayoutPartsFor there): falls back to the shared constant.
        private static int CradlePayout(int worldIndex)
        {
            MethodInfo m = typeof(AssemblyCradle).GetMethod("PayoutPartsFor", BindingFlags.Public | BindingFlags.Static);
            return m != null ? (int)m.Invoke(null, new object[] { worldIndex }) : AssemblyCradle.PayoutParts;
        }
    }
}
