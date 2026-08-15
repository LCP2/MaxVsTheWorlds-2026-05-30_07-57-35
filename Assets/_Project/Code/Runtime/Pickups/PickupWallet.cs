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

        /// <summary>Max power cells the reserve holds at Cell Capacity level 0 (MV-374: dropped from
        /// the old flat 30 — that number now sits at level 1 of 3, see <see cref="PowerCellCapacityPerLevel"/>).
        /// Collecting past the reserve's current <see cref="Capacity"/> is wasted; the Hydro device
        /// drains against it. Tunable via <see cref="MaxWorlds.Core.DevTuning.PowerCellCapacity"/>,
        /// which overrides the whole level-based formula outright.</summary>
        public const int DefaultCapacity = 20;

        /// <summary>Extra capacity each Cell Capacity level adds (MV-374): 3 levels at +10 each carry
        /// the reserve from 20 up to 50 (20, 30, 40, 50).</summary>
        public const int PowerCellCapacityPerLevel = 10;

        /// <summary>The Cell Capacity track's level cap (MV-374) — 3 purchasable levels, same
        /// "spend a part" idiom as <see cref="MaxWorlds.Weapons.WeaponSystemState"/>'s tracks, but
        /// this one is a general player stat rather than a weapon/ability track, so it lives here
        /// rather than under <c>MaxWorlds.Weapons</c>.</summary>
        public const int PowerCellCapacityMaxLevel = 3;

        /// <summary>Levels bought via <see cref="MaxWorlds.Weapons.PartSpend.TrySpendOnCellCapacity"/>,
        /// 0 (fresh run) to <see cref="PowerCellCapacityMaxLevel"/>.</summary>
        public static int PowerCellCapacityLevel { get; private set; }

        /// <summary>The reserve's current full mark before any <see cref="MaxWorlds.Core.DevTuning"/>
        /// override — <see cref="DefaultCapacity"/> plus whatever <see cref="PowerCellCapacityLevel"/>
        /// has bought. What the Settings panel's "no override" baseline reads (MV-374).</summary>
        public static int BaseCapacity => DefaultCapacity + PowerCellCapacityLevel * PowerCellCapacityPerLevel;

        public static int Capacity =>
            Mathf.Max(1, Mathf.RoundToInt(MaxWorlds.Core.DevTuning.Or(
                MaxWorlds.Core.DevTuning.PowerCellCapacity, BaseCapacity)));

        /// <summary>Fired when the reserve's capacity itself changes — a level-up (MV-374) — separate
        /// from <see cref="PowerCellsChanged"/>, which fires when the banked COUNT moves. The HUD's
        /// "current/max" text and the weapons screen's CELLS chip both need to redraw even though the
        /// current count didn't change.</summary>
        public static event Action<int> CapacityChanged;

        /// <summary>Raise Cell Capacity by one level (MV-374), up to <see cref="PowerCellCapacityMaxLevel"/>.
        /// No-ops (returns false) already at the cap. Call through
        /// <see cref="MaxWorlds.Weapons.PartSpend.TrySpendOnCellCapacity"/> so a part is only actually
        /// spent when this succeeds.</summary>
        public static bool LevelUpCellCapacity()
        {
            if (PowerCellCapacityLevel >= PowerCellCapacityMaxLevel) return false;
            PowerCellCapacityLevel++;
            CapacityChanged?.Invoke(Capacity);
            return true;
        }

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
        /// re-reads zero rather than keeping a stale count on screen. MV-374: Cell Capacity resets to
        /// level 0 too, same as every other run-scoped upgrade track.</summary>
        public static void Reset()
        {
            PowerCells = 0;
            PartsBanked = 0;
            PowerCellCapacityLevel = 0;
            PowerCellsChanged?.Invoke(0);
            PartsChanged?.Invoke(0);
            CapacityChanged?.Invoke(Capacity);
        }
    }
}
