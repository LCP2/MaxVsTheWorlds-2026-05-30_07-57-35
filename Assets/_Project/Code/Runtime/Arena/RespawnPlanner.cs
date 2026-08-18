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

    /// <summary>Resolves a <see cref="RespawnPlan"/> from where Max died. The three edge cases the
    /// ticket calls out by name (Area 1, the boss room, an ordinary mid-run area) all fall out of the
    /// same two rules: land one area back, and never re-close a condition-gated door.</summary>
    public static class RespawnPlanner
    {
        /// <summary><paramref name="deathAreaIndex"/> is the 1-based area Max died in (a normal area
        /// is 1..<paramref name="areaCount"/>; the boss room is <paramref name="areaCount"/> + 1).
        /// <paramref name="areaCount"/> is the world's authored combat-area count
        /// (<c>WorldConfig.dials.areaCount</c>, 18 for World 1).</summary>
        public static RespawnPlan Resolve(int deathAreaIndex, int areaCount)
        {
            int bossAreaIndex = areaCount + 1;

            if (deathAreaIndex >= bossAreaIndex)
            {
                // The boss room: fall back one area, but the boss gate — opened on a condition, not
                // broken by fire — must never re-close (edge case 2: it would be unreopenable).
                return new RespawnPlan(areaCount, deathAreaIndex, recloseGate: false);
            }

            // Area 1 has no previous arena — fall back to the entry stub (index 0), still gated by
            // the same combat gate every other area's fallback recloses (edge case 1).
            int respawnArea = Mathf.Max(0, deathAreaIndex - 1);
            return new RespawnPlan(respawnArea, deathAreaIndex, recloseGate: true);
        }
    }
}
