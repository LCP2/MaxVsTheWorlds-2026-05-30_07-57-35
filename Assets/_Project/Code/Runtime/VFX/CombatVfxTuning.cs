using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>Pure maths behind the combat feedback VFX — unit-testable without a scene.</summary>
    public static class CombatVfxTuning
    {
        /// <summary>Sparks thrown off an enemy that just took a hit. Scales with the size of
        /// the hit and doubles up on a crit, but stays small: a sustained water stream lands a
        /// tick every 0.1s on every enemy it touches, so a big burst here would bury the screen.</summary>
        public static int HitSparkCount(float damage, bool crit)
        {
            if (damage <= 0f) return 0;
            int n = Mathf.RoundToInt(3f + damage * 0.45f);
            if (crit) n *= 2;
            return Mathf.Clamp(n, 3, 12);
        }

        /// <summary>How many trail puffs to lay down over a dash step of <paramref name="distance"/>.
        /// A dash moves far in one frame, so emitting a single puff per frame leaves a dotted
        /// line; this fills the gap in proportion to the ground actually covered.</summary>
        public static int TrailSteps(float distance)
        {
            int steps = Mathf.CeilToInt(distance / 0.35f);
            return Mathf.Clamp(steps, 1, 8);
        }
    }
}
