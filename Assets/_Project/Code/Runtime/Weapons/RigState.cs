using System;
using System.Collections.Generic;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// A run's live levels across THE RIG's whole ability tree (MV-422) — the single node model
    /// that replaces <see cref="WeaponSystemState"/>'s four separate per-enum dictionaries. Keyed by
    /// the string ids <see cref="RigBoard"/> defines, not by enum, so a node with no legacy enum
    /// equivalent (<c>e_cel</c>, <c>e_mag</c>, <c>p_prc</c>, the six new Sentinel axes, ...) is a
    /// first-class citizen from day one rather than a special case bolted on top.
    ///
    /// Two gates, matching the design board's own model.rules exactly:
    /// <list type="bullet">
    /// <item>a <see cref="RigNodeKind.Cap"/> can only reach level 1 via <see cref="AcquireCap"/> (a
    /// Morphing Module draft) — <see cref="TrySpendPart"/> can raise an already-owned cap further,
    /// but can never perform the 0-&gt;1 unlock;</item>
    /// <item>a <see cref="RigNodeKind.Stat"/> needs no draft — it is spendable via
    /// <see cref="TrySpendPart"/> from level 0 the moment it is <see cref="IsReached"/>.</item>
    /// </list>
    /// A node is reached when it has no parent, or its parent is at level &gt;= 1 — recursion isn't
    /// needed since every node's own reached-ness only ever depends on its immediate parent's level,
    /// per the design board's own rule text.
    /// </summary>
    public static class RigState
    {
        private static readonly Dictionary<string, int> s_levels = new Dictionary<string, int>();

        static RigState() => ResetLevels();

        /// <summary>Fired whenever a node's level changes (a part spend or a draft acquire), or the
        /// state is reset.</summary>
        public static event Action Changed;

        /// <summary>A node's current level, 0 if never touched this run.</summary>
        public static int Level(string id) => s_levels.TryGetValue(id, out int lvl) ? lvl : 0;

        /// <summary>True once <paramref name="id"/> is at level &gt;= 1 — the RIG's own definition of
        /// "owned".</summary>
        public static bool IsOwned(string id) => Level(id) >= 1;

        /// <summary>A node is REACHED when it has no parent, or its parent is at level &gt;= 1
        /// (model.rules, verbatim).</summary>
        public static bool IsReached(string id)
        {
            string parent = RigBoard.Parent(id);
            return string.IsNullOrEmpty(parent) || Level(parent) >= 1;
        }

        /// <summary>Spend one part to raise <paramref name="id"/> by a level. A <c>cap</c> at level 0
        /// can NEVER be raised this way (model.rules: "parts can never unlock it") — it must go
        /// through <see cref="AcquireCap"/> first. A <c>stat</c> must be <see cref="IsReached"/>.
        /// Fails at the node's own <see cref="RigBoard.MaxLevel"/> either way. Does not touch any
        /// currency — the caller (<see cref="PartSpend"/>) only actually spends a banked part once
        /// this returns true.</summary>
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
            if (RigBoard.IsCap(id))
            {
                if (level <= 0) return false; // caps only unlock via a draft, never a part
            }
            else if (!IsReached(id))
            {
                return false;
            }
            return level < RigBoard.MaxLevel(id);
        }

        /// <summary>Grant a <c>cap</c> at level 1 — a Morphing Module draft pick. Idempotent: a
        /// draft's own candidate pool (<see cref="EligibleCapIds"/>) never offers an already-owned
        /// or unreached cap, so this failing (false, no-op) should not normally happen outside a
        /// test asserting the guard itself. No-ops on a <c>stat</c> id — those need no unlock step.</summary>
        public static bool AcquireCap(string id)
        {
            if (!RigBoard.Exists(id) || !RigBoard.IsCap(id)) return false;
            if (Level(id) > 0) return false;
            if (!IsReached(id)) return false;
            s_levels[id] = 1;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Every cap that is currently a valid Morphing Module draft candidate: reached AND
        /// not already owned (model.rules, verbatim) — the full eligible pool, not a sampled draw.
        /// <see cref="RigBoard.DraftMaxCandidates"/> sampling without replacement is the caller's
        /// job (the draft-screen ticket, 3/5); this is the pure eligibility set the sample draws
        /// from.</summary>
        public static IEnumerable<string> EligibleCapIds()
        {
            foreach (string id in RigBoard.CapIds)
                if (IsReached(id) && !IsOwned(id)) yield return id;
        }

        /// <summary>Back to a fresh run's baseline: every node at <see cref="RigBoard.StartLevel"/>
        /// (today, every node 0 except <c>p_dmg</c> at 1). Fires <see cref="Changed"/> so live
        /// systems re-fit.</summary>
        public static void Reset()
        {
            ResetLevels();
            Changed?.Invoke();
        }

        private static void ResetLevels()
        {
            s_levels.Clear();
            foreach (string id in RigBoard.AllIds)
            {
                int start = RigBoard.StartLevel(id);
                if (start > 0) s_levels[id] = start;
            }
        }
    }
}
