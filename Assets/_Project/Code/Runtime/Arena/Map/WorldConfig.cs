using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>Which wall of an area, in the world-config schema (MV-267, Confluence MVW 34439170
    /// §7). A rectangle has exactly four; a gate names one on each of the two areas it joins.</summary>
    public enum Wall { N, E, S, W }

    public static class WallEnums
    {
        public static bool TryParse(string s, out Wall wall) =>
            Enum.TryParse(s, ignoreCase: true, out wall);

        /// <summary>The wall a gate on the far side of this one connects to on a rectangular area —
        /// N faces S, E faces W. A gate that does not join opposite walls is not a doorway between two
        /// neighbouring rooms, it is two rooms poking through each other's corner.</summary>
        public static Wall Opposite(Wall wall) => wall switch
        {
            Wall.N => Wall.S,
            Wall.S => Wall.N,
            Wall.E => Wall.W,
            Wall.W => Wall.E,
            _ => wall,
        };
    }

    [Serializable]
    public sealed class WorldAreaOrigin
    {
        public float x;
        public float z;
    }

    [Serializable]
    public sealed class WorldAreaSize
    {
        public float w;
        public float d;
    }

    /// <summary>One area of a world map: a 2D rectangle at an arbitrary origin — NOT constrained to a
    /// shared centre-line the way the old corridor engine's rooms were (MV-267). <see cref="origin"/>
    /// is the rectangle's MIN corner (matches how the design board's <c>world1_config.json</c> is
    /// authored), unlike <see cref="MapZone"/> which is authored by its centre.</summary>
    [Serializable]
    public sealed class WorldArea
    {
        public string id;
        public int index;
        public string name;

        /// <summary>What the area is for — free text, but "entry", "boss" and "exit" are read as
        /// substrings so "boss+exit" (the compost clearing, which is both) says both at once without
        /// a combinable-flags field.</summary>
        public string role = "normal";

        public WorldAreaOrigin origin;
        public WorldAreaSize size;

        // Origination fields (garrison/sheds/threat budget) belong to MV-268/MV-269 — parsed here only
        // so a world config that carries them round-trips without loss; this ticket's engine does not
        // read them.
        public bool hasShed;
        public string garrisonDensity;

        public float XMin => origin?.x ?? 0f;
        public float XMax => XMin + (size?.w ?? 0f);
        public float ZMin => origin?.z ?? 0f;
        public float ZMax => ZMin + (size?.d ?? 0f);

        public Vector2 CenterXz => new Vector2((XMin + XMax) * 0.5f, (ZMin + ZMax) * 0.5f);
        public Rect Footprint => new Rect(XMin, ZMin, XMax - XMin, ZMax - ZMin);

        public bool IsEntryRole => HasRole("entry");
        public bool IsBossRole => HasRole("boss");
        public bool IsExitRole => HasRole("exit");

        private bool HasRole(string token) =>
            role != null && role.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The fixed coordinate a wall sits on — every point on the N/S wall shares a Z, every
        /// point on the E/W wall shares an X.</summary>
        public float WallCoord(Wall wall) => wall switch
        {
            Wall.N => ZMax,
            Wall.S => ZMin,
            Wall.E => XMax,
            Wall.W => XMin,
            _ => 0f,
        };

        /// <summary>The span a wall runs along — X for the N/S walls, Z for the E/W walls. A gate's
        /// <c>pos</c> (0..1) is a fraction of this.</summary>
        public Span WallSpan(Wall wall) =>
            (wall == Wall.N || wall == Wall.S) ? new Span(XMin, XMax) : new Span(ZMin, ZMax);

        /// <summary>True if this wall's line is a constant Z (so the span it runs along is X).</summary>
        public bool WallRunsAlongX(Wall wall) => wall == Wall.N || wall == Wall.S;
    }

    /// <summary>One end of a gate: which area, which of its four walls, and how far along that wall
    /// (0 = the wall's first corner, 1 = its last).</summary>
    [Serializable]
    public sealed class WorldGateEndpoint
    {
        public string area;
        public string wall;
        public float pos;
    }

    /// <summary>A doorway between two areas' walls, placed at a fraction along each rather than forced
    /// onto a shared centre-line (MV-267). <see cref="opensWith"/> is data the difficulty/origination
    /// engines (MV-268/MV-269) act on — this ticket only carries and validates the word, it does not
    /// wire any gameplay behind it (gates still open by their own HP, <see cref="AreaGate"/>).</summary>
    [Serializable]
    public sealed class WorldGate
    {
        public string id;
        public WorldGateEndpoint from;
        public WorldGateEndpoint to;
        public float width;
        public string opensWith = "start";
    }

    /// <summary>A whole world's map, in the 2D-area-placement schema (MV-267, Confluence MVW 34439170
    /// §7-8). Deliberately narrow: only the fields the map engine itself reads (areas' geometry, gates'
    /// wall placement) are declared. The dial set (MV-269) and per-type threat values (MV-268) live in
    /// the same JSON file but are read by later tickets — <c>JsonUtility</c> ignores JSON keys with no
    /// matching field, so a full <c>world1_config.json</c> parses cleanly through this today and gains
    /// no fields it does not need yet.</summary>
    [Serializable]
    public sealed class WorldConfig
    {
        public string world = "Untitled World";

        public WorldArea[] areas = Array.Empty<WorldArea>();
        public WorldGate[] gates = Array.Empty<WorldGate>();

        public WorldArea Area(string areaId)
        {
            if (areas == null || string.IsNullOrEmpty(areaId)) return null;
            foreach (WorldArea a in areas)
                if (a != null && a.id == areaId) return a;
            return null;
        }
    }
}
