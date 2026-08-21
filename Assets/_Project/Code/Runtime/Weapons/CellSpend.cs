using MaxWorlds.Pickups;
using UnityEngine;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Spends banked POWER CELLS on THE RIG board (MV-458) — cells are the primary progression
    /// currency for both a node's 0-&gt;1 unlock and its further levels. MV-492: unlocking additionally
    /// needs 1 banked PART — Lee's model, "a part is one piece of the puzzle" for a NEW ability — while
    /// upgrading an already-owned node stays cells-only. Same "check the sink can accept it BEFORE
    /// touching the bank" order every other spend in this codebase follows.
    /// </summary>
    public static class CellSpend
    {
        /// <summary>Cost to unlock a new node — its 0-&gt;1 grant — with cells. MV-511: flat, does not
        /// escalate — unlock breadth is already gated twice over by parts scarcity and the parent
        /// level &gt;= 2 requirement (<see cref="RigState.IsCellUnlockable"/>).</summary>
        public const int UnlockCostCells = 10;

        /// <summary>MV-492: the part half of a node's 0-&gt;1 unlock — "a part is one piece of the
        /// puzzle," never enough on its own (see <see cref="UnlockCostCells"/> for the other half), and
        /// never sufficient to raise an already-owned node (that's cells-only, see
        /// <see cref="UpgradeCostFor"/>).</summary>
        public const int UnlockCostParts = 1;

        /// <summary>MV-511: the base cell cost of an upgrade, before the per-level escalation
        /// <see cref="UpgradeCostFor"/> applies. Deliberately not named with the old flat constant's
        /// exact "UpgradeCost" + "Cells" prefix run-on — AC6's own grep for that retired declaration
        /// must not false-positive on this one.</summary>
        public const int UpgradeCostBaseCells = 5;

        /// <summary>MV-511: the node level past which the escalation stops rising — set by
        /// <see cref="MaxWorlds.Pickups.PickupWallet.DefaultCapacity"/> (20): uncapped,
        /// <c>UpgradeCostBaseCells * level</c> would put the level 5-&gt;6 upgrade (25 cells) out of
        /// reach of a base wallet, stranding a player who hasn't unlocked the ENERGY family (and
        /// therefore can't reach <c>e_cel</c>) below level 5 on every node forever.</summary>
        public const int UpgradeCostEscalationCap = 4;

        /// <summary>Cost to raise a node currently at <paramref name="level"/> by one level with cells —
        /// 5, 10, 15, 20, 20, 20... for levels 1..6 and beyond, escalating by the node's OWN level (a
        /// within-build depth-vs-breadth trade the player makes) rather than by world/area progress,
        /// which would tax advancement the way <see cref="MaxWorlds.Weapons.RigState"/>'s design
        /// deliberately avoids (see MV-511 for the full reasoning). Capped at
        /// <see cref="UpgradeCostEscalationCap"/> so every single upgrade stays affordable on
        /// <see cref="MaxWorlds.Pickups.PickupWallet.DefaultCapacity"/> alone.</summary>
        public static int UpgradeCostFor(int level) => UpgradeCostBaseCells * Mathf.Min(level, UpgradeCostEscalationCap);

        /// <summary>Unlock <paramref name="id"/> for <see cref="UnlockCostCells"/> cells AND
        /// <see cref="UnlockCostParts"/> part. Requires <see cref="RigState.IsCellUnlockable"/> (its
        /// category unlocked and, for a non-root node, its parent at level &gt;= 2). No special case for
        /// <c>e_cel</c> or any other node — every RIG node unlocks the same way now. Every precondition
        /// (both currencies banked, the node eligible) is checked before either currency is touched, so
        /// a failed unlock never leaves a partial spend behind; both currencies then commit together.
        /// Grants through <see cref="WeaponSystemState.AcquireById"/> (MV-435 AC5's own sanctioned
        /// entry point) rather than calling the RIG model layer's own grant primitive directly — that
        /// would silently skip <see cref="WeaponSystemState.Changed"/> and leave any HUD control gated
        /// on it stuck stale.</summary>
        public static bool TryUnlockNode(string id)
        {
            if (PickupWallet.PartsBanked < UnlockCostParts) return false;
            if (PickupWallet.PowerCells < UnlockCostCells) return false;
            if (!RigState.IsCellUnlockable(id)) return false;
            if (!WeaponSystemState.AcquireById(id)) return false;
            PickupWallet.TrySpendPart();
            PickupWallet.TrySpendPowerCells(UnlockCostCells);
            return true;
        }

        /// <summary>Raise an already-owned <paramref name="id"/> by one level for
        /// <see cref="UpgradeCostFor"/>'s current-level price in cells — never touches banked parts,
        /// however many are sitting there. The same "unowned/locked items can't be upgraded" gate every
        /// other spend in this codebase enforces (<see cref="RigState.CanSpendPart"/>'s own owned/below-cap
        /// check, via <see cref="RigState.RaiseLevel"/> — the currency-agnostic "raise one level"
        /// primitive both the legacy part-spend wrappers and this class raise against). The cost is
        /// read off the node's level BEFORE <see cref="RigState.RaiseLevel"/> advances it — a level 1
        /// node's own upgrade costs <see cref="UpgradeCostFor"/>(1), not the level it is about to
        /// become.</summary>
        public static bool TryUpgradeNode(string id)
        {
            int cost = UpgradeCostFor(RigState.Level(id));
            if (PickupWallet.PowerCells < cost) return false;
            if (!RigState.RaiseLevel(id)) return false;
            PickupWallet.TrySpendPowerCells(cost);
            return true;
        }

        /// <summary>MV-470: the CELLS-only half of a node's affordability — owned checks the upgrade
        /// path, unowned checks the unlock path. Pure, so THE RIG board can drive its per-node "live vs
        /// inert" read (a pulsing ring/badge vs a flat one) without a speculative spend-and-undo, same
        /// idiom as <see cref="RigState.CanSpendPart"/>. Deliberately narrower than
        /// <c>WeaponsScreen.IsAbilityNodeSpendable</c> (which, on the unlock path, also requires
        /// <see cref="UnlockCostParts"/>) — this is only ever asked "would CELLS alone pay for this
        /// right now."</summary>
        public static bool IsCellActionAffordable(string id, int cellsBanked) =>
            RigState.IsOwned(id)
                ? RigState.CanSpendPart(id) && cellsBanked >= UpgradeCostFor(RigState.Level(id))
                : RigState.IsCellUnlockable(id) && cellsBanked >= UnlockCostCells;

        /// <summary>MV-470: 0..1 progress toward whichever cell cost currently applies to
        /// <paramref name="id"/> — the unlock cost while unowned-and-cell-unlockable, the upgrade cost
        /// while owned-and-below-max, 0 when neither applies (nothing to accumulate toward). Feeds THE
        /// RIG board's per-node progress ring.</summary>
        public static float CellCostProgress01(string id, int cellsBanked)
        {
            if (RigState.IsOwned(id))
                return RigState.CanSpendPart(id) ? Mathf.Clamp01((float)cellsBanked / UpgradeCostFor(RigState.Level(id))) : 0f;
            return RigState.IsCellUnlockable(id) ? Mathf.Clamp01((float)cellsBanked / UnlockCostCells) : 0f;
        }

        /// <summary>MV-470: the cell price currently showing on <paramref name="id"/>'s cost tag — 0 when
        /// no cell action currently applies (owned-and-maxed, or locked).</summary>
        public static int CurrentCellCost(string id)
        {
            if (RigState.IsOwned(id))
                return RigState.CanSpendPart(id) ? UpgradeCostFor(RigState.Level(id)) : 0;
            return RigState.IsCellUnlockable(id) ? UnlockCostCells : 0;
        }
    }
}
