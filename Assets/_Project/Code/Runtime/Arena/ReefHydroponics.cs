using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// World 3's hydroponic bed placement pass (MV-1055, change item 1) — decides WHERE a bed goes;
    /// <see cref="ReefKit.BuildHydroponicBed"/> (Rendering assembly, no reference to this one's
    /// MapData/WorldConfig types) builds what stands there, same split <see cref="ReefDressing"/>
    /// already keeps for the coolant turret and crate skin.
    ///
    /// A deterministic grid scan, not a random scatter — the ticket's own "auto-placed", and the same
    /// map must place the same beds on every load (CI, a replay, a second run), not a different
    /// layout each time. Up to <see cref="MaxBedsPerZone"/> per room, each at least
    /// <see cref="MinClearance"/> clear of every cover piece, gate mouth, garrison point, factory and
    /// replicator (ticket, "Change" §1) — and of every bed already placed in the same room, so two
    /// beds never land on top of each other.
    /// </summary>
    public static class ReefHydroponics
    {
        /// <summary>Ticket AC: "its centre is >= 2 m (edge-to-edge) from every cover piece, gate
        /// mouth and garrison point" — the bed's own centre to the obstacle's own nearest edge
        /// (a point obstacle's "edge" is just itself).</summary>
        public const float MinClearance = 2f;

        private const int MaxBedsPerZone = 3;
        private const float GridStep = 1f;

        /// <summary>Keeps a bed's own ~1.5 m radius fully inside its room, clear of the wall/fence
        /// line the room's own edges become.</summary>
        private const float ZoneEdgeMargin = 2f;

        /// <summary>Places every bed this map has room for and returns how many it built.</summary>
        public static int PlaceBeds(Transform host, MapData map, WorldConfig cfg, IReadOnlyList<CoverPiece> cover)
        {
            if (host == null || map?.zones == null) return 0;

            List<Vector2> pointObstacles = PointObstacles(map, cfg);

            Transform root = null;
            int placed = 0;

            foreach (MapZone zone in map.zones)
            {
                if (zone == null || zone.level != 0) continue;

                int zonePlaced = 0;
                var zoneBeds = new List<Vector2>();

                for (float z = zone.ZMin + ZoneEdgeMargin; z <= zone.ZMax - ZoneEdgeMargin && zonePlaced < MaxBedsPerZone; z += GridStep)
                {
                    for (float x = zone.XMin + ZoneEdgeMargin; x <= zone.XMax - ZoneEdgeMargin && zonePlaced < MaxBedsPerZone; x += GridStep)
                    {
                        var candidate = new Vector2(x, z);
                        if (!ClearsEverything(candidate, cover, pointObstacles, zoneBeds)) continue;

                        if (root == null)
                        {
                            root = new GameObject("Hydroponic Beds").transform;
                            root.SetParent(host, false);
                        }

                        ReefKit.BuildHydroponicBed(root, new Vector3(candidate.x, 0f, candidate.y));
                        zoneBeds.Add(candidate);
                        zonePlaced++;
                        placed++;
                    }
                }
            }

            return placed;
        }

        private static bool ClearsEverything(
            Vector2 candidate, IReadOnlyList<CoverPiece> cover, List<Vector2> pointObstacles, List<Vector2> placedBeds)
        {
            foreach (CoverPiece piece in cover)
                if (piece.Cover.DistanceTo(candidate) < MinClearance) return false;

            foreach (Vector2 p in pointObstacles)
                if (Vector2.Distance(candidate, p) < MinClearance) return false;

            foreach (Vector2 b in placedBeds)
                if (Vector2.Distance(candidate, b) < MinClearance) return false;

            return true;
        }

        /// <summary>Gate mouths, factories and Replicators (<see cref="MapData.entities"/> — each
        /// already sits at the exact point the ticket means: a gate's doorway is centred on its own
        /// entity, and a factory/Replicator's entity position IS its body), plus every authored
        /// garrison point (<see cref="WorldArea.garrison"/> — never built as a <see cref="MapEntity"/>
        /// at all, since a garrison slot is a spawn position, not a built object).</summary>
        private static List<Vector2> PointObstacles(MapData map, WorldConfig cfg)
        {
            var list = new List<Vector2>();

            if (map.entities != null)
            {
                foreach (MapEntity e in map.entities)
                {
                    if (e == null) continue;
                    switch (e.Kind)
                    {
                        case EntityKind.AreaGate:
                        case EntityKind.Factory:
                        case EntityKind.Replicator:
                            list.Add(new Vector2(e.x, e.z));
                            break;
                    }
                }
            }

            if (cfg?.areas != null)
            {
                foreach (WorldArea area in cfg.areas)
                {
                    if (area?.garrison == null) continue;
                    foreach (WorldGarrisonEntry g in area.garrison)
                        if (g != null) list.Add(new Vector2(g.x, g.z));
                }
            }

            return list;
        }
    }
}
