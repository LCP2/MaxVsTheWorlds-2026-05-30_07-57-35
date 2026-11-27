using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Weapons;

namespace MaxWorlds.Pickups
{
    /// <summary>
    /// MV-1455: a pure, test-callable model of one world's Parts economy (Lee, 25 Nov: "based on the number of
    /// robots in the world and the areas they're in, you should be able to calculate how many parts are being
    /// dropped in total ... enough to upgrade to max on everything by the end of the world").
    ///
    /// <see cref="PartsByArea"/> is every Parts source the game really pays in an area, assuming a full clear
    /// (expected values where a source is a chance roll). <see cref="FullUpgradeCost"/> is the Parts needed to
    /// take every node on the world's board from its world-start level to max at the world's real prices.
    ///
    /// SOURCES (each total is in <see cref="SourceTotals"/>; code location of the real payout in brackets):
    /// <list type="bullet">
    /// <item>"kills" - large-kill Parts: the flat <see cref="WorldDefinition.PartsPerLargeKill"/> per kill, or the
    ///   per-area budget <c>CellsForArea x PartsMultiplier</c> (<c>PickupDirector.ResolveCellDrop</c>); Breaker kills
    ///   pay double (MV-1228), a Reef Brute +2 (MV-1106), a Dreadnought +4 (MV-1217) [PickupDirector.OnRobotDied].
    ///   Kill count = the area's solved <c>LargeCount</c> (<c>AreaAccumulationDirector.FillArea</c>; the garrison is
    ///   deducted from it, not added). Relay/twin robots pay nothing (MV-1394/MV-1396).</item>
    /// <item>"areaSupercell" - the ONE per-area Supercell budget (MV-1384, <c>PickupDirector.ClaimAreaSupercell</c>)
    ///   shared by the last-Bruiser grant (cadence areas, MV-767), a destroyed shed/replicator's cache and an elite's
    ///   bonus (<c>RobotEnemy.Die</c> -> <c>PlaceEliteSupercell</c>), x <see cref="PickupWallet.SupercellCellValue"/>.</item>
    /// <item>"shedCache" - the 6-Part ring of a destroyed shed/replicator that did not drop a Device
    ///   (<c>PickupDirector.SpawnCellCache</c>/<c>OnFactoryDestroyed</c>; a Device drops on odd factory ordinals while
    ///   any RIG category is still locked, and pays no Parts).</item>
    /// <item>"secrets" - 1 Supercell + 4 Parts per secret cover piece (<c>DestructibleCover.PlaceSecretCache</c>).</item>
    /// <item>"vaults" - 2 Supercells + 6 Parts each (<c>Vault.PlaceCache</c>).</item>
    /// <item>"challenges" - 1 Supercell each at base reward (<c>ChallengePad.Win</c>; the no-hit double is not assumed).</item>
    /// <item>"cradles" - <c>AssemblyCradle.PayoutPartsFor</c> each, plus a Supercell for every even destroyed-cradle
    ///   ordinal from 6 (<c>AssemblyCradle.ApplyUnlockSchedule</c>).</item>
    /// <item>"boss" - the final area's boss victory payoff: 5 Supercells (<c>BossVictoryPayoff.partCount</c>).</item>
    /// </list>
    /// Not modelled (a spending-pace effect, not a supply one): Parts refused while the wallet is at capacity.
    /// Computing a model points <see cref="RigBoard"/> at the world's loadout board (it prices through it); callers
    /// that care reset it (<c>RigBoard.ResetForTests</c>).
    /// </summary>
    public sealed class WorldEconomyModel
    {
        /// <summary><c>BossVictoryPayoff.partCount</c>'s default.</summary>
        public const int BossVictorySupercells = 5;

        public int WorldIndex { get; private set; }

        /// <summary>Parts paid in area n (1-based; index 0 unused), full clear.</summary>
        public float[] PartsByArea { get; private set; }

        /// <summary>Each source's total over the whole world.</summary>
        public Dictionary<string, float> SourceTotals { get; } = new Dictionary<string, float>();

        /// <summary>Parts to take every board node from its world-start level to max.</summary>
        public float FullUpgradeCost { get; private set; }

        /// <summary>Large kills in the world (sum of every area's solved large count).</summary>
        public int LargeKills { get; private set; }

