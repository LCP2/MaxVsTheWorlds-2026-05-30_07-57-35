using System.Collections.Generic;

namespace MaxWorlds.Upgrades
{
    /// <summary>
    /// The unique drop table (YT-133): each of the five parts drops exactly once across the level, so
    /// a player who clears level 1 is guaranteed the complete set and never a duplicate. Power cells
    /// are separate and common (YT-131) — this only governs the parts.
    ///
    /// A plain dispenser the drop director owns: it hands out the next undispensed part until the five
    /// are gone, then reports empty. Fixed catalog order for now — deterministic and testable; a
    /// shuffle can slot in here later without touching the caller.
    /// </summary>
    public sealed class PartDropTable
    {
        private readonly Queue<PartKind> _remaining;

        public PartDropTable()
        {
            _remaining = new Queue<PartKind>(UpgradeCatalog.AllKinds);
        }

        /// <summary>How many parts are still to drop.</summary>
        public int Remaining => _remaining.Count;

        /// <summary>Whether any part is still to drop.</summary>
        public bool HasNext => _remaining.Count > 0;

        /// <summary>Take the next part to drop. Returns false once all five have been dispensed —
        /// further robot deaths drop only power cells.</summary>
        public bool TryNext(out PartKind kind)
        {
            if (_remaining.Count == 0) { kind = default; return false; }
            kind = _remaining.Dequeue();
            return true;
        }
    }
}
