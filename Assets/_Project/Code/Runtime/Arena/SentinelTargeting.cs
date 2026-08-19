using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Pure proximity-based target-selection rule for MV-362: "Aggro is proximity-based, not
    /// absolute. Robots attack a sentinel when it blocks them or is the nearest target — they must
    /// NOT always prefer sentinels over Max, or the player just builds a distraction and walks past
    /// every fight." Kept static/pure (no live scene) so the rule itself is unit-testable in
    /// isolation from <see cref="MaxWorlds.Enemies.RobotEnemy"/>.
    /// </summary>
    public static class SentinelTargeting
    {
        /// <summary>How far a robot will divert toward a Sentinel it hasn't reached yet — beyond this
        /// a distant, off-fight sentinel never steals a robot away from a chase happening somewhere
        /// else in the arena. Matches <see cref="MaxWorlds.Enemies.EnemyArchetype.Launcher"/>'s own max
        /// fire range (10 m) — the widest "how far away a robot already treats as a live threat"
        /// number already in the game, rather than inventing a new one.</summary>
        public const float AggroRadius = 10f;

        /// <summary>True if a robot standing this close to Max and to the nearest Sentinel should
        /// engage the Sentinel instead — strictly closer AND within <paramref name="aggroRadius"/>,
        /// so it is never an absolute preference (a robot two rooms away from its target sentinel
        /// still goes straight for Max).</summary>
        public static bool ShouldEngageSentinel(float distanceToPlayer, float distanceToSentinel, float aggroRadius)
        {
            if (distanceToSentinel > aggroRadius) return false;
            return distanceToSentinel < distanceToPlayer;
        }

        /// <summary>The nearest living deployed Sentinel to <paramref name="from"/>, or null if none
        /// are deployed (or all are dead).</summary>
        public static Sentinel Nearest(Vector3 from)
        {
            Sentinel best = null;
            float bestSq = float.MaxValue;
            var active = Sentinel.Active;
            for (int i = 0; i < active.Count; i++)
            {
                Sentinel s = active[i];
                if (s == null || !s.IsAlive) continue;
                float d = (s.transform.position - from).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = s; }
            }
            return best;
        }
    }
}
