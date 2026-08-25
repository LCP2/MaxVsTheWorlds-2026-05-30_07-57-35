using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Arena
{
    /// <summary>One of the "at least" four numbers/principles MV-487 checks in — previously these lived
    /// only in a chat transcript and a throwaway verifier script, re-derived differently every session
    /// (Confluence DM space, Methodology Review — August 2026 §2). Read from
    /// <c>Resources/Worlds/level_design_constraints.json</c> by <see cref="LevelDesignVerifier"/>, never
    /// duplicated as a C# constant.</summary>
    [Serializable]
    public sealed class GateAxisAlternationConstraint
    {
        public string principle;
    }

    /// <summary>Prose only — carried so the file, not a code comment, is where the next person reads
    /// about a known gap in <see cref="MapValidation"/> that this ticket does not fix.</summary>
    [Serializable]
    public sealed class KnownEngineDefects
    {
        public string mapValidationCoverReturnsOnFirstViolation;
        public string mapValidationOnlyValidatesCoverEntities;
    }

    [Serializable]
    public sealed class LevelDesignConstraints
    {
        public float minRobotToCoverGapMetres;
        public float minShedSeparationMetres;
        public float minRoomDimensionMetres;
        public GateAxisAlternationConstraint gateAxisAlternation;
        public KnownEngineDefects knownEngineDefects;
    }

    /// <summary>
    /// Reads <see cref="LevelDesignConstraints"/> from disk and checks a <see cref="WorldConfig"/> against
    /// every one of them, collecting every violation instead of stopping at the first the way
    /// <see cref="MapValidation.Cover"/> does (MV-487; that method's own first-violation-only behaviour is
    /// a known, noted, NOT-fixed-here defect — see <see cref="KnownEngineDefects"/>). This is a lint, not
    /// a structural gate: unlike <see cref="MapValidation.ValidateWorldConfig"/>, a violation here does
    /// not fail <see cref="WorldConfigLoader.TryLoad"/> — an authored world (world1_config.json's older
    /// areas in particular) can carry a known, logged violation without the game refusing to load it.
    /// </summary>
    public static class LevelDesignVerifier
    {
        public const string ConstraintsResourcePath = "Worlds/level_design_constraints";

        public static bool TryLoadConstraints(out LevelDesignConstraints constraints, out string reason)
        {
            TextAsset asset = Resources.Load<TextAsset>(ConstraintsResourcePath);
            if (asset == null)
            {
                constraints = null;
                reason = $"level-design constraints resource '{ConstraintsResourcePath}' was not found";
                return false;
            }

            return TryParseConstraints(asset.text, out constraints, out reason);
        }

        public static bool TryParseConstraints(string json, out LevelDesignConstraints constraints, out string reason)
        {
            constraints = string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<LevelDesignConstraints>(json);
            if (constraints == null)
            {
                reason = "level-design constraints JSON is empty or failed to parse";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>Every violation of <paramref name="constraints"/> found in <paramref name="cfg"/>,
        /// checked to completion rather than returning on the first (MV-487 AC2).</summary>
        public static List<string> Violations(WorldConfig cfg, LevelDesignConstraints constraints)
        {
            var violations = new List<string>();
            if (cfg?.areas == null || constraints == null) return violations;

            VerifyMinRoomDimension(cfg, constraints, violations);
            VerifyShedSeparation(cfg, constraints, violations);
            VerifyRobotToCoverGap(cfg, constraints, violations);
            VerifyGateAxisAlternation(cfg, constraints, violations);

            return violations;
        }

        /// <summary>Loads the checked-in constraints and logs every violation found in
        /// <paramref name="cfg"/> as a warning — called from every successful
        /// <see cref="WorldConfigLoader.TryLoad"/> so a config is never accepted without at least being
        /// checked (MV-487: "wire the verifier into the area-generation path so a config cannot be
        /// accepted unverified"). Does not block the load: this is a lint on top of
        /// <see cref="MapValidation"/>'s structural gate, not a second copy of it.</summary>
        public static void LogViolations(WorldConfig cfg)
        {
            if (!TryLoadConstraints(out LevelDesignConstraints constraints, out string reason))
            {
                Debug.LogWarning($"[LevelDesignVerifier] constraints not checked: {reason}");
                return;
            }

            List<string> violations = Violations(cfg, constraints);
            foreach (string v in violations)
                Debug.LogWarning($"[LevelDesignVerifier] {v}");
        }

        private static void VerifyMinRoomDimension(WorldConfig cfg, LevelDesignConstraints constraints, List<string> violations)
        {
            foreach (WorldArea a in cfg.areas)
            {
                if (a == null || a.size == null || a.IsEntryRole) continue;

                if (a.size.w < constraints.minRoomDimensionMetres || a.size.d < constraints.minRoomDimensionMetres)
                {
                    violations.Add(
                        $"area '{a.id}' is {a.size.w:0.#}×{a.size.d:0.#} m — under the " +
                        $"{constraints.minRoomDimensionMetres:0.#} m minimum room dimension");
                }
            }
        }

        /// <summary>Every authored shed, not just the first in an area (MV-541: an area can carry
        /// several via <see cref="WorldArea.Sheds"/>) — flattened to (area id, shed) pairs so this
        /// lint sees every shed the same way <see cref="MapValidation.WorldSheds"/> already does.</summary>
        private static void VerifyShedSeparation(WorldConfig cfg, LevelDesignConstraints constraints, List<string> violations)
        {
            var sheds = new List<(string areaId, WorldShed shed)>();
            foreach (WorldArea a in cfg.areas)
            {
                if (a == null || !a.hasShed) continue;
                foreach (WorldShed s in a.Sheds())
                    if (s != null) sheds.Add((a.id, s));
            }

            for (int i = 0; i < sheds.Count; i++)
            for (int j = i + 1; j < sheds.Count; j++)
            {
                WorldShed s1 = sheds[i].shed;
                WorldShed s2 = sheds[j].shed;
                float dist = Vector2.Distance(new Vector2(s1.x, s1.z), new Vector2(s2.x, s2.z));

                if (dist < constraints.minShedSeparationMetres)
                {
                    violations.Add(
                        $"sheds in area '{sheds[i].areaId}' and area '{sheds[j].areaId}' are {dist:0.#} m apart — " +
                        $"under the {constraints.minShedSeparationMetres:0.#} m minimum shed separation");
                }
            }
        }

        private static void VerifyRobotToCoverGap(WorldConfig cfg, LevelDesignConstraints constraints, List<string> violations)
        {
            foreach (WorldArea a in cfg.areas)
            {
                if (a == null || a.cover == null || a.cover.Length == 0) continue;

                int count = Garrison.SeedCount(a.index, cfg);
                if (count <= 0) continue;

                Vector3[] seeds = Garrison.SeedPositions(a, count);
                foreach (Vector3 seed in seeds)
                {
                    var point = new Vector2(seed.x, seed.z);

                    foreach (WorldCover c in a.cover)
                    {
                        if (c == null) continue;

                        ArenaCover body = new MapEntity
                        {
                            x = c.x, z = c.z, width = c.width, height = c.height, depth = c.depth, shape = c.shape,
                        }.ToCover();

                        float gap = body.DistanceTo(point);
                        if (gap < constraints.minRobotToCoverGapMetres)
                        {
                            violations.Add(
                                $"area '{a.id}': a garrison seed point sits {gap:0.00} m from cover '{c.id}' — " +
                                $"under the {constraints.minRobotToCoverGapMetres:0.#} m minimum robot-to-cover gap");
                        }
                    }
                }
            }
        }

        private enum GateAxis { Vertical, Horizontal }

        private static void VerifyGateAxisAlternation(WorldConfig cfg, LevelDesignConstraints constraints, List<string> violations)
        {
            if (constraints.gateAxisAlternation == null || cfg.gates == null || cfg.gates.Length < 2) return;

            GateAxis? previousAxis = null;
            string previousId = null;

            foreach (WorldGate g in cfg.gates)
            {
                if (g?.from == null || !WallEnums.TryParse(g.from.wall, out Wall wall))
                {
                    previousAxis = null;
                    previousId = null;
                    continue;
                }

                GateAxis axis = (wall == Wall.N || wall == Wall.S) ? GateAxis.Vertical : GateAxis.Horizontal;

                if (previousAxis.HasValue && previousAxis.Value == axis)
                {
                    string axisName = axis == GateAxis.Vertical ? "N/S" : "E/W";
                    violations.Add(
                        $"gate '{g.id}' repeats the {axisName} axis of the previous gate '{previousId}' — the " +
                        "east/west gate placement principle calls for alternating vertical climbs and lateral jogs");
                }

                previousAxis = axis;
                previousId = g.id;
            }
        }
    }
}
