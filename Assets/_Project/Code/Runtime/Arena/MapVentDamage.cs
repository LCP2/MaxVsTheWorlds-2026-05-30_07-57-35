using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// MV-1240: the plasma vent's own authored rule — a static, map-placed floor hazard (World 5's
    /// Vent Halls and the Reactor Ring) that fires on a fixed timer and hurts EVERYTHING standing in it
    /// when it fires: Max, sentinels and robots alike. Deliberately unlike <see cref="MapSludgeDamage"/>
    /// (MV-795/MV-838's own lesson: a per-tick DoT must never touch a robot) — a vent is not continuous,
    /// world-wide pressure, it is a localised, telegraphed, once-per-cycle hit that the ticket's own
    /// "do not re-raise" explicitly wants robots exposed to ("luring is a tactic").
    /// </summary>
    public static class MapVentDamage
    {
        /// <summary>Lee's own authored cycle length (ticket table) — glow, then column, then idle,
        /// repeating.</summary>
        public const float CycleInterval = 4.0f;

        /// <summary>The orange glow tell, before the column fires.</summary>
        public const float GlowDuration = 1.0f;

        /// <summary>The flame column itself — the only part of the cycle that can deal damage.</summary>
        public const float ColumnDuration = 0.6f;

        /// <summary>The column's own visual height, metres (ticket table) — read by the visual, not by
        /// the damage logic.</summary>
        public const float ColumnHeight = 2.6f;

        /// <summary>Once per cycle, to anything standing in the vent during the column (ticket table).</summary>
        public const float DamagePerCycle = 20f;

        /// <summary>True only at floor level, inside an authored vent rect (MV-1240) — same floor-vs-deck
        /// Y gate <see cref="MapGeometry.SpeedMultiplierAt"/> already uses for map sludge, since a vent is
        /// exactly as floor-only as one ("flat, walkable", the ticket's own words — never built on a
        /// deck).</summary>
        public static bool IsInVent(MapData map, Vector3 worldPosition)
        {
            if (map?.entities == null) return false;
            if (worldPosition.y >= map.deckHeight - 0.5f) return false;

            foreach (MapEntity e in map.VentEntities())
            {
                var footprint = new Rect(e.x - e.width * 0.5f, e.z - e.depth * 0.5f, e.width, e.depth);
                if (footprint.Contains(new Vector2(worldPosition.x, worldPosition.z))) return true;
            }
            return false;
        }

        /// <summary>Deals one cycle's worth of damage to <paramref name="receiver"/> if it is standing in
        /// a vent — the single call every receiver kind (<see cref="MapVentDamageRunner"/>'s player, every
        /// <see cref="MaxWorlds.Enemies.RobotEnemy"/>, every <see cref="Sentinel"/>) goes through, so the
        /// amount and the <see cref="DamageSource"/>/<see cref="Team"/> tagging can never drift between
        /// them. <see cref="Team.Neutral"/> always applies (<see cref="DamageRules.Applies"/>) — the
        /// hazard hurts its own side exactly as readily as Max's.</summary>
        public static void FireAt(MapData map, IDamageable receiver, Vector3 position)
        {
            if (receiver == null || !receiver.IsAlive) return;
            if (!IsInVent(map, position)) return;
            receiver.TakeDamage(new DamageInfo(DamagePerCycle, position, Vector3.up, Team.Neutral, source: DamageSource.Environment));
        }
    }
}
