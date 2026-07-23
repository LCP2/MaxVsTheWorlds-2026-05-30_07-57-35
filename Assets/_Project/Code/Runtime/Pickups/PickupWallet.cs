using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Upgrades;

namespace MaxWorlds.Pickups
{
    /// <summary>
    /// The player's banked drops (YT-131/133). Power cells accumulate into a count the HUD shows — a
    /// future currency with no gameplay use yet. Parts collected but not yet installed queue up in the
    /// order they were picked up (dropped-part-decides — each carries which of the five it is); the
    /// HUD flashes while any wait, and the upgrade screen installs them front-first.
    ///
    /// Static because there is exactly one player and the HUD, the pickups, and the upgrade screen all
    /// need to see the same tally without threading a reference through the scene. Event-driven so the
    /// HUD reacts rather than polls. <see cref="Reset"/> exists for a new run and for test isolation.
    /// </summary>
    public static class PickupWallet
    {
        /// <summary>Banked power cells (display-only currency for now).</summary>
        public static int PowerCells { get; private set; }

        // Parts waiting to be installed, oldest first — the upgrade screen takes them front-first.
        private static readonly Queue<PartKind> s_parts = new Queue<PartKind>();

        /// <summary>How many parts are collected but not yet installed (YT-132's chip shows while > 0).</summary>
        public static int PartsPending => s_parts.Count;

        /// <summary>The pending queue, oldest (front) first — a save slot persisting exactly what's
        /// waiting, in the order the upgrade screen will install it (YT-151).</summary>
        public static IEnumerable<PartKind> PendingParts => s_parts;

        /// <summary>Fired when the power-cell count changes. Arg = the new total.</summary>
        public static event Action<int> PowerCellsChanged;

        /// <summary>Fired when the pending-parts count changes. Arg = the new count. The HUD raises its
        /// flashing edge icon off this (YT-131); the upgrade screen consumes them (YT-132/133).</summary>
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

        /// <summary>Bank a collected part of a specific kind (YT-133).</summary>
        public static void AddPart(PartKind kind)
        {
            s_parts.Enqueue(kind);
            PartsChanged?.Invoke(s_parts.Count);
        }

        /// <summary>The next part to install (front of the queue), without removing it — what the
        /// upgrade screen reveals. False if none pending.</summary>
        public static bool TryPeekPart(out PartKind kind)
        {
            if (s_parts.Count == 0) { kind = default; return false; }
            kind = s_parts.Peek();
            return true;
        }

        /// <summary>Consume the next pending part (the upgrade screen calls this once it's installed).
        /// No-op with nothing pending. Returns true if one was actually spent.</summary>
        public static bool SpendPart()
        {
            if (s_parts.Count == 0) return false;
            s_parts.Dequeue();
            PartsChanged?.Invoke(s_parts.Count);
            return true;
        }

        /// <summary>Wipe the bank (new run / test isolation). Fires the change events so any live HUD
        /// re-reads zero rather than keeping a stale count on screen.</summary>
        public static void Reset()
        {
            PowerCells = 0;
            s_parts.Clear();
            PowerCellsChanged?.Invoke(0);
            PartsChanged?.Invoke(0);
        }
    }
}
