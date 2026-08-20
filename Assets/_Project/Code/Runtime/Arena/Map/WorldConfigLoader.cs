using System;
using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Parses and validates a world config's 8 dials + enemyTypes THV table (Confluence MVW 34439170
    /// §4/§7-8, MV-269) — the schema-and-loader half of this ticket, kept separate from
    /// <see cref="WorldMapLoader"/> (MV-267, geometry only) exactly as that file's own doc comment
    /// anticipated: "the dial set (MV-269) and per-type threat values (MV-268) live in the same JSON
    /// file but are read by later tickets." This is that later ticket.
    /// </summary>
    public static class WorldConfigLoader
    {
        /// <summary>Parse a world-config JSON string into a validated <see cref="WorldConfig"/> — both
        /// the map-geometry rules (<see cref="MapValidation.ValidateWorldConfig"/>, MV-267) and the
        /// dial/enemyTypes rules below. A bad number fails here with a plain-language reason, not in a
        /// playtest.</summary>
        public static bool TryLoad(string json, out WorldConfig cfg, out string reason)
        {
            cfg = null;

            if (string.IsNullOrWhiteSpace(json)) { reason = "the world config JSON is empty"; return false; }

            try
            {
                cfg = JsonUtility.FromJson<WorldConfig>(json);
            }
            catch (Exception e)
            {
                reason = $"world config JSON is malformed: {e.Message}";
                return false;
            }

            if (cfg == null) { reason = "world config JSON did not parse"; return false; }

            cfg.areas ??= Array.Empty<WorldArea>();
            cfg.gates ??= Array.Empty<WorldGate>();

            if (!Validate(cfg, out reason)) return false;

            // MV-487: a config cannot be accepted unverified against the checked-in level-design
            // constraints, even though (unlike Validate above) a violation here is logged, not rejected —
            // see LevelDesignVerifier's own doc comment for why this is a lint, not a second gate.
            LevelDesignVerifier.LogViolations(cfg);

            reason = null;
            return true;
        }

        public static bool Validate(WorldConfig cfg, out string reason)
        {
            if (cfg == null) { reason = "the world config is null"; return false; }

            return MapValidation.ValidateWorldConfig(cfg, out reason)
                && ValidateDials(cfg, out reason)
                && ValidateEnemyTypes(cfg, out reason);
        }

        private static bool ValidateDials(WorldConfig cfg, out string reason)
        {
            WorldDials d = cfg.dials;
            if (d == null) { reason = "the world config has no dials"; return false; }

            if (d.areaCount <= 0)
            { reason = $"dials.areaCount must be positive, got {d.areaCount}"; return false; }

            if (d.baseThreat <= 0f)
            { reason = $"dials.baseThreat must be positive, got {d.baseThreat}"; return false; }

            if (d.threatGrowth < 0f)
            { reason = $"dials.threatGrowth must not be negative, got {d.threatGrowth}"; return false; }

            if (d.band == null)
            { reason = "dials.band is missing"; return false; }

            if (d.pacingRhythm == null || d.pacingRhythm.Length == 0)
            { reason = "dials.pacingRhythm is empty — a world needs at least one authored multiplier"; return false; }

            if (d.toughnessCurve == null)
            { reason = "dials.toughnessCurve is missing"; return false; }

            if (d.powerupCadence <= 0)
            { reason = $"dials.powerupCadence must be positive, got {d.powerupCadence}"; return false; }

            reason = null;
            return true;
        }

        private static bool ValidateEnemyTypes(WorldConfig cfg, out string reason)
        {
            WorldEnemyTypes t = cfg.enemyTypes;
            if (t == null || t.small == null || t.large == null || t.heavy == null || t.brute == null)
            {
                reason = "enemyTypes must define all four archetypes: small, large, heavy and brute";
                return false;
            }

            if (t.small.thv <= 0f || t.large.thv <= 0f || t.heavy.thv <= 0f || t.brute.thv <= 0f)
            {
                reason = "every enemyTypes entry must have a positive thv";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
