using System.Collections.Generic;

namespace MaxWorlds.Upgrades
{
    /// <summary>
    /// The unique drop table (YT-133): each of the seven parts drops exactly once across the level, so
    /// a player who clears level 1 is guaranteed the complete set and never a duplicate. Power cells
    /// are separate and common (YT-131) — this only governs the parts.
    ///
    /// A plain dispenser the drop director owns: it hands out the next undispensed part until they're
    /// gone, then reports empty. Fixed catalog order for now — deterministic and testable; a shuffle
    /// can slot in here later without touching the caller.
    ///
    /// The draft-pick reveal (YT-207) additionally needs to PEEK ahead without committing — the
    /// player previews a couple of upcoming parts as extra candidates alongside the one already
    /// collected, and only the one actually chosen leaves the pool. <see cref="PeekNext"/> and
    /// <see cref="Commit"/> are additive: <see cref="TryNext"/> keeps its original dequeue-now
    /// behaviour for the drop-per-kill cadence in <c>PickupDirector</c>.
    /// </summary>
    public sealed class PartDropTable
    {
        private readonly List<PartKind> _remaining;

        public PartDropTable()
        {
            _remaining = new List<PartKind>(UpgradeCatalog.AllKinds);
        }

        /// <summary>How many parts are still to drop.</summary>
        public int Remaining => _remaining.Count;

        /// <summary>Whether any part is still to drop.</summary>
        public bool HasNext => _remaining.Count > 0;

        /// <summary>Take the next part to drop. Returns false once every part has been dispensed —
        /// further robot deaths drop only power cells.</summary>
        public bool TryNext(out PartKind kind)
        {
            if (_remaining.Count == 0) { kind = default; return false; }
            kind = _remaining[0];
            _remaining.RemoveAt(0);
            return true;
        }

        /// <summary>Peek up to <paramref name="count"/> of the still-undispensed parts, front-first,
        /// WITHOUT removing them (YT-207) — the draft-pick reveal's extra preview candidates. Returns
        /// fewer than requested once the pool is running low, and an empty array once it's empty.</summary>
        public PartKind[] PeekNext(int count)
        {
            int n = count < _remaining.Count ? count : _remaining.Count;
            if (n <= 0) return System.Array.Empty<PartKind>();
            var result = new PartKind[n];
            for (int i = 0; i < n; i++) result[i] = _remaining[i];
            return result;
        }

        /// <summary>Remove a specific still-undispensed part from the pool (YT-207) — called once the
        /// player installs a previewed candidate ahead of its natural drop turn, so it can never be
        /// offered or installed again. Returns false (a no-op) if it isn't in the pool, e.g. a
        /// double-commit.</summary>
        public bool Commit(PartKind kind) => _remaining.Remove(kind);
    }
}
