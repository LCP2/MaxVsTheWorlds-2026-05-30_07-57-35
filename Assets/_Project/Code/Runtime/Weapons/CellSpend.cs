using System.Collections.Generic;
using MaxWorlds.Pickups;
using UnityEngine;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Spends banked POWER CELLS on THE RIG board (MV-458) — cells are the primary progression
    /// currency for both a node's 0-&gt;1 unlock and its further levels. MV-515: unlocking is cells-only
    /// again — a Supercell is no longer an unlock requirement; MV-519 went further and retired the
    /// banked-Supercell concept outright — it grants <see cref="MaxWorlds.Pickups.PickupWallet.SupercellCellValue"/>
    /// cells the instant it's picked up (<see cref="MaxWorlds.Pickups.PickupWallet.AddSupercell"/>).
    /// Same "check the sink can accept it BEFORE touching the bank" order every other spend in this
    /// codebase follows.
    /// </summary>
    public static class CellSpend
    {
        /// <summary>Cost to unlock a new node — its 0-&gt;1 grant — with cells. MV-511: flat, does not
        /// escalate — unlock breadth is already gated by the parent level &gt;= 2 requirement
        /// (<see cref="RigState.IsCellUnlockable"/>). The default for every node NOT in
        /// <see cref="s_flatNodeCost"/> — see <see cref="UnlockCostFor"/>.</summary>
        public const int UnlockCostCells = 10;

        /// <summary>MV-511: the base cell cost of an upgrade, before the per-level escalation
        /// <see cref="UpgradeCostFor(int)"/> applies. Deliberately not named with the old flat constant's
        /// exact "UpgradeCost" + "Cells" prefix run-on — AC6's own grep for that retired declaration
        /// must not false-positive on this one.</summary>
        public const int UpgradeCostBaseCells = 5;

        /// <summary>MV-511: the node level past which the escalation stops rising — set by
        /// <see cref="MaxWorlds.Pickups.PickupWallet.DefaultCapacity"/> (20): uncapped,
        /// <c>UpgradeCostBaseCells * level</c> would put the level 5-&gt;6 upgrade (25 cells) out of
        /// reach of a base wallet, stranding a player who hasn't unlocked the ENERGY family (and
        /// therefore can't reach <c>e_cel</c>) below level 5 on every node forever.</summary>
        public const int UpgradeCostEscalationCap = 4;

        /// <summary>MV-623 (DECISION, Lee 29 Aug 2026): the flat cell price for every Slots (<c>u_slt</c>)
        /// unlock/upgrade — a deliberately expensive late-game purchase (see <see cref="s_flatNodeCost"/>).</summary>
        public const int SlotCostCells = 40;

        /// <summary>MV-623: per-node flat-price override, keyed by ability id — a node in this table pays
        /// the SAME flat price for its unlock and for every one of its upgrades, in place of the global
        /// <see cref="UnlockCostCells"/> / <see cref="UpgradeCostFor(int)"/> ladder. Every node not listed
        /// here is untouched by this table. <see cref="UnlockCostFor(string)"/> and
        /// <see cref="UpgradeCostFor(string, int)"/> are the only two entry points that read it — every
        /// other member of this class (display and spend alike) must route through those, never the bare
        /// constants/level-only overload, or the display and spend paths can disagree (a node showing one
        /// price and charging another).</summary>
        private static readonly Dictionary<string, int> s_flatNodeCost = new() { { "u_slt", SlotCostCells } };

        /// <summary>Cost to unlock <paramref name="id"/> with cells — <see cref="s_flatNodeCost"/>'s
        /// price if it has one, else the global <see cref="UnlockCostCells"/>.</summary>
        public static int UnlockCostFor(string id) =>
            s_flatNodeCost.TryGetValue(id, out int c) ? c : UnlockCostCells;

        /// <summary>Cost to raise a node currently at <paramref name="level"/> by one level with cells —
        /// 5, 10, 15, 20, 20, 20... for levels 1..6 and beyond, escalating by the node's OWN level (a
        /// within-build depth-vs-breadth trade the player makes) rather than by world/area progress,
        /// which would tax advancement the way <see cref="MaxWorlds.Weapons.RigState"/>'s design
        /// deliberately avoids (see MV-511 for the full reasoning). Capped at
        /// <see cref="UpgradeCostEscalationCap"/> so every single upgrade stays affordable on
        /// <see cref="MaxWorlds.Pickups.PickupWallet.DefaultCapacity"/> alone. The fallback
        /// <see cref="UpgradeCostFor(string, int)"/> reads for every id not in <see cref="s_flatNodeCost"/>.</summary>
        public static int UpgradeCostFor(int level) => UpgradeCostBaseCells * Mathf.Min(level, UpgradeCostEscalationCap);

        /// <summary>Cost to raise <paramref name="id"/>, currently at <paramref name="level"/>, by one
        /// level with cells — <see cref="s_flatNodeCost"/>'s flat price if it has one, else the same
        /// level-escalating <see cref="UpgradeCostFor(int)"/> every other node uses.</summary>
        public static int UpgradeCostFor(string id, int level) =>
            s_flatNodeCost.TryGetValue(id, out int c) ? c : UpgradeCostFor(level);

        /// <summary>Unlock <paramref name="id"/> for <see cref="UnlockCostCells"/> cells. Requires
        /// <see cref="RigState.IsCellUnlockable"/> (its category unlocked and, for a non-root node, its
        /// parent at level &gt;= 2). No special case for <c>e_cel</c> or any other node — every RIG node
        /// unlocks the same way now. MV-515: cells-only — a Supercell is no longer required to unlock.
        /// Every precondition (cells banked, the node eligible) is checked before either is touched, so
        /// a failed unlock never leaves a partial spend behind. Grants through
        /// <see cref="WeaponSystemState.AcquireById"/> (MV-435 AC5's own sanctioned entry point) rather
        /// than calling the RIG model layer's own grant primitive directly — that would silently skip
        /// <see cref="WeaponSystemState.Changed"/> and leave any HUD control gated on it stuck stale.</summary>
        public static bool TryUnlockNode(string id)
        {
            int cost = UnlockCostFor(id);
            if (PickupWallet.PowerCells < cost) return false;
            if (!RigState.IsCellUnlockable(id)) return false;
            if (!WeaponSystemState.AcquireById(id)) return false;
            PickupWallet.TrySpendPowerCells(cost);
            return true;
        }

        /// <summary>Raise an already-owned <paramref name="id"/> by one level for
        /// <see cref="UpgradeCostFor"/>'s current-level price in cells — never touches banked parts,
        /// however many are sitting there. The same "unowned/locked items can't be upgraded" gate every
        /// other spend in this codebase enforces (<see cref="RigState.CanSpendPart"/>'s own owned/below-cap
        /// check, via the RIG model layer's own currency-agnostic "raise one level" primitive both the
        /// legacy part-spend wrappers and this class raise against). The cost is read off the node's
        /// level BEFORE the raise advances it — a level 1 node's own upgrade costs
        /// <see cref="UpgradeCostFor"/>(1), not the level it is about to become. Raises through
        /// <see cref="WeaponSystemState.RaiseLevelById"/> (MV-659) rather than calling the RIG model
        /// layer's grant primitive directly — that would silently skip
        /// <see cref="WeaponSystemState.Changed"/> and leave anything gated on it (the reticle, the
        /// drawn stream) stuck at whatever it was last built at.</summary>
        public static bool TryUpgradeNode(string id)
        {
            int cost = UpgradeCostFor(id, RigState.Level(id));
            if (PickupWallet.PowerCells < cost) return false;
            if (!WeaponSystemState.RaiseLevelById(id)) return false;
            PickupWallet.TrySpendPowerCells(cost);
            return true;
        }

        /// <summary>MV-470: the CELLS-only half of a node's affordability — owned checks the upgrade
        /// path, unowned checks the unlock path. Pure, so THE RIG board can drive its per-node "live vs
        /// inert" read (a pulsing ring/badge vs a flat one) without a speculative spend-and-undo, same
        /// idiom as <see cref="RigState.CanSpendPart"/>. MV-515: this is the same question
        /// <c>WeaponsScreen.IsAbilityNodeSpendable</c> now asks, since cells are the only currency an
        /// unlock or upgrade ever needed.</summary>
        public static bool IsCellActionAffordable(string id, int cellsBanked) =>
            RigState.IsOwned(id)
                ? RigState.CanSpendPart(id) && cellsBanked >= UpgradeCostFor(id, RigState.Level(id))
                : RigState.IsCellUnlockable(id) && cellsBanked >= UnlockCostFor(id);

        /// <summary>MV-470: 0..1 progress toward whichever cell cost currently applies to
        /// <paramref name="id"/> — the unlock cost while unowned-and-cell-unlockable, the upgrade cost
        /// while owned-and-below-max, 0 when neither applies (nothing to accumulate toward). Feeds THE
        /// RIG board's per-node progress ring.</summary>
        public static float CellCostProgress01(string id, int cellsBanked)
        {
            if (RigState.IsOwned(id))
                return RigState.CanSpendPart(id) ? Mathf.Clamp01((float)cellsBanked / UpgradeCostFor(id, RigState.Level(id))) : 0f;
            return RigState.IsCellUnlockable(id) ? Mathf.Clamp01((float)cellsBanked / UnlockCostFor(id)) : 0f;
        }

        /// <summary>MV-470: the cell price currently showing on <paramref name="id"/>'s cost tag — 0 when
        /// no cell action currently applies (owned-and-maxed, or locked).</summary>
        public static int CurrentCellCost(string id)
        {
            if (RigState.IsOwned(id))
                return RigState.CanSpendPart(id) ? UpgradeCostFor(id, RigState.Level(id)) : 0;
            return RigState.IsCellUnlockable(id) ? UnlockCostFor(id) : 0;
        }

        /// <summary>MV-520: what <paramref name="id"/> will cost whether or not it is actionable right
        /// now — unlike <see cref="CurrentCellCost"/>, this ignores <see cref="RigState.IsCellUnlockable"/>
        /// entirely, so a node gated by family lock or the parent level &gt;= 2 rule still tells the
        /// player its price instead of hiding it. Gating changes how a node LOOKS, never whether its
        /// price is legible. Still 0 for owned-and-maxed — there is truly nothing left to buy.</summary>
        public static int PotentialCellCost(string id)
        {
            if (RigState.IsOwned(id))
                return RigState.CanSpendPart(id) ? UpgradeCostFor(id, RigState.Level(id)) : 0;
            return UnlockCostFor(id);
        }
    }
}