        /// <summary>Cumulative Parts through the end of <paramref name="area"/>.</summary>
        public float CumulativeThrough(int area)
        {
            float sum = 0f;
            for (int a = 1; a <= area && a < PartsByArea.Length; a++) sum += PartsByArea[a];
            return sum;
        }

        public float TotalParts => CumulativeThrough(PartsByArea.Length - 1);

        /// <summary>Cumulative Parts through <paramref name="area"/> as a share of <see cref="FullUpgradeCost"/>.</summary>
        public float CoverageThrough(int area) => FullUpgradeCost > 0f ? CumulativeThrough(area) / FullUpgradeCost : 0f;

        public static WorldEconomyModel Compute(int worldIndex)
        {
            WorldDefinition row = WorldCatalog.Get(worldIndex);
            WorldConfig cfg = WorldLibrary.Load(row.ConfigKey);
            var m = new WorldEconomyModel { WorldIndex = worldIndex };
            int areaCount = cfg.dials != null ? cfg.dials.areaCount : 0;
            m.PartsByArea = new float[areaCount + 1];

            float supercell = PickupWallet.SupercellCellValue;
            int lockedCategories = LockedCategoriesAtWorldStart(worldIndex);
            int factoryOrdinal = 0, cradleOrdinal = 0;

            for (int n = 1; n <= areaCount; n++)
            {
                WorldArea area = cfg.AreaByIndex(n);
                if (area == null) continue;
                DifficultyEngine.Composition comp = cfg.SolveComposition(n);
                int large = comp.LargeCount;
                m.LargeKills += large;

                // --- kills (MV-1478: in a Parts Cache world the same amount is paid by the area's cache instead) ---
                float kills = KillPartsFor(row, comp, n);
                if (row.UsesPartsCaches) Add(m, n, "caches", Mathf.RoundToInt(kills));
                else Add(m, n, "kills", kills);

                // --- per-area Supercell budget (one, shared) ---
                bool budgetClaimed = false;
                float areaSuper = 0f;
                if (comp.Bruiser > 0 && (n - 1) % row.SupercellCadenceAreas == 0) { areaSuper = 1f; budgetClaimed = true; }

                // --- sheds / replicators ---
                int factories = area.replicators.Length + area.Sheds().Length;
                float shedParts = 0f;
                for (int f = 0; f < factories; f++)
                {
                    factoryOrdinal++;
                    if (lockedCategories > 0 && factoryOrdinal % 2 == 1) { lockedCategories--; continue; }   // a Device: no Parts
                    shedParts += PickupDirector.ShedCellCacheAmount;
                    budgetClaimed = true;   // a cache's Supercell, if the budget is still open, is the area's one
                    areaSuper = 1f;
                }

                // --- elites: the chance an area spawns at least one, spent only if the budget is still open ---
                if (!budgetClaimed && cfg.dials != null && cfg.dials.EliteChancePctForArea(n) > 0f)
                {
                    int eligible = comp.TotalCount - comp.Turret - comp.Dreadnought - comp.Lurker - comp.Skitter;
                    float p = Mathf.Clamp01(cfg.dials.EliteChancePctForArea(n) / 100f);
                    areaSuper = Mathf.Max(areaSuper, 1f - Mathf.Pow(1f - p, Mathf.Max(0, eligible)));
                }
                Add(m, n, "areaSupercell", areaSuper * supercell);
                Add(m, n, "shedCache", shedParts);

                // --- authored caches ---
                int secrets = 0;
                foreach (WorldCover c in area.cover) if (c.secret) secrets++;
                Add(m, n, "secrets", secrets * (supercell + DestructibleCover.SecretPowerCellCount));
                Add(m, n, "vaults", area.vaults.Length * (Vault.SupercellCount * supercell + Vault.PowerCellCount));
                Add(m, n, "challenges", area.challenges.Length * supercell);

                float cradles = 0f;
                for (int c = 0; c < area.cradles.Length; c++)
                {
                    cradleOrdinal++;
                    cradles += AssemblyCradle.PayoutPartsFor(worldIndex);
                    if (cradleOrdinal >= 6 && cradleOrdinal % 2 == 0) cradles += supercell;
                }
                Add(m, n, "cradles", cradles);

                if (n == areaCount && area.Bosses().Length > 0) Add(m, n, "boss", BossVictorySupercells * supercell);
            }

            m.FullUpgradeCost = FullUpgradeCostFor(worldIndex);
            return m;
        }

