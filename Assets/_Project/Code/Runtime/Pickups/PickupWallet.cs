using System;
using UnityEngine;
using MaxWorlds.Weapons;

namespace MaxWorlds.Pickups
{
    /// <summary>
    /// The player's banked drops (YT-131, recut WV-228, MV-515, recut again MV-519). Power cells
    /// accumulate into a count the HUD shows and (MV-458) are THE RIG board's primary currency —
    /// unlocking a new node costs <see cref="MaxWorlds.Weapons.CellSpend.UnlockCostCells"/>, raising an
    /// owned one costs <see cref="MaxWorlds.Weapons.CellSpend.UpgradeCostFor"/> of its current level.
    /// MV-519: a Supercell is no longer banked or cashed in — it grants <see cref="SupercellCellValue"/>
    /// cells the instant it's picked up (<see cref="AddSupercell"/>), even past <see cref="Capacity"/>,
    /// which MV-515's banked/cash-in model is retired in favour of.
    ///
    /// Static because there is exactly one player and the HUD, the pickups, and the weapons area all
    /// need to see the same tally without threading a reference through the scene. Event-driven so the
    /// HUD reacts rather than polls. <see cref="Reset"/> exists for a new run and for test isolation.
    /// </summary>
    public static class PickupWallet
    {
        /// <summary>Banked power cells (MV-458: THE RIG board's primary spendable currency). MV-519: may
        /// sit above <see cref="Capacity"/> after a Supercell pickup — see <see cref="AddSupercell"/>.</summary>
        public static int PowerCells { get; private set; }

        /// <summary>How many cells one collected Supercell grants, instantly, on pickup (MV-519).</summary>
        public const int SupercellCellValue = 10;

        /// <summary>Fired when the power-cell count changes. Arg = the new total.</summary>
        public static event Action<int> PowerCellsChanged;

        /// <summary>Max power cells the reserve holds at Cell Storage level 0 (MV-374: dropped from
        /// the old flat 30 — that number now sits at level 1 of 3, see <see cref="PowerCellCapacityPerLevel"/>).
        /// Collecting past the reserve's current <see cref="Capacity"/> is wasted; the Hydro device
        /// drains against it. Tunable via <see cref="MaxWorlds.Core.DevTuning.PowerCellCapacity"/>,
        /// which overrides the whole level-based formula outright.</summary>
        public const int DefaultCapacity = 20;

        /// <summary>Extra capacity each Cell Storage level adds (MV-374): 10 per level, see
        /// <see cref="BaseCapacity"/>.</summary>
        public const int PowerCellCapacityPerLevel = 10;

        /// <summary>Cell Storage's (<c>e_cel</c>) level cap — MV-422 moved this track off
        /// <see cref="PickupWallet"/> onto THE RIG's own <c>e_cel</c> node (a <c>cap</c>: it must be
        /// taken in a Morphing Module draft before any part can raise it further, unlike the old
        /// "owned/spendable from run start" track this replaces).</summary>
        public static int PowerCellCapacityMaxLevel => RigBoard.MaxLevel("e_cel");

        /// <summary>Levels bought via <see cref="MaxWorlds.Weapons.CellSpend"/>, 0 (fresh run, unowned)
        /// to <see cref="PowerCellCapacityMaxLevel"/> — now a thin read of
        /// <c>e_cel</c>'s live RIG level (MV-422), not separately tracked state.</summary>
        public static int PowerCellCapacityLevel => RigState.Level("e_cel");

        /// <summary>The reserve's current full mark before any <see cref="MaxWorlds.Core.DevTuning"/>
        /// override — <see cref="DefaultCapacity"/> plus whatever <see cref="PowerCellCapacityLevel"/>
        /// has bought. What the Settings panel's "no override" baseline reads (MV-374).</summary>
        public static int BaseCapacity => DefaultCapacity + PowerCellCapacityLevel * PowerCellCapacityPerLevel;

        public static int Capacity =>
            Mathf.Max(1, Mathf.RoundToInt(MaxWorlds.Core.DevTuning.Or(
                MaxWorlds.Core.DevTuning.PowerCellCapacity, BaseCapacity)));

        /// <summary>MV-519: true once a Supercell has pushed <see cref="PowerCells"/> past
        /// <see cref="Capacity"/> — every cell readout reads this to give the over-cap balance its own
        /// distinct "bonus" treatment (Change item 5) instead of looking like an unclamped bug.</summary>
        public static bool IsOverCapacity => PowerCells > Capacity;

        /// <summary>Fired when the reserve's capacity itself changes — a level-up (MV-374) — separate
        /// from <see cref="PowerCellsChanged"/>, which fires when the banked COUNT moves. The HUD's
        /// "current/max" text and the weapons screen's CELLS chip both need to redraw even though the
        /// current count didn't change.</summary>
        public static event Action<int> CapacityChanged;

        private static int s_lastNotifiedCapacity = DefaultCapacity;

        /// <summary>MV-458: e_cel now levels through the same generic <see cref="MaxWorlds.Weapons.CellSpend"/>
        /// path every other node uses, not only through <see cref="LevelUpCellCapacity"/> — so <see cref="CapacityChanged"/> must
        /// also fire off <c>RigState.Changed</c> directly, or the HUD's capacity readout goes stale the
        /// instant a generic spend (rather than the old dedicated wrapper) is what moved e_cel.</summary>
        static PickupWallet() => RigState.Changed += RaiseCapacityChangedIfMoved;

        private static void RaiseCapacityChangedIfMoved()
        {
            int cap = Capacity;
            if (cap == s_lastNotifiedCapacity) return;
            s_lastNotifiedCapacity = cap;
            CapacityChanged?.Invoke(cap);
        }

