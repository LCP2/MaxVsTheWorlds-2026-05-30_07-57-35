using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Where a robot appears when the factory emits it (YT-70).
    ///
    /// The old spawner scattered robots on a full ring around its origin, which meant they popped
    /// into being on every side of the shed — including behind it, and (when the player stood close)
    /// effectively on top of the player. Nothing about that read as "these came OUT of that
    /// building". So emission is now a MOUTH: robots leave through the factory's player-facing face,
    /// fanned across an arc, and then chase. Seen from the fixed camera that's a stream flowing out
    /// of the source and down the lawn at you — which is the whole "kill the source" fantasy made
    /// visible.
    ///
    /// Pure maths, no scene references — so the emission pattern is unit-testable.
    /// </summary>
    public static class FactoryMouth
    {
        /// <summary>Golden-ratio conjugate. Successive multiples of it, taken mod 1, never clump and
        /// never repeat a pattern — so consecutive robots leave by noticeably different parts of the
        /// mouth (a stream), instead of the vertical stripe a fixed step would give.</summary>
        private const float GoldenConjugate = 0.618033988f;

        /// <summary>Position across the mouth for the n-th robot, in −1 (hard left) … +1 (hard right).</summary>
        public static float FanOffset(int index)
        {
            float f = index * GoldenConjugate;
            return (f - Mathf.Floor(f)) * 2f - 1f;
        }

        /// <summary>
        /// The direction the n-th robot walks out of the factory: toward
        /// <paramref name="towardTarget"/> (Max), swung across the mouth by up to
        /// <paramref name="halfAngleDeg"/>. Falls back to <paramref name="mouthFacing"/> — the
        /// factory's own front face — when there's no target or it's standing inside the factory,
        /// so robots still emerge forwards rather than from a degenerate direction.
        /// Always flat (XZ) and unit length.
        /// </summary>
        public static Vector3 ExitDirection(Vector3 towardTarget, Vector3 mouthFacing, int index,
                                            float halfAngleDeg)
        {
            Vector3 fwd = Flatten(towardTarget);
            if (fwd == Vector3.zero) fwd = Flatten(mouthFacing);
            if (fwd == Vector3.zero) fwd = Vector3.back; // last resort: the scene's down-lawn direction

            return Quaternion.AngleAxis(FanOffset(index) * halfAngleDeg, Vector3.up) * fwd;
        }

        /// <summary>Where that robot stands the instant it appears: clear of the factory body, on the
        /// ground, out in front of the mouth.</summary>
        public static Vector3 ExitPoint(Vector3 factory, Vector3 exitDirection, float radius, float y)
        {
            Vector3 p = factory + exitDirection * radius;
            p.y = y;
            return p;
        }

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-6f ? Vector3.zero : v.normalized;
        }
    }
}
