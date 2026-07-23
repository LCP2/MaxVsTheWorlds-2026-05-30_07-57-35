using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Pure, allocation-free maths behind the water VFX. Kept separate from the
    /// MonoBehaviour so it can be unit-tested with no scene, no physics, and no
    /// rendering — the prevailing test style in this repo.
    /// </summary>
    public static class WaterVfxTuning
    {
        /// <summary>Hard ceiling on splash emissions serviced in a single frame. The blaster
        /// is a volume weapon: at ~20–30 enemies a single fire tick can hit a dozen bodies at
        /// once, and an uncapped splash-per-hit turns into a particle spike. Impacts beyond
        /// this are dropped (the ones that survive are indistinguishable to the eye).</summary>
        public const int MaxSplashesPerFrame = 8;

        /// <summary>Droplets emitted per splash. Scaled by damage so a chip hit and a
        /// full-power hit don't look identical, but clamped so the count stays predictable.</summary>
        public static int SplashDroplets(float damage)
        {
            if (damage <= 0f) return 0;
            int n = Mathf.RoundToInt(4f + damage * 0.75f);
            return Mathf.Clamp(n, 4, 14);
        }

        /// <summary>
        /// Nearest point to <paramref name="target"/> on the fire ray, clamped to the
        /// stream's length. Used to place the impact splash on the surface facing the
        /// blaster instead of at the target's centre (which is where the damage event
        /// reports it). Cosmetic only — it never feeds damage.
        /// </summary>
        public static Vector3 NearestPointOnRay(Vector3 origin, Vector3 dir, float range, Vector3 target)
        {
            Vector3 d = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            float t = Mathf.Clamp(Vector3.Dot(target - origin, d), 0f, Mathf.Max(0f, range));
            return origin + d * t;
        }

        /// <summary>
        /// Splash direction: water hitting a body sprays back toward the shooter and upward,
        /// never straight through the target. <paramref name="fireDir"/> is the stream's travel
        /// direction; the result is a normalised cone axis for the droplet burst.
        /// </summary>
        public static Vector3 SplashAxis(Vector3 fireDir)
        {
            Vector3 f = fireDir.sqrMagnitude > 1e-6f ? fireDir.normalized : Vector3.forward;
            // 55% back along the incoming stream, plus a strong upward bias so the burst
            // is visible from the fixed ~72° top-down camera rather than hidden behind the body.
            Vector3 axis = -f * 0.55f + Vector3.up;
            return axis.normalized;
        }

        /// <summary>
        /// How far a particle must travel from the muzzle so that the WIDEST visible edge of the
        /// spray — <paramref name="halfAngleDeg"/>, the stream's own half-angle — still lands
        /// exactly on the aim outline at <paramref name="range"/> from the weapon's origin (YT-177).
        ///
        /// The muzzle sits <paramref name="muzzleOffset"/> out in front of that origin, so a
        /// particle travelling straight down the centre line only has to cover
        /// <c>range - muzzleOffset</c> — the old formula, and still exactly what this returns at
        /// angle zero. But a particle emitted off to the side leaves from that SAME muzzle point at
        /// an angle: its straight-line path is the third side of a triangle with the offset, not a
        /// plain subtraction, and the wider the cone the shorter that subtraction fell — a wide
        /// weapon (big half-angle) opened a visible gap between the water and the reticle that a
        /// narrow one never showed enough to notice. Solving that triangle for the edge particle's
        /// travel distance is what keeps every angle on the outline, not just the centre.
        /// </summary>
        public static float ReachForCone(float range, float muzzleOffset, float halfAngleDeg)
        {
            range = Mathf.Max(0.01f, range);
            muzzleOffset = Mathf.Max(0f, muzzleOffset);
            float rad = Mathf.Clamp(halfAngleDeg, 0f, 89f) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            float underRoot = Mathf.Max(0f, range * range - muzzleOffset * muzzleOffset * sin * sin);
            float reach = Mathf.Sqrt(underRoot) - muzzleOffset * cos;
            return Mathf.Max(0.1f, reach);
        }
    }
}