        /// <summary>Raise Cell Storage (<c>e_cel</c>) by one level, up to <see cref="PowerCellCapacityMaxLevel"/>
        /// — a direct convenience wrapper kept for callers that only care about e_cel specifically
        /// (tests, mainly). MV-458: e_cel is no longer special-cased for a live spend — the generic
        /// <see cref="MaxWorlds.Weapons.CellSpend.TryUnlockNode"/>/<see cref="MaxWorlds.Weapons.CellSpend.TryUpgradeNode"/>
        /// raise e_cel the exact same way every other RIG node levels, by calling
        /// <c>RigState.RaiseLevel("e_cel")</c> directly rather than through here — which is why
        /// <see cref="CapacityChanged"/> also listens to <c>RigState.Changed</c> below, not only to
        /// this method's own explicit fire.</summary>
        public static bool LevelUpCellCapacity()
        {
            if (!RigState.RaiseLevel("e_cel")) return false;
            CapacityChanged?.Invoke(Capacity);
            return true;
        }

        /// <summary>Bank one collected power cell. Returns false and changes nothing at capacity
        /// (MV-439) — the caller (<see cref="PickupDirector.Collect"/>) must not consume the pickup,
        /// emit a gain, or pull it via Magneto when this refuses.</summary>
        public static bool AddPowerCell()
        {
            if (PowerCells >= Capacity) return false;   // reserve is full (YT-137)
            PowerCells++;
            PowerCellsChanged?.Invoke(PowerCells);
            return true;
        }

        /// <summary>Set the banked total directly — a save slot restoring what was on disk (YT-151),
        /// not a pickup. MV-519: floored at zero only, NOT clamped to <see cref="Capacity"/> — a save
        /// taken while over-cap (a Supercell picked up past the reserve's mark, see
        /// <see cref="AddSupercell"/>) must restore that same over-cap value, not silently destroy it.</summary>
        public static void SetPowerCells(int count)
        {
            int clamped = Mathf.Max(0, count);
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

        // ------------------------------------------------------------------ Power Cells (MV-672)
        //
        // A second, separate, scarcer currency ("Power Cells" to the player) — NOT the same balance as
        // PowerCells above, which post-Issue-1's rename displays to the player as "Parts" despite its
        // old C# name. Mirrors PowerCells' own getter/Add/TrySpend/Set/changed-event shape exactly, but
        // carries no reserve capacity of its own (nothing has authored a cap for it yet).

        /// <summary>Banked Power Cells (MV-672's actual new currency).</summary>
        public static int PowerCellsSecondary { get; private set; }

        /// <summary>Fired when the Power Cells count changes. Arg = the new total.</summary>
        public static event Action<int> PowerCellsSecondaryChanged;

        /// <summary>Bank one collected Power Cell.</summary>
        public static void AddPowerCellSecondary()
        {
            PowerCellsSecondary++;
            PowerCellsSecondaryChanged?.Invoke(PowerCellsSecondary);
        }

        /// <summary>Set the banked total directly — a save slot restoring what was on disk, not a pickup.</summary>
        public static void SetPowerCellSecondary(int count)
        {
            int clamped = Mathf.Max(0, count);
            if (clamped == PowerCellsSecondary) return;
            PowerCellsSecondary = clamped;
            PowerCellsSecondaryChanged?.Invoke(PowerCellsSecondary);
        }

        /// <summary>Consume one Power Cell if any remain. Returns false at empty.</summary>
        public static bool TrySpendPowerCellSecondary()
        {
            if (PowerCellsSecondary <= 0) return false;
            PowerCellsSecondary--;
            PowerCellsSecondaryChanged?.Invoke(PowerCellsSecondary);
            return true;
        }

        /// <summary>Spend several Power Cells atomically — affords the full amount or spends nothing.</summary>
        public static bool TrySpendPowerCellSecondaries(int amount)
        {
            if (amount <= 0) return true;
            if (PowerCellsSecondary < amount) return false;
            PowerCellsSecondary -= amount;
            PowerCellsSecondaryChanged?.Invoke(PowerCellsSecondary);
            return true;
        }

        /// <summary>Grant one collected Supercell's cells instantly (MV-519, retiring MV-515's banked/
        /// cash-in model) — no bank, no cash-in step, no player action. Always adds the FULL
        /// <see cref="SupercellCellValue"/>, even past <see cref="Capacity"/>: unlike an ordinary cell
        /// pickup (<see cref="AddPowerCell"/>), a Supercell is never refused and never clamped — it is
        /// always worth taking. <see cref="PowerCells"/> may sit above <see cref="Capacity"/> afterwards
        /// until the player spends it back down; that over-cap balance stands, it does not decay.</summary>
        public static void AddSupercell()
        {
            PowerCells += SupercellCellValue;
            PowerCellsChanged?.Invoke(PowerCells);
        }

        /// <summary>Wipe the bank (new run / test isolation). Fires the change events so any live HUD
        /// re-reads zero rather than keeping a stale count on screen. MV-422: Cell Capacity
        /// (<c>e_cel</c>) now lives in the shared <see cref="RigState"/>, which has no per-node reset —
        /// this resets THE WHOLE RIG tree, not just <c>e_cel</c>, so a caller doesn't also need to
        /// remember <see cref="MaxWorlds.Weapons.WeaponSystemState.Reset"/> just to get a clean
        /// Cell-Capacity baseline (harmless double-reset on the production "new run" path,
        /// <c>HomeScreen.StartSlot</c>, which already calls both).</summary>
        public static void Reset()
        {
            PowerCells = 0;
            PowerCellsSecondary = 0;
            RigState.Reset();
            RigFusionState.Reset();
            PowerCellsChanged?.Invoke(0);
            PowerCellsSecondaryChanged?.Invoke(0);
            CapacityChanged?.Invoke(Capacity);
        }
    }
}