        /// <summary>The Parts an area's large kills are worth in this world (expected value): the flat per-kill rate or the
        /// per-area budget, Breaker kills double, Dreadnought +4, a Reef Brute +2. In a <see cref="WorldDefinition.UsesPartsCaches"/>
        /// world this is the amount the area's Parts Cache holds (MV-1478).</summary>
        private static float KillPartsFor(WorldDefinition row, DifficultyEngine.Composition comp, int n)
        {
            int large = comp.LargeCount;
            float multiplier = row.PartsMultiplier != null ? row.PartsMultiplier() : 1f;
            float perKill = row.PartsPerLargeKill > 0f
                ? row.PartsPerLargeKill
                : (large > 0 ? CellEconomyTuning.CellsForArea(n) * multiplier / large : 0f);
            float kills = large * perKill + comp.Breaker * perKill;   // a Breaker kill pays its roll twice
            kills += comp.Dreadnought * 4f;
            if (row.Id == WorldIds.Reef) kills += comp.Brute * 2f;
            return kills;
        }

        /// <summary>MV-1478: the whole-number Parts the Parts Cache of 1-based <paramref name="areaIndex"/> bursts in
        /// <paramref name="worldIndex"/> - exactly the "caches" figure <see cref="Compute"/> books for that area. 0 in a
        /// world that does not use caches.</summary>
        public static int CachePartsForArea(int worldIndex, int areaIndex)
        {
            WorldDefinition row = WorldCatalog.Get(worldIndex);
            if (!row.UsesPartsCaches) return 0;
            WorldConfig cfg = WorldLibrary.Load(row.ConfigKey);
            if (cfg == null || cfg.AreaByIndex(areaIndex) == null) return 0;
            return Mathf.RoundToInt(KillPartsFor(row, cfg.SolveComposition(areaIndex), areaIndex));
        }

        private static void Add(WorldEconomyModel m, int area, string source, float parts)
        {
            if (parts <= 0f) return;
            m.PartsByArea[area] += parts;
            m.SourceTotals.TryGetValue(source, out float t);
            m.SourceTotals[source] = t + parts;
        }

        /// <summary>RIG categories still locked when the world starts: everything but ENERGY/MOVE (carried), the
        /// world's own PRIMARY (granted by the Weapon Core) and, where it carries, SECONDARY.</summary>
        private static int LockedCategoriesAtWorldStart(int worldIndex)
        {
            WorldDefinition row = WorldCatalog.Get(worldIndex);
            UseBoard(worldIndex);
            int locked = 0;
            foreach (string cat in RigBoard.AllCategoryIds)
            {
                if (cat == "ENERGY" || cat == "MOVE" || cat == "PRIMARY") continue;
                if (cat == "SECONDARY" && row.CarrySecondaryAcrossMorph) continue;
                locked++;
            }
            return locked;
        }

        private static void UseBoard(int worldIndex)
        {
            WorldDefinition row = WorldCatalog.Get(worldIndex);
            RigBoard.UseLoadout(worldIndex, row.PrimaryWeapon, row.SecondaryWeapon);
        }

        /// <summary>The Parts to raise every node of <paramref name="worldIndex"/>'s board from its world-start level
        /// (<c>HomeScreen.StartSlotWorld</c> + <c>WorldCatalog.ApplyDevStartFloor</c>: ENERGY/MOVE at the previous
        /// world's own caps, everything else at the board's own start level) to its max, at that world's real prices.</summary>
        public static float FullUpgradeCostFor(int worldIndex)
        {
            int previousWorld = worldIndex - 1;
            IReadOnlyDictionary<string, int> prevCaps = previousWorld >= 0 ? RigBoard.SnapshotMaxLevels(previousWorld) : null;
            UseBoard(worldIndex);
            float total = 0f;
            foreach (string id in RigBoard.AllIds)
            {
                string cat = RigBoard.Category(id);
                int max = RigBoard.MaxLevel(id);
                int start = RigBoard.StartLevel(id);
                if ((cat == "ENERGY" || cat == "MOVE") && prevCaps != null && prevCaps.TryGetValue(id, out int carried))
                    start = Mathf.Min(max, Mathf.Max(start, carried));
                if (start == 0) total += CellSpend.UnlockCostFor(id);
                for (int level = Mathf.Max(start, 1); level < max; level++) total += CellSpend.UpgradeCostFor(id, level);
            }
            return total;
        }
    }
}
