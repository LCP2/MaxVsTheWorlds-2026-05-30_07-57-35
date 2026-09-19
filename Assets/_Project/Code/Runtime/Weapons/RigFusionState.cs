using System;
using System.Collections.Generic;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// A run's live FORGE state (MV-426, THE RIG 5/5) — which fusions are forged. Deliberately its own
    /// class, not folded into <see cref="RigState"/>: a fusion is FORGED, never drafted or leveled, so
    /// it needs none of that class's level/reached machinery, only a one-way boolean per id.
    ///
    /// Eligibility (<see cref="IsEligible"/>) reads as "both parent categories lit" — at least one
    /// OWNED ability in each, exactly the design board's own amber/faint rule — and is deliberately
    /// independent of the currently-banked cell count (MV-423.png vs -noparts.png: OVERCHARGE stays
    /// amber/ready at zero cells banked; only the tap itself needs the cells). Spending the
    /// <see cref="RigFusionDef.CellCost"/> cells themselves is <see cref="PartSpend.TrySpendOnFusion"/>'s
    /// job (MV-515: converted from parts), same "check the sink can accept it BEFORE touching the bank"
    /// shape <see cref="RigState.RaiseLevel"/> already uses.
    /// </summary>
    public static class RigFusionState
    {
        private static readonly HashSet<string> s_forged = new HashSet<string>();

        /// <summary>Fired whenever a fusion is forged, or the state is reset.</summary>
        public static event Action Changed;

        /// <summary>Whether FORGE is playable in <paramref name="worldIndex"/>'s world (MV-850) — World 1
        /// only, until the four fusions are redesigned for World 2's LPPE/Shoulder Rack kit (today
        /// they're broken there: DELUGE only touches World 1 weapons, "SLOT B/U" has no caller,
        /// OVERCHARGE never spends a cell, SKIRMISH lost half its effect to MV-579). The one switch the
        /// eventual redesign ticket flips.</summary>
        public static bool EnabledInWorld(int worldIndex) => worldIndex < 1;

        /// <summary>False for every fusion once <see cref="RigBoard.ActiveWorldIndex"/> leaves World 1
        /// (MV-850), regardless of what was forged back in World 1 — a fusion forged there has no effect
        /// in World 2+ (no BLINKGUARD bubble, no SKIRMISH snap, no OVERCHARGE rate). The underlying forge
        /// itself is never cleared, only masked, so it reads true again on returning to World 1.</summary>
        public static bool IsForged(string id) => EnabledInWorld(RigBoard.ActiveWorldIndex) && s_forged.Contains(id);

        /// <summary>Both parent categories have at least one owned ability — the board's "ready"
        /// (amber) vs "not ready" (faint, <c>? ? ?</c>) rule, independent of parts currently banked.</summary>
        public static bool IsEligible(string id)
        {
            if (!RigBoard.TryGetFusion(id, out var def)) return false;
            return CategoryLit(def.ParentA) && CategoryLit(def.ParentB);
        }

        /// <summary>Whether <paramref name="category"/> alone is lit — at least one owned ability
        /// anywhere in it. <see cref="IsEligible"/>'s own per-parent half, exposed publicly (MV-729)
        /// so a locked FORGE row can tell the player which of its two required categories they
        /// already have, instead of naming both as if neither were met.</summary>
        public static bool CategoryLit(string category)
        {
            foreach (string abilityId in RigBoard.AllIds)
                if (RigBoard.Category(abilityId) == category && RigState.IsOwned(abilityId)) return true;
            return false;
        }

        /// <summary>Forges <paramref name="id"/> if it exists, isn't already forged, and is eligible.
        /// Does not touch <see cref="MaxWorlds.Pickups.PickupWallet"/> — the caller
        /// (<see cref="PartSpend.TrySpendOnFusion"/>) only actually spends parts once this returns
        /// true, same pattern every other RIG spend in this file's sibling classes follows. Refuses
        /// outright outside World 1 (MV-850) — "nothing in it can be bought" holds even if something
        /// ever calls this without going through the (already forge-less) World 2+ board UI.</summary>
        public static bool TryForge(string id)
        {
            if (!EnabledInWorld(RigBoard.ActiveWorldIndex)) return false;
            if (!RigBoard.FusionExists(id)) return false;
            if (s_forged.Contains(id)) return false;
            if (!IsEligible(id)) return false;
            s_forged.Add(id);
            Changed?.Invoke();
            return true;
        }

        /// <summary>The fusion id currently occupying HUD slot "B" or "U", or null if that slot is
        /// still LOCKED — what <see cref="MaxWorlds.UI.HudController"/> reads to decide whether to
        /// keep showing LOCKED there.</summary>
        public static string ForgedInSlot(string hudSlot)
        {
            foreach (var def in RigBoard.Fusions)
                if (def.HudSlot == hudSlot && IsForged(def.Id)) return def.Id;
            return null;
        }

        /// <summary>Back to a fresh run's baseline: nothing forged. Fires <see cref="Changed"/> so a
        /// live board/HUD redraws.</summary>
        public static void Reset()
        {
            s_forged.Clear();
            Changed?.Invoke();
        }

        /// <summary>Test isolation only — same as <see cref="Reset"/> without the event, mirroring
        /// <see cref="MaxWorlds.Arena.Sentinel.ResetRegistry"/>'s silent-clear idiom.</summary>
        public static void ResetForTests() => s_forged.Clear();
    }
}
