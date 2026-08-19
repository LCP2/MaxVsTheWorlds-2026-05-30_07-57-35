using System;
using System.Collections.Generic;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// A run's live levels across THE RIG's whole ability tree — the single node model that
    /// replaces <see cref="WeaponSystemState"/>'s four separate per-enum dictionaries. Keyed by
    /// the string ids <see cref="RigBoard"/> defines, not by enum, so a node with no legacy enum
    /// equivalent (<c>e_cel</c>, <c>e_mag</c>, <c>p_prc</c>, the six new Sentinel axes, ...) is a
    /// first-class citizen from day one rather than a special case bolted on top.
    ///
    /// One gate, not two (schema 3, MV-436 — retires the old cap/stat split): every node can only
    /// reach level 1 via <see cref="AcquireCap"/> (a Morphing Module draft) — <see cref="TrySpendPart"/>
    /// can raise an already-owned node further, but can never perform the 0-&gt;1 unlock itself.
    /// <see cref="IsReached"/> still gates which unowned nodes a draft may offer; it no longer confers
    /// any spendability of its own now that every node needs a draft regardless.
    ///
    /// A node is reached when it has no parent, or its parent is at level &gt;= 1 — recursion isn't
    /// needed since every node's own reached-ness only ever depends on its immediate parent's level,
    /// per the design board's own rule text.
    /// </summary>
    public static class RigState
    {
        private static readonly Dictionary<string, int> s_levels = new Dictionary<string, int>();

        /// <summary>Categories a shed has unlocked this run (MV-457) — a root node (no parent) is only
        /// REACHED once its own category is in here; PRIMARY starts unlocked (whichever category owns
        /// the run-start ability, today <c>p_dmg</c>) and every other category starts locked, opened
        /// only by <see cref="UnlockCategory"/>.</summary>
        private static readonly HashSet<string> s_unlockedCategories = new HashSet<string>();

        static RigState() => ResetLevels();

        /// <summary>Fired whenever a node's level changes (a part spend or a draft acquire), a category
        /// unlocks, or the state is reset.</summary>
        public static event Action Changed;

        /// <summary>A node's current level, 0 if never touched this run.</summary>
        public static int Level(string id) => s_levels.TryGetValue(id, out int lvl) ? lvl : 0;

        /// <summary>True once <paramref name="id"/> is at level &gt;= 1 — the RIG's own definition of
        /// "owned".</summary>
        public static bool IsOwned(string id) => Level(id) >= 1;

        /// <summary>True once a shed has unlocked <paramref name="category"/> this run (MV-457).</summary>
        public static bool IsCategoryUnlocked(string category) => s_unlockedCategories.Contains(category);

        /// <summary>Every category not yet unlocked — the pool a shed's Morphing Module draws its two
        /// family candidates from (<see cref="RigDraft.DrawCandidateCategories"/>).</summary>
        public static IEnumerable<string> LockedCategoryIds()
        {
            foreach (string category in RigBoard.AllCategoryIds)
                if (!IsCategoryUnlocked(category)) yield return category;
        }

        /// <summary>Unlocks <paramref name="category"/> — a shed's Morphing Module draft pick (MV-457).
        /// Idempotent: unlocking an already-open category is a no-op (false, fires nothing).</summary>
        public static bool UnlockCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return false;
            if (!s_unlockedCategories.Add(category)) return false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>A node is REACHED when it has no parent, or its parent is at level &gt;= 1
        /// (model.rules) — with MV-457's own addition: a ROOT node also needs its own category
        /// unlocked. A non-root node's reached-ness still depends only on its immediate parent's level,
        /// exactly as before — a shed unlocking a category never skips the tree's own parent gating.</summary>
        public static bool IsReached(string id)
        {
            string parent = RigBoard.Parent(id);
            if (string.IsNullOrEmpty(parent)) return IsCategoryUnlocked(RigBoard.Category(id));
            return Level(parent) >= 1;
        }

        /// <summary>MV-458: the gate for the CELLS-funded 0-&gt;1 unlock (<see cref="CellSpend.TryUnlockNode"/>)
        /// — a root node still only needs its own category unlocked, same as <see cref="IsReached"/>,
        /// but a non-root node now needs its PARENT at level &gt;= 2, tightened from the level &gt;= 1
        /// <see cref="IsReached"/> uses. <see cref="IsReached"/> itself is untouched: it still gates the
        /// Morphing Module draft pool (<see cref="EligibleCapIds"/>), which this ticket doesn't touch.
        /// Since &gt;= 2 implies &gt;= 1, anything cell-unlockable is always also reached.</summary>
        public static bool IsCellUnlockable(string id)
        {
            string parent = RigBoard.Parent(id);
            if (string.IsNullOrEmpty(parent)) return IsCategoryUnlocked(RigBoard.Category(id));
            return Level(parent) >= 2;
        }

        /// <summary>Spend one part to raise <paramref name="id"/> by a level. A node at level 0 can
        /// NEVER be raised this way (model.rules: "parts can never unlock it") — it must go through
        /// <see cref="AcquireCap"/> first. Fails at the node's own <see cref="RigBoard.MaxLevel"/>
        /// either way. Does not touch any currency — the caller (<see cref="PartSpend"/>) only
        /// actually spends a banked part once this returns true.</summary>
        public static bool TrySpendPart(string id)
        {
            if (!CanSpendPart(id)) return false;
            s_levels[id] = Level(id) + 1;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Would <see cref="TrySpendPart"/> succeed right now, without spending anything? The
        /// same rule <see cref="TrySpendPart"/> enforces, factored out so THE RIG board (MV-423) can
        /// show its amber "+" badge on exactly the nodes a part would actually raise, without a
        /// speculative spend-and-undo.</summary>
        public static bool CanSpendPart(string id)
        {
            if (!RigBoard.Exists(id)) return false;
            int level = Level(id);
            if (level <= 0) return false; // every node only unlocks via a draft, never a part
            return level < RigBoard.MaxLevel(id);
        }

        /// <summary>Grant a node at level 1 — a Morphing Module draft pick. Idempotent: a draft's own
        /// candidate pool (<see cref="EligibleCapIds"/>) never offers an already-owned or unreached
        /// node, so this failing (false, no-op) should not normally happen outside a test asserting
        /// the guard itself.</summary>
        public static bool AcquireCap(string id)
        {
            if (!RigBoard.Exists(id)) return false;
            if (Level(id) > 0) return false;
            if (!IsReached(id)) return false;
            s_levels[id] = 1;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Every node that is currently a valid Morphing Module draft candidate: reached AND
        /// not already owned (model.rules, verbatim) — the full eligible pool, not a sampled draw.
        /// <see cref="RigBoard.DraftMaxCandidates"/> sampling without replacement is the caller's
        /// job (the draft-screen ticket, 3/5); this is the pure eligibility set the sample draws
        /// from.</summary>
        public static IEnumerable<string> EligibleCapIds()
        {
            foreach (string id in RigBoard.AllIds)
                if (IsReached(id) && !IsOwned(id)) yield return id;
        }

        /// <summary>Back to a fresh run's baseline: every node at <see cref="RigBoard.StartLevel"/>
        /// (today, every node 0 except <c>p_dmg</c> at 1), and only the category that run-start ability
        /// belongs to (PRIMARY) unlocked — every other category starts locked, opened only by a shed
        /// (MV-457). Fires <see cref="Changed"/> so live systems re-fit.</summary>
        public static void Reset()
        {
            ResetLevels();
            Changed?.Invoke();
        }

        private static void ResetLevels()
        {
            s_levels.Clear();
            s_unlockedCategories.Clear();
            foreach (string id in RigBoard.AllIds)
            {
                int start = RigBoard.StartLevel(id);
                if (start > 0)
                {
                    s_levels[id] = start;
                    s_unlockedCategories.Add(RigBoard.Category(id));
                }
            }
        }
    }
}
