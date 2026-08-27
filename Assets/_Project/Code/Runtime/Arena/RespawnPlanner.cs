using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>Where a death sends Max, and what it does to the arena he fell in (MV-427). Pure and
    /// unit-testable — no MonoBehaviour, no scene wiring; a caller (<see cref="WorldRunner"/>) turns
    /// this into an actual teleport/gate-reclose/area-restore.</summary>
    public readonly struct RespawnPlan
    {
        /// <summary>The 1-based area index Max lands in — the previous arena, or 0 for the entry stub
        /// when he died in Area 1 (there is no previous arena to fall back to).</summary>
        public readonly int RespawnAreaIndex;

        /// <summary>The area whose robots are wiped and respawned to their authored composition — the
        /// one Max actually died in.</summary>
        public readonly int RestoreAreaIndex;

        /// <summary>Whether the gate leading into <see cref="RestoreAreaIndex"/> should re-close and
        /// re-lock. False only for the boss-room edge case: the boss gate opens on a condition
        /// (all-sheds-destroyed), not combat, and re-closing it would be unreopenable — a softlock.</summary>
        public readonly bool RecloseGate;

        public RespawnPlan(int respawnAreaIndex, int restoreAreaIndex, bool recloseGate)
        {
            RespawnAreaIndex = respawnAreaIndex;
            RestoreAreaIndex = restoreAreaIndex;
            RecloseGate = recloseGate;
        }
    }

    /// <summary>Resolves a <see cref="RespawnPlan"/> from where Max died. MV-575: every area a death
    /// can happen in — ordinary or boss — carries a real 1-based index into the world's authored
    /// sequence; there is no synthetic index past the end of it for "the boss room" (World 1 places
    /// three bosses, at areas 12, 20 and 30, not one at <c>areaCount + 1</c>). Both edge cases the
    /// ticket calls out by name (Area 1, a boss area) fall out of the same two rules: land one area
    /// back, and never re-close a condition-gated door.</summary>
    public static class RespawnPlanner
    {
        /// <summary><paramref name="deathAreaIndex"/> is the 1-based area Max died in.
        /// <paramref name="deathGateIsConditionGated"/> is whether the gate leading INTO that area
        /// opens on a condition (<c>all-sheds-destroyed</c> / <c>sheds-destroyed-before</c>) rather
        /// than combat — the caller (<see cref="WorldRunner"/>) knows this from the area's own
        /// <c>WorldArea.role</c>, not from where the area sits in the sequence. Whether a gate may be
        /// re-closed is a property of the AREA, not its index.</summary>
        public static RespawnPlan Resolve(int deathAreaIndex, bool deathGateIsConditionGated)
        {
            // Area 1 has no previous arena — fall back to the entry stub (index 0). Every other area
            // falls back exactly one area, boss areas included.
            int respawnArea = Mathf.Max(0, deathAreaIndex - 1);
            return new RespawnPlan(respawnArea, deathAreaIndex, recloseGate: !deathGateIsConditionGated);
        }
    }
}
