using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Dimensions of the hand-built Backyard critical path (YT-38, spec §4.6):
    /// patio/entry → lawn (open fight) → shed (the factory) → boss gate → Big Bermuda arena.
    /// A straight corridor that opens into a wider boss arena. Pure data + invariants so the
    /// layout (lane width, gate placement, arena bounds) is unit-testable without building any
    /// GameObjects; <see cref="BackyardPath"/> turns it into greybox geometry.
    ///
    /// Z runs along the path (Max starts near z=0 and advances toward +Z). X is the lane width.
    /// </summary>
    [System.Serializable]
    public struct BackyardPathLayout
    {
        public float StartZ;         // patio back wall — the player can't retreat past here
        public float GateZ;          // the boss gate that opens when the factory dies
        public float ArenaEndZ;      // back wall of the boss arena
        public float LaneHalfWidth;  // half-width of the corridor (patio→gate)
        public float ArenaHalfWidth; // half-width of the boss arena (opens out from the lane)
        public float WallHeight;
        public float WallThickness;

        /// <summary>The shipped Backyard slice path. Factory sits at z≈10, gate at z=18,
        /// boss at z≈26 — this frames a corridor around them and an arena beyond.</summary>
        public static BackyardPathLayout Default => new BackyardPathLayout
        {
            StartZ = -8f,
            GateZ = 18f,
            ArenaEndZ = 38f,
            LaneHalfWidth = 4.5f,
            ArenaHalfWidth = 13f,
            WallHeight = 3.5f,
            WallThickness = 1f,
        };

        public float LaneWidth => LaneHalfWidth * 2f;
        public float CorridorLength => GateZ - StartZ;
        public float ArenaLength => ArenaEndZ - GateZ;
        public Vector3 ArenaCenter => new Vector3(0f, 0f, (GateZ + ArenaEndZ) * 0.5f);

        /// <summary>True when the layout is a coherent, traversable path: a real corridor that a
        /// player (and enemies) can move down, a gate that fully spans the lane, and an arena that
        /// sits beyond the gate and is wider than the lane. Used by tests and as a build guard.</summary>
        public bool IsValid(float minPlayerLane = 3f)
        {
            return CorridorLength > 0f
                && ArenaLength > 0f
                && LaneWidth >= minPlayerLane
                && ArenaHalfWidth > LaneHalfWidth   // the space opens out into the arena
                && WallHeight >= 2f                 // tall enough to block Max and read as a wall
                && GateZ > StartZ && GateZ < ArenaEndZ;
        }

        /// <summary>Gate width needed to seal the lane (covers the opening plus the wall thickness
        /// on each side, so there's no gap to squeeze through).</summary>
        public float GateSealWidth => LaneWidth + WallThickness * 2f;
    }
}
