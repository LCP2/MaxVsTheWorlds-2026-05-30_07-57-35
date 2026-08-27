using System;
using System.Collections.Generic;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// A run's live levels across THE RIG's whole ability tree — the single node model that
    /// replaces <see cref="WeaponSystemState"/>'s four separate per-enum dictionaries. Keyed by
    /// the string ids <see cref="RigBoard"/> defines, not by enum, so a node with no legacy enum
    /// equivalent (<c>e_cel</c>, <c>e_mag</c>, the six new Sentinel axes, ...) is a
    /// first-class citizen from day one rather than a special case bolted on top.
    ///
    /// One gate, not two (schema 3, MV-436 — retires the old cap/stat split): every node can only
    /// reach level 1 via <see cref="AcquireCap"/> (a Morphing Module draft) — <see cref="RaiseLevel"/>
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
        /// Since &gt;= 2 implies &gt;= 1, anything cell-unlockable is always also reached.
        ///
        /// MV-530: the gate is <c>Level(parent) &gt;= min(2, parent's own MaxLevel)</c>, not a bare
        /// &gt;= 2 — three roots (<c>s_bal</c>, <c>u_sen</c>, <c>s_aut</c>) cap at <c>maxLevel: 1</c>,
        /// so a bare &gt;= 2 made their six children permanently unreachable. A parent that is fully
        /// maxed always satisfies the gate now, whatever its cap; a parent whose cap is &gt;= 2 still
        /// needs the full two levels, preserving MV-458's depth-before-breadth intent.</summary>
        public static bool IsCellUnlockable(string id)
        {
            string parent = RigBoard.Parent(id);
            if (string.IsNullOrEmpty(parent)) return IsCategoryUnlocked(RigBoard.Category(id));
            return Level(parent) >= Math.Min(2, RigBoard.MaxLevel(parent));
        }

        /// <summary>Raise <paramref name="id"/> by a level — the currency-agnostic model primitive
        /// every spend (a part on a legacy track/ability, cells via <see cref="CellSpend.TryUpgradeNode"/>)
        /// ultimately calls once its own currency check has passed. MV-492: renamed from
        /// <c>TrySpendPart</c> — that name implied it spent a part itself, which made it look like the
        /// same path as <see cref="CellSpend"/>'s cell spends when it is really just "raise the level,"
        /// currency already handled by the caller. A node at level 0 can NEVER be raised this way
        /// (model.rules: "parts can never unlock it") — it must go through <see cref="AcquireCap"/>
        /// first. Fails at the node's own <see cref="RigBoard.MaxLevel"/> either way.</summary>
        public static bool RaiseLevel(string id)
        {
            if (!CanSpendPart(id)) return false;
            s_levels[id] = Level(id) + 1;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Would <see cref="RaiseLevel"/> succeed right now, without spending anything? The
        /// same rule <see cref="RaiseLevel"/> enforces — an owned, below-max node — factored out so
        /// callers (THE RIG board's own upgrade-eligibility read, legacy part-spend wrappers) never
        /// have to speculatively spend-and-undo to find out.</summary>
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

        /// <summary>Every node currently at a non-zero level, id to level (MV-557: a mid-run resume
        /// checkpoint snapshot). Returns a fresh copy; the caller owns it and mutating it has no
        /// effect on live state.</summary>
        public static IReadOnlyDictionary<string, int> SnapshotLevels() => new Dictionary<string, int>(s_levels);

        /// <summary>Every category unlocked this run (MV-557: a mid-run resume checkpoint snapshot).
        /// Returns a fresh copy; the caller owns it.</summary>
        public static IReadOnlyCollection<string> SnapshotUnlockedCategories() =>
            new List<string>(s_unlockedCategories);

        /// <summary>Overwrite the whole tree from a captured checkpoint (MV-557: a mid-run resume
        /// restore, not a draft/spend) — replaces levels and unlocked categories wholesale rather than
        /// merging, since a restore always starts from <see cref="Reset"/>'s baseline in practice. Fires
        /// <see cref="Changed"/> once so live systems (e.g. <see cref="MaxWorlds.Pickups.PickupWallet"/>'s
        /// capacity readout) re-fit.
        ///
        /// MV-597: an older save can carry an id a since-retired node used to own (e.g. <c>p_prc</c>,
        /// deleted this ticket) or a level above a node's own current cap (a save from before
        /// <c>p_dmg</c>/<c>p_spr</c> were capped tighter) — a node no longer in <see cref="RigBoard"/> is
        /// dropped rather than stranded, and a level above the node's current <see cref="RigBoard.MaxLevel"/>
        /// is clamped down to it rather than persisted out of range.</summary>
        public static void RestoreSnapshot(IReadOnlyDictionary<string, int> levels, IEnumerable<string> unlockedCategories)
        {
            s_levels.Clear();
            if (levels != null)
                foreach (KeyValuePair<string, int> kv in levels)
                {
                    if (!RigBoard.Exists(kv.Key)) continue;
                    s_levels[kv.Key] = Math.Min(kv.Value, RigBoard.MaxLevel(kv.Key));
                }

            s_unlockedCategories.Clear();
            if (unlockedCategories != null)
                foreach (string category in unlockedCategories)
                    s_unlockedCategories.Add(category);

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
