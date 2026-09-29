using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>MV-997: an extra doorway cut into a boundary wall for a world's own finale exit --
    /// unlike an ordinary <see cref="MapLink"/> doorway, this has no "to" zone inside this map at all:
    /// the corridor it opens onto lives entirely outside this <see cref="WorldConfig"/>, built later by
    /// <see cref="MaxWorlds.Intro.WorldJoinSequence"/>. Resolved once by <see cref="WorldTransitions.ApplyExitDoorway"/>
    /// before <see cref="MapRuntime.Build"/> ever runs <see cref="MapGeometry.Walls"/>, and consumed
    /// there exactly like any other link's hole -- so the wall's collider, its mesh, and every dressing
    /// kit that reads <see cref="MapGeometry.Faces"/>/<see cref="MapGeometry.Walls"/> (the fence line,
    /// <c>StormdrainDressing</c>'s own wall props) all cut the identical gap for free, with no separate
    /// "and also skip this bit" pass anywhere.</summary>
    public readonly struct ExitDoorway
    {
        public readonly Wall Wall;
        public readonly float Coord;
        public readonly Span Hole;

        public ExitDoorway(Wall wall, float coord, Span hole)
        {
            Wall = wall; Coord = coord; Hole = hole;
        }

        /// <summary>Matches <see cref="WallSegment.AlongX"/>'s own convention: true for a wall sitting
        /// on a constant-Z line (N/S), false for one on a constant-X line (E/W).</summary>
        public bool AlongX => Wall == Wall.N || Wall == Wall.S;
    }

    /// <summary>
    /// MV-964: one authored row per world that has a next world to walk into — which wall its finale
    /// door cuts, where along that wall, how long the corridor is, and where it lands in the next
    /// world's own entry stub. Everything else (the door's world-space mouth, the corridor's footprint,
    /// the arrival shell's footprint) is RESOLVED from the real <see cref="WorldConfig"/> geometry, never
    /// authored twice — so a new world only ever needs one new row here, never a bespoke sequence class.
    ///
    /// The exit AREA itself is derived, never authored: the config's own boss-role area at
    /// <c>dials.areaCount</c> (World 1's a30, World 2's a21) — see <see cref="ExitArea"/>.
    /// </summary>
    public sealed class WorldTransitionEntry
    {
        /// <summary>The door opening's width, and the corridor's own interior width (MV-964's table).</summary>
        public const float DoorWidth = 3f;
        public const float CorridorWidth = 3f;

        public readonly int FromWorld;
        public readonly Wall ExitWall;
        public readonly float ExitDoorPos;
        public readonly float CorridorLength;

        /// <summary>Distance from the exit door, along the corridor, at which segment A gives way to B
        /// (MV-964 §5) — <see cref="BiomePalette.ForWorld(int)"/> of <see cref="FromWorld"/> holds
        /// through segment A, the 50/50 lerp holds through B, and the destination world's palette holds
        /// from here to the corridor's end (segment C).</summary>
        public readonly float SegmentAEnd;

        /// <summary>Distance from the exit door at which segment B gives way to C.</summary>
        public readonly float SegmentBEnd;

        public readonly Wall ArrivalWall;
        public readonly float ArrivalDoorPos;
        public readonly float ArrivalShellLength;

        public WorldTransitionEntry(int fromWorld, Wall exitWall, float exitDoorPos, float corridorLength,
            float segmentAEnd, float segmentBEnd, Wall arrivalWall, float arrivalDoorPos, float arrivalShellLength)
        {
            FromWorld = fromWorld;
            ExitWall = exitWall;
            ExitDoorPos = exitDoorPos;
            CorridorLength = corridorLength;
            SegmentAEnd = segmentAEnd;
            SegmentBEnd = segmentBEnd;
            ArrivalWall = arrivalWall;
            ArrivalDoorPos = arrivalDoorPos;
            ArrivalShellLength = arrivalShellLength;
        }

        /// <summary>The exit area is derived, never authored: the boss-role area at the config's own
        /// last combat index.</summary>
        public WorldArea ExitArea(WorldConfig fromCfg) =>
            fromCfg?.dials == null ? null : fromCfg.AreaByIndex(fromCfg.dials.areaCount);

        /// <summary>The next world's own entry stub — where the corridor lands.</summary>
        public WorldArea ArrivalArea(WorldConfig toCfg) => toCfg?.Area("stub");

        public Vector2 ExitDoorMouth(WorldConfig fromCfg)
        {
            WorldArea area = ExitArea(fromCfg);
            return area == null ? default : DoorMouth(area, ExitWall, ExitDoorPos);
        }

        public Vector2 ArrivalDoorMouth(WorldConfig toCfg)
        {
            WorldArea area = ArrivalArea(toCfg);
            return area == null ? default : DoorMouth(area, ArrivalWall, ArrivalDoorPos);
        }

        /// <summary>The corridor's world-space footprint: <see cref="CorridorLength"/> outward from the
        /// exit door, <see cref="CorridorWidth"/> plus both walls' thickness across.</summary>
        public Rect CorridorFootprint(WorldConfig fromCfg, float wallThickness)
        {
            WorldArea area = ExitArea(fromCfg);
            return area == null ? default
                : Footprint(area, ExitWall, ExitDoorPos, CorridorLength, CorridorWidth + wallThickness * 2f);
        }

        /// <summary>The arrival shell's world-space footprint, built OUTWARD from the destination stub's
        /// own wall — it only ever touches that wall's edge, never the stub's interior.</summary>
        public Rect ArrivalFootprint(WorldConfig toCfg, float wallThickness)
        {
            WorldArea area = ArrivalArea(toCfg);
            return area == null ? default
                : Footprint(area, ArrivalWall, ArrivalDoorPos, ArrivalShellLength, CorridorWidth + wallThickness * 2f);
        }

        /// <summary>Internal rather than private (MV-1013): <see cref="WorldTransitions.ApplyExitDoorway"/>
        /// needs this same area/wall/pos -&gt; world-space mouth resolution for the last world's own
        /// finale-only door, which has no full <see cref="WorldTransitionEntry"/> to ask.</summary>
        internal static Vector2 DoorMouth(WorldArea area, Wall wall, float doorPos)
        {
            Span span = area.WallSpan(wall);
            float along = Mathf.Lerp(span.Min, span.Max, doorPos);
            float wallCoord = area.WallCoord(wall);
            return area.WallRunsAlongX(wall) ? new Vector2(along, wallCoord) : new Vector2(wallCoord, along);
        }

        /// <summary>A rect extending OUTWARD from an area's wall (N/E extend toward +Z/+X, S/W toward
        /// -Z/-X — the same "which side is out" convention <see cref="WorldArea.WallCoord"/> already
        /// uses), so it can never overlap that area's own interior, only touch its wall's edge.</summary>
        private static Rect Footprint(WorldArea area, Wall wall, float doorPos, float length, float crossWidth)
        {
            Span span = area.WallSpan(wall);
            float along = Mathf.Lerp(span.Min, span.Max, doorPos);
            float wallCoord = area.WallCoord(wall);
            bool outwardIsPositive = wall == Wall.N || wall == Wall.E;
            float outMin = outwardIsPositive ? wallCoord : wallCoord - length;

            return area.WallRunsAlongX(wall)
                ? new Rect(along - crossWidth * 0.5f, outMin, crossWidth, length)
                : new Rect(outMin, along - crossWidth * 0.5f, length, crossWidth);
        }
    }

    /// <summary>The design guarantee this ticket exists for: a world without a row here has no way into
    /// its next one, and <c>WorldTransitionCoverageTests</c> fails loudly, by name, the moment one is
    /// missing — adding a World 4 to <see cref="WorldLibrary.Keys"/> with no matching row here is caught
    /// by that test, not discovered in a build.</summary>
    public static class WorldTransitions
    {
        private static readonly WorldTransitionEntry[] Entries =
        {
            // World 1 (Backyard) -> World 2 (Stormdrain): a30's own E wall into World 2's entry stub,
            // W wall (MV-997: was N -- every arrival enters heading east, so every exit must too, or
            // the corridor's own direction change breaks the continuity it exists for).
            new WorldTransitionEntry(fromWorld: 0, exitWall: Wall.E, exitDoorPos: 0.5f, corridorLength: 30f,
                segmentAEnd: 9f, segmentBEnd: 13f,
                arrivalWall: Wall.W, arrivalDoorPos: 0.5f, arrivalShellLength: 10f),

            // World 2 (Stormdrain) -> World 3 (Reef): a21's own E wall (its N wall abuts a5) into
            // World 3's entry stub, W wall.
            new WorldTransitionEntry(fromWorld: 1, exitWall: Wall.E, exitDoorPos: 0.5f, corridorLength: 30f,
                segmentAEnd: 10f, segmentBEnd: 16f,
                arrivalWall: Wall.W, arrivalDoorPos: 0.5f, arrivalShellLength: 10f),
        };

        /// <summary>The transition out of <paramref name="fromWorld"/>, or null when it's the last world
        /// — <see cref="WorldLibrary.Count"/> - 1 always resolves null, by construction.</summary>
        public static WorldTransitionEntry For(int fromWorld)
        {
            foreach (WorldTransitionEntry e in Entries)
                if (e.FromWorld == fromWorld) return e;
            return null;
        }

        /// <summary>Set by <see cref="MaxWorlds.UI.RunFlow.StartNextWorld"/> just before the reload that
        /// boots the destination world; consumed and cleared by whatever builds that world's arrival
        /// shell on its own first frame (<see cref="MaxWorlds.Intro.WorldJoinSequence"/>). Null means "no
        /// arrival in progress" — PLAY, RESUME, a Home world button and a respawn all boot with this
        /// null, exactly as before this ticket.</summary>
        public static int? PendingArrivalFrom { get; set; }

        /// <summary>MV-1013: the last world's own finale exit door — real map geometry cut at boot
        /// exactly like a <see cref="WorldTransitionEntry"/>'s, but with no corridor to build behind it:
        /// there is no next world to walk into, so <see cref="MaxWorlds.VFX.WorldFinaleGate"/> opens this
        /// gate directly and watches for Max crossing it itself, rather than handing off to
        /// <see cref="MaxWorlds.Intro.WorldJoinSequence"/>. Kept out of <see cref="Entries"/> on purpose
        /// — folding a corridor-less row in there would make every corridor-only reader of <see cref="For"/>
        /// (and <see cref="WorldTransitionCoverageTests"/>, which holds every world WITH a next world to
        /// its own row) special-case a row with no corridor fields to speak of.</summary>
        private static readonly (int WorldIndex, Wall Wall, float DoorPos)[] FinaleOnlyDoors =
        {
            // World 3 (Reef): a30's E wall (34x34, origin 586,0) — same "exit runs east" convention every
            // other world's row already follows. NOT the wall's exact centre (0.5, z 17): Anchorhead sits
            // there too (603, 17, dead centre of the room) with a flanking cover pair on every wall at
            // its own centreline (a30_c17..c20 in world3_config.json) — a door cut through the centre
            // would open right behind a solid cover box. 0.3 (z 10.2) clears that piece, the south
            // corner cluster (c5-c8, z up to 5.8) and both wall ends, with margin to spare against
            // WorldTransitionCoverageTests' own clearances (2m from cover, 1.9m from a wall end) even
            // though that test doesn't cover this table.
            (2, Wall.E, 0.3f),
        };

        /// <summary>MV-997: resolves this world's own finale exit doorway against the real config and
        /// stamps it onto <paramref name="map"/> — BEFORE <see cref="MapRuntime.Build"/> ever runs
        /// <see cref="MapGeometry.Walls"/> — so the exit door becomes real map geometry present from boot
        /// instead of a runtime <c>CutWallGap</c>. MV-1013: every world gets one now, either from its own
        /// <see cref="WorldTransitionEntry"/> row (a next world to advance into) or from
        /// <see cref="FinaleOnlyDoors"/> (the last world, no corridor). A no-op (<c>map.exitDoorway</c>
        /// stays null) only if neither table carries this <paramref name="worldIndex"/>, or if
        /// <paramref name="cfg"/> can't resolve a real exit area.</summary>
        public static void ApplyExitDoorway(MapData map, WorldConfig cfg, int worldIndex)
        {
            if (map == null || cfg?.dials == null) return;

            Wall wall; float doorPos;
            WorldTransitionEntry entry = For(worldIndex);
            if (entry != null) { wall = entry.ExitWall; doorPos = entry.ExitDoorPos; }
            else if (!TryFinaleOnlyDoor(worldIndex, out wall, out doorPos)) return;

            WorldArea exitArea = cfg.AreaByIndex(cfg.dials.areaCount);
            if (exitArea == null) return;

            Vector2 doorMouth = WorldTransitionEntry.DoorMouth(exitArea, wall, doorPos);
            bool alongX = exitArea.WallRunsAlongX(wall);
            float coord = exitArea.WallCoord(wall);
            float along = alongX ? doorMouth.x : doorMouth.y;
            float half = WorldTransitionEntry.DoorWidth * 0.5f;

            map.exitDoorway = new ExitDoorway(wall, coord, new Span(along - half, along + half));
        }

        private static bool TryFinaleOnlyDoor(int worldIndex, out Wall wall, out float doorPos)
        {
            foreach (var d in FinaleOnlyDoors)
            {
                if (d.WorldIndex != worldIndex) continue;
                wall = d.Wall;
                doorPos = d.DoorPos;
                return true;
            }
            wall = default;
            doorPos = default;
            return false;
        }
    }
}
