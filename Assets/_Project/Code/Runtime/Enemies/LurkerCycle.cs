using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Pure timing/FSM for the Grate Lurker's submerge cycle (MV-688) — SUBMERGED (invulnerable,
    /// invisible) → RATTLE (the tell) → EMERGED (a normal, damageable target for a short combat
    /// window) → SUBMERGING (sinking back down) → SUBMERGED again, at another authored grate. No
    /// MonoBehaviour, no live Unity time — <see cref="RobotEnemy"/> owns sight/hit bookkeeping and the
    /// actual position warp on reappear; this only owns the clock, so it is unit-testable directly
    /// (same idiom as <see cref="BlinkerTeleport"/>'s pure maths).
    /// </summary>
    public static class LurkerCycle
    {
        public enum Phase { Submerged, Rattle, Emerged, Submerging }

        /// <summary>How close Max must be, while this Lurker is awake (the universal "dormant until
        /// seen" rule, latched once by the caller), before SUBMERGED begins a RATTLE.</summary>
        public const float WakeRadius = 5f;

        public const float RattleDuration = 0.8f;

        /// <summary>The "up to ... 3 s" combat cap once EMERGED — cut short instead by
        /// <see cref="MaxHitsPerEmergence"/> landed hits, whichever comes first.</summary>
        public const float EmergedDuration = 3f;

        public const int MaxHitsPerEmergence = 2;

        public const float SubmergeDuration = 0.4f;

        /// <summary>How long SUBMERGED holds before it may RATTLE again after reappearing.</summary>
        public const float CooldownDuration = 3f;

        /// <summary>How far a reappear may travel from the grate this Lurker is leaving.</summary>
        public const float ReappearRadius = 6f;

        /// <summary>Advances the cycle by <paramref name="dt"/>, carrying any leftover time into the
        /// next phase rather than discarding it — a single call may cross more than one phase boundary
        /// (a caller "advancing 3.1 s" in one step lands exactly where 31 real 0.1 s frames would).
        /// <paramref name="elapsed"/> is time already spent in <paramref name="phase"/>; it is reset
        /// (net of any carry-over) on every transition. <paramref name="awake"/>/<paramref name="distanceToTarget"/>/
        /// <paramref name="hitsThisEmergence"/> are read fresh each call — the caller owns what they mean.</summary>
        /// <summary>Float accumulation across several chained subtractions in one <see cref="Step"/>
        /// call (RATTLE's leftover feeding EMERGED, EMERGED's feeding SUBMERGING, ...) can land a hair
        /// under an exact duration threshold — e.g. 0.4 s of carried-over SUBMERGING time reads back as
        /// 0.399999976. Every duration check below tolerates this, the same "don't let float noise cost
        /// a whole extra frame" idiom <c>Geo.Epsilon</c> uses elsewhere in this codebase.</summary>
        private const float Epsilon = 1e-4f;

        public static Phase Step(Phase phase, ref float elapsed, float dt,
            bool awake, float distanceToTarget, int hitsThisEmergence)
        {
            elapsed += dt;

            while (true)
            {
                switch (phase)
                {
                    case Phase.Submerged:
                        if (elapsed >= CooldownDuration - Epsilon && awake && distanceToTarget <= WakeRadius)
                        {
                            elapsed -= CooldownDuration;
                            phase = Phase.Rattle;
                            continue;
                        }
                        return phase;

                    case Phase.Rattle:
                        if (elapsed >= RattleDuration - Epsilon)
                        {
                            elapsed -= RattleDuration;
                            phase = Phase.Emerged;
                            continue;
                        }
                        return phase;

                    case Phase.Emerged:
                        bool timeUp = elapsed >= EmergedDuration - Epsilon;
                        if (timeUp || hitsThisEmergence >= MaxHitsPerEmergence)
                        {
                            elapsed = timeUp ? elapsed - EmergedDuration : 0f;
                            phase = Phase.Submerging;
                            continue;
                        }
                        return phase;

                    case Phase.Submerging:
                        if (elapsed >= SubmergeDuration - Epsilon)
                        {
                            elapsed -= SubmergeDuration;
                            phase = Phase.Submerged;
                            continue;
                        }
                        return phase;

                    default:
                        return phase;
                }
            }
        }

        /// <summary>Only EMERGED is a normal, damageable target — "killing it while emerged is the
        /// only way to kill it".</summary>
        public static bool IsDamageable(Phase phase) => phase == Phase.Emerged;

        /// <summary>Where a Lurker leaving <paramref name="home"/> reappears (MV-688): the nearest
        /// OTHER grate in <paramref name="areaGrates"/> within <paramref name="maxDistance"/>, or
        /// <paramref name="home"/> itself if none qualifies.</summary>
        public static Vector3 PickReappearGrate(Vector3 home, System.Collections.Generic.IReadOnlyList<Vector3> areaGrates, float maxDistance)
        {
            Vector3 best = home;
            float bestDist = float.MaxValue;
            if (areaGrates == null) return best;

            for (int i = 0; i < areaGrates.Count; i++)
            {
                Vector3 g = areaGrates[i];
                if (g == home) continue;
                float d = Vector3.Distance(home, g);
                if (d <= maxDistance && d < bestDist) { bestDist = d; best = g; }
            }
            return best;
        }
    }
}
