using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>What a zone is FOR. Drives nothing structural — a room is a room — but it is how the
    /// author says "this is the fight room" vs "this is the boss arena", and it is what the dressing
    /// and validation layers key off (a boss arena must stay clear of cover; an entry must not hold a
    /// factory).</summary>
    public enum ZoneKind { Entry, Open, Cover, Interior, Hazard, Dense, Boss }

    /// <summary>Everything that can stand in a map. <see cref="EntityKind.Unknown"/> is what an
    /// unrecognised string parses to — the loader skips those rather than throwing, so a map authored
    /// against a newer build still loads the parts this build understands.</summary>
    public enum EntityKind { Unknown, PlayerSpawn, Factory, Gate, Boss, Cover, Prop, Pickup }

    /// <summary>One room. An axis-aligned rectangle on the XZ plane, authored by its centre and size
    /// in metres — the same way the design board draws it. Walls are NOT authored: they are derived
    /// from the zone's edges (<see cref="MapGeometry"/>), so moving or resizing a room re-walls it
    /// automatically. That is the whole point of the engine.</summary>
    [Serializable]
    public sealed class MapZone
    {
        public string id;
        public string name;
        public string type = "open";
        public float x;       // centre X
        public float z;       // centre Z
        public float width;   // size along X
        public float depth;   // size along Z

        public ZoneKind Kind => MapEnums.Zone(type);

        public float XMin => x - width * 0.5f;
        public float XMax => x + width * 0.5f;
        public float ZMin => z - depth * 0.5f;
        public float ZMax => z + depth * 0.5f;

        public Vector3 Center => new Vector3(x, 0f, z);
        public Rect Footprint => new Rect(XMin, ZMin, width, depth);

        public bool Contains(float px, float pz) =>
            px >= XMin && px <= XMax && pz >= ZMin && pz <= ZMax;

        /// <summary>Largest circle that fits in the room — the circle-strafe loop the player gets to
        /// use. A fight room with a small number here is a corridor wearing a room's name.</summary>
        public float InscribedRadius => Mathf.Min(width, depth) * 0.5f;
    }

    /// <summary>A way through between two rooms. The engine cuts the doorway out of the shared wall:
    /// <c>doorway = 0</c> opens the whole shared edge (how a patio opens out into a lawn),
    /// a positive <c>doorway</c> leaves a hole that wide (how a lawn necks down to a gate).
    /// <c>gate</c> names the gate entity that fills the hole — and the doorway is centred on THAT
    /// entity, so dragging the gate in the editor slides the doorway with it.</summary>
    [Serializable]
    public sealed class MapLink
    {
        public string from;
        public string to;
        public float doorway;   // metres; 0 = the whole shared edge is open
        public string gate;     // id of the gate entity filling this doorway (optional)
    }

    /// <summary>Anything that stands in a room: where Max starts, the factory, the gate it opens, the
    /// boss, cover to fight around, props, pickups. One flat record for every kind — a map file you
    /// can read top-to-bottom and a format that does not need a class per noun.</summary>
    [Serializable]
    public sealed class MapEntity
    {
        public string id;
        public string kind = "prop";
        public float x;
        public float z;

        // Size. Used by cover/prop (the body) and by gate (its thickness/height; the WIDTH of a gate
        // is not authored here — it is the doorway of the link it fills, so the two can never disagree).
        public float width = 1f;
        public float height = 1f;
        public float depth = 1f;

        public string shape = "box";      // box | cylinder
        public string dressing = "none";  // none | tree | hedge | planter  (art pass, YT-75)

        /// <summary>Gate only — the unlock condition: the factory whose destruction opens this gate,
        /// or a comma-separated list of factories ALL of which must fall first (YT-92). Empty means
        /// the gate never opens, which validation rejects: a locked door with no key is a bug every
        /// time.
        ///
        /// A list rather than a per-gate flag because that is the sentence the level is trying to say
        /// — "this way opens when the hutch AND the greenhouse are gone" — and because it degrades to
        /// the single id the one-factory maps already carry.</summary>
        public string opensOn;

        /// <summary>The factories named in <see cref="opensOn"/>. Empty for anything that is not a
        /// keyed gate.</summary>
        public string[] Keys => MapEnums.Ids(opensOn);

        public EntityKind Kind => MapEnums.Entity(kind);
        public CoverShape Shape => MapEnums.Shape(shape);
        public CoverDressing Dressing => MapEnums.Dressing(dressing);

        public Vector3 Size => new Vector3(width, height, depth);
        public Vector2 CenterXz => new Vector2(x, z);

        /// <summary>World centre with Y derived so the body rests on the floor — a prop can never be
        /// authored half-buried or floating, because its Y is not authored at all.</summary>
        public Vector3 GroundedCenter => new Vector3(x, height * 0.5f, z);

        /// <summary>The same record expressed as the cover struct the rest of the game already
        /// speaks (<see cref="BackyardCover"/>, the dressing pass, the sight-line tests).</summary>
        public ArenaCover ToCover() =>
            new ArenaCover(string.IsNullOrEmpty(id) ? "Cover" : id, CenterXz, Size, Shape, Dressing);
    }

    /// <summary>
    /// A map: rooms, the ways between them, and the things standing in them. This — not a scene file,
    /// not a hand-placed hierarchy — is what a level IS (YT-89). <see cref="MapGeometry"/> turns it
    /// into walls and floor, <see cref="MapRuntime"/> turns it into GameObjects, and
    /// <see cref="MapValidation"/> refuses to build one that would not play.
    ///
    /// Serialized as JSON (<see cref="MapLibrary"/>) rather than a ScriptableObject asset on purpose:
    /// a text file is diffable in a PR, editable without the Unity editor open, and — critically —
    /// cannot be silently overridden by a stale copy serialized into the scene, which is exactly the
    /// trap the hand-built layout fell into.
    /// </summary>
    [Serializable]
    public sealed class MapData
    {
        public string name = "Untitled";

        public float wallHeight = 3.5f;
        public float wallThickness = 1f;

        public MapZone[] zones = Array.Empty<MapZone>();
        public MapLink[] links = Array.Empty<MapLink>();
        public MapEntity[] entities = Array.Empty<MapEntity>();

        public MapZone Zone(string zoneId)
        {
            if (zones == null || string.IsNullOrEmpty(zoneId)) return null;
            foreach (MapZone z in zones)
                if (z != null && z.id == zoneId) return z;
            return null;
        }

        public MapEntity Entity(string entityId)
        {
            if (entities == null || string.IsNullOrEmpty(entityId)) return null;
            foreach (MapEntity e in entities)
                if (e != null && e.id == entityId) return e;
            return null;
        }

        /// <summary>The first entity of a kind, or null. Most kinds are one-of in a slice map.</summary>
        public MapEntity First(EntityKind kind)
        {
            if (entities == null) return null;
            foreach (MapEntity e in entities)
                if (e != null && e.Kind == kind) return e;
            return null;
        }

        /// <summary>The zone a point falls in, or null if it is outside every room. Used to check that
        /// nothing is authored into the void.</summary>
        public MapZone ZoneAt(float px, float pz)
        {
            if (zones == null) return null;
            foreach (MapZone z in zones)
                if (z != null && z.Contains(px, pz)) return z;
            return null;
        }

        /// <summary>Bounding box of every room, in XZ. The floor is cut to this.</summary>
        public Rect Bounds()
        {
            if (zones == null || zones.Length == 0) return new Rect(0f, 0f, 0f, 0f);
            float xMin = float.MaxValue, xMax = float.MinValue;
            float zMin = float.MaxValue, zMax = float.MinValue;
            foreach (MapZone z in zones)
            {
                if (z == null) continue;
                xMin = Mathf.Min(xMin, z.XMin); xMax = Mathf.Max(xMax, z.XMax);
                zMin = Mathf.Min(zMin, z.ZMin); zMax = Mathf.Max(zMax, z.ZMax);
            }
            return new Rect(xMin, zMin, xMax - xMin, zMax - zMin);
        }
    }

    /// <summary>String ↔ enum for the map format. The format stores words, not numbers: JsonUtility
    /// writes enums as integers, which would make the map file unreadable to a human and would
    /// silently re-map every value the day someone inserts a case in the middle of an enum.</summary>
    public static class MapEnums
    {
        public static ZoneKind Zone(string s) =>
            Parse(s, ZoneKind.Open);

        public static EntityKind Entity(string s) =>
            Parse(s, EntityKind.Unknown);

        public static CoverShape Shape(string s) =>
            Parse(s, CoverShape.Box);

        public static CoverDressing Dressing(string s) =>
            Parse(s, CoverDressing.None);

        /// <summary>A comma-separated list of entity ids, as written by hand: <c>"a, b"</c> and
        /// <c>"a,b"</c> and <c>"a"</c> all say what they look like they say. Ids themselves are taken
        /// verbatim — they are names, not words, and nothing here may quietly rewrite one.</summary>
        public static string[] Ids(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Array.Empty<string>();

            string[] parts = s.Split(',');
            var ids = new List<string>(parts.Length);
            foreach (string part in parts)
            {
                string id = part.Trim();
                if (id.Length > 0) ids.Add(id);
            }
            return ids.ToArray();
        }

        /// <summary>Case- and separator-insensitive: <c>playerSpawn</c>, <c>PlayerSpawn</c> and
        /// <c>player_spawn</c> all mean the same thing, because a map file is written by hand.</summary>
        private static T Parse<T>(string s, T fallback) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            string cleaned = s.Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);
            return Enum.TryParse(cleaned, ignoreCase: true, out T parsed) ? parsed : fallback;
        }
    }
}
