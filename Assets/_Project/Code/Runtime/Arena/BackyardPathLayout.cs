using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Dimensions of the Backyard critical path (YT-38, reshaped by YT-68):
    /// patio/entry → lawn (the main fight room) → shed (the factory) → boss gate → Big Bermuda arena.
    ///
    /// YT-68: the path used to be one uniform 9 m corridor from the patio all the way to the gate.
    /// That reads as a path but plays badly as a fight — twin-stick combat needs room to
    /// circle-strafe. So the path is now a sequence of rooms with their own widths: a narrow patio
    /// that OPENS OUT into a wide lawn (the room you actually fight in), which necks back down to a
    /// gate doorway, which opens out again into the boss arena. Cover pieces inside the lawn live in
    /// <see cref="BackyardCover"/>.
    ///
    /// Pure data + invariants so the layout is unit-testable without building any GameObjects;
    /// <see cref="BackyardPath"/> turns it into greybox geometry.
    ///
    /// Z runs along the path (Max starts near z=0 and advances toward +Z). X is the room width.
    /// </summary>
    [System.Serializable]
    public struct BackyardPathLayout
    {
        public float StartZ;          // patio back wall — the player can't retreat past here
        public float LawnStartZ;      // where the patio opens out into the lawn
        public float GateZ;           // the boss gate that opens when the factory dies
        public float ArenaEndZ;       // back wall of the boss arena
        public float PatioHalfWidth;  // half-width of the entry patio (the narrow bit)
        public float LawnHalfWidth;   // half-width of the lawn — the fight room, must be roomy
        public float GateHalfWidth;   // half-width of the doorway left open in the gate wall
        public float ArenaHalfWidth;  // half-width of the boss arena
        public float WallHeight;
        public float WallThickness;

        /// <summary>The shipped Backyard slice path. A 24×27 m lawn you can genuinely circle in
        /// (was a 9 m-wide corridor), with the shed/factory at its far end, then a doorway into a
        /// 30×22 m boss arena.</summary>
        public static BackyardPathLayout Default => new BackyardPathLayout
        {
            StartZ = -14f,
            LawnStartZ = -5f,
            GateZ = 22f,
            ArenaEndZ = 44f,
            PatioHalfWidth = 5.5f,
            LawnHalfWidth = 12f,
            GateHalfWidth = 4.5f,
            ArenaHalfWidth = 15f,
            WallHeight = 3.5f,
            WallThickness = 1f,
        };

        /// <summary>Minimum width for a room to count as fightable — enough for Max (~1 m) to hold a
        /// circle-strafe loop around a robot pack instead of backing down a hallway.</summary>
        public const float MinFightRoomWidth = 18f;

        public float PatioWidth => PatioHalfWidth * 2f;
        public float LawnWidth => LawnHalfWidth * 2f;
        public float ArenaWidth => ArenaHalfWidth * 2f;

        public float PatioLength => LawnStartZ - StartZ;
        public float LawnLength => GateZ - LawnStartZ;
        public float ArenaLength => ArenaEndZ - GateZ;

        public Vector3 LawnCenter => new Vector3(0f, 0f, (LawnStartZ + GateZ) * 0.5f);
        public Vector3 ArenaCenter => new Vector3(0f, 0f, (GateZ + ArenaEndZ) * 0.5f);

        /// <summary>Largest circle that fits inside the lawn — the circle-strafe loop the player
        /// gets to use. The whole point of YT-68 is that this is no longer tiny.</summary>
        public float LawnCircleRadius => Mathf.Min(LawnHalfWidth, LawnLength * 0.5f);

        /// <summary>True when the layout is a coherent, traversable path AND the lawn is a room you
        /// can actually fight in: rooms in order along Z, a lawn that opens out from the patio and
        /// is wide enough to circle in, a gate doorway that is a door (not the whole wall), and an
        /// arena beyond the gate. Used by tests and as a runtime build guard.</summary>
        public bool IsValid()
        {
            return PatioLength > 0f
                && LawnLength > 0f
                && ArenaLength > 0f
                && StartZ < LawnStartZ && LawnStartZ < GateZ && GateZ < ArenaEndZ
                && PatioWidth >= 3f                        // Max plus room to dodge
                && LawnHalfWidth > PatioHalfWidth          // the space opens out into the fight room
                && LawnWidth >= MinFightRoomWidth          // …and the fight room is roomy (YT-68)
                && GateHalfWidth > 1.5f                    // a doorway Max and the swarm fit through
                && GateHalfWidth < LawnHalfWidth           // …but still a doorway, not an open end
                && ArenaHalfWidth >= LawnHalfWidth         // the boss arena doesn't shrink the fight
                && WallHeight >= 2f;                       // tall enough to block Max and read as a wall
        }

        /// <summary>Gate width needed to seal the doorway (covers the opening plus the wall
        /// thickness on each side, so there's no gap to squeeze through).</summary>
        public float GateSealWidth => GateHalfWidth * 2f + WallThickness * 2f;

        /// <summary>True if the XZ point is inside the lawn floor (walls excluded).</summary>
        public bool IsInsideLawn(Vector3 p) =>
            Mathf.Abs(p.x) <= LawnHalfWidth && p.z >= LawnStartZ && p.z <= GateZ;
    }
}
