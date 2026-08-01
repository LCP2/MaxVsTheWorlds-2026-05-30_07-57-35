using System;
using UnityEngine;

namespace MaxWorlds.Pickups
{
    /// <summary>
    /// The player's banked drops (YT-131, recut WV-228). Power cells accumulate into a count the HUD
    /// shows — a future currency with no gameplay use yet. Parts are now universal upgrade tokens
    /// (WV-228): a plain banked count, no identity, no auto-install and no draft-pick popup on
    /// collection — replaces the old dropped-part-decides queue (YT-133/YT-207). Spending one against a
    /// chosen owned track/ability lives in <see cref="MaxWorlds.Weapons.PartSpend"/>.
    ///
    /// Static because there is exactly one player and the HUD, the pickups, and the weapons area all
    /// need to see the same tally without threading a reference through the scene. Event-driven so the
    /// HUD reacts rather than polls. <see cref="Reset"/> exists for a new run and for test isolation.
    /// </summary>
    public static class PickupWallet
    {
        /// <summary>Banked power cells (display-only currency for now).</summary>
        public static int PowerCells { get; private set; }

        /// <summary>Banked parts (WV-228) — universal upgrade tokens, no identity. The HUD's chip shows
        /// while > 0; the weapons area spends them one at a time against a chosen owned track/ability.</summary>
        public static int PartsBanked { get; private set; }

        /// <summary>Fired when the power-cell count changes. Arg = the new total.</summary>
        public static event Action<int> PowerCellsChanged;

        /// <summary>Fired when the banked-parts count changes. Arg = the new count. The HUD raises its
        /// flashing edge icon off this (YT-131); the weapons area spends them (WV-228).</summary>
        public static event Action<int> PartsChanged;

        /// <summary>Max power cells the reserve holds (YT-137) — the meter's full mark. Collecting past
        /// it is wasted; the Hydro device drains against it. Tunable via <see cref="MaxWorlds.Core.DevTuning.PowerCellCapacity"/>.</summary>
        public const int DefaultCapacity = 30;

        public static int Capacity =>
            Mathf.Max(1, Mathf.RoundToInt(MaxWorlds.Core.DevTuning.Or(
                MaxWorlds.Core.DevTuning.PowerCellCapacity, DefaultCapacity)));

        public static void AddPowerCell()
        {
            if (PowerCells >= Capacity) return;   // reserve is full (YT-137)
            PowerCells++;
            PowerCellsChanged?.Invoke(PowerCells);
        }

        /// <summary>Set the banked total directly — a save slot restoring what was on disk (YT-151),
        /// not a pickup. Clamped to the reserve the same way <see cref="AddPowerCell"/> is.</summary>
        public static void SetPowerCells(int count)
        {
            int clamped = Mathf.Clamp(count, 0, Capacity);
            if (clamped == PowerCells) return;
            PowerCells = clamped;
            PowerCellsChanged?.Invoke(PowerCells);
        }

        /// <summary>Consume one power cell if any remain — the Hydro condenser burns these to self-supply
        /// water while untethered (YT-137). Returns false at empty, which is what stalls Hydro.</summary>
        public static bool TrySpendPowerCell()
        {
            if (PowerCells <= 0) return false;
            PowerCells--;
            PowerCellsChanged?.Invoke(PowerCells);
            return true;
        }

        /// <summary>Spend several power cells atomically for a single ability activation (WV-231) —
        /// a Water Balloon throw or a Dash/Teleport use either affords its whole cost or doesn't fire
        /// at all, never a partial spend. Returns false (and spends nothing) if the reserve can't
        /// cover the full amount.</summary>
        public static bool TrySpendPowerCells(int amount)
        {
            if (amount <= 0) return true;
            if (PowerCells < amount) return false;
            PowerCells -= amount;
            PowerCellsChanged?.Invoke(PowerCells);
            return true;
        }

        /// <summary>Bank one collected part (WV-228) — a fungible token, no identity to carry.</summary>
        public static void AddPart()
        {
            PartsBanked++;
            PartsChanged?.Invoke(PartsBanked);
        }

        /// <summary>Consume one banked part (WV-228) — the weapons area calls this once a spend on a
        /// chosen owned track/ability actually raises its level. No-op with nothing banked. Returns
        /// true if one was actually spent.</summary>
        public static bool TrySpendPart()
        {
            if (PartsBanked <= 0) return false;
            PartsBanked--;
            PartsChanged?.Invoke(PartsBanked);
            return true;
        }

        /// <summary>Wipe the bank (new run / test isolation). Fires the change events so any live HUD
        /// re-reads zero rather than keeping a stale count on screen.</summary>
        public static void Reset()
        {
            PowerCells = 0;
            PartsBanked = 0;
            PowerCellsChanged?.Invoke(0);
            PartsChanged?.Invoke(0);
        }
    }
}
