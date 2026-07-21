using System;

namespace MaxWorlds.Pickups
{
    /// <summary>
    /// The player's banked drops (YT-131). Power cells accumulate into a count the HUD shows — a
    /// future currency with no gameplay use yet. Parts accumulate into a pending count that the HUD
    /// turns into a flashing "you have an upgrade to install" icon; YT-132's upgrade screen is what
    /// eventually consumes them.
    ///
    /// Static because there is exactly one player and the HUD, the pickups, and (later) the upgrade
    /// screen all need to see the same tally without threading a reference through the scene. Kept
    /// tiny and event-driven so the HUD reacts to a change rather than polling. <see cref="Reset"/>
    /// exists for a new run and for test isolation — static state would otherwise leak between tests.
    /// </summary>
    public static class PickupWallet
    {
        /// <summary>Banked power cells (display-only currency for now).</summary>
        public static int PowerCells { get; private set; }

        /// <summary>Parts collected but not yet installed via the upgrade screen (YT-132).</summary>
        public static int PartsPending { get; private set; }

        /// <summary>Fired when the power-cell count changes. Arg = the new total.</summary>
        public static event Action<int> PowerCellsChanged;

        /// <summary>Fired when a part is collected. Arg = the new pending count. The HUD raises its
        /// flashing edge icon off this (YT-131); the upgrade screen consumes it (YT-132).</summary>
        public static event Action<int> PartsChanged;

        public static void AddPowerCell()
        {
            PowerCells++;
            PowerCellsChanged?.Invoke(PowerCells);
        }

        public static void AddPart()
        {
            PartsPending++;
            PartsChanged?.Invoke(PartsPending);
        }

        /// <summary>Consume one pending part (YT-132's upgrade screen calls this once it's installed).
        /// No-op with nothing pending. Returns true if one was actually spent.</summary>
        public static bool SpendPart()
        {
            if (PartsPending <= 0) return false;
            PartsPending--;
            PartsChanged?.Invoke(PartsPending);
            return true;
        }

        /// <summary>Wipe the bank (new run / test isolation). Fires the change events so any live HUD
        /// re-reads zero rather than keeping a stale count on screen.</summary>
        public static void Reset()
        {
            PowerCells = 0;
            PartsPending = 0;
            PowerCellsChanged?.Invoke(0);
            PartsChanged?.Invoke(0);
        }
    }
}
