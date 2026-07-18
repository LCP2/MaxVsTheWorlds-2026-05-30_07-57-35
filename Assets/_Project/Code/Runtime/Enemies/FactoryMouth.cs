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

        /// <summary>Where that robot has finished emerging: clear of the factory body, on the
        /// ground, out in front of the mouth. Since YT-100 this is where the robot walks TO, not
        /// where it appears — see <see cref="DoorPoint"/>.</summary>
        public static Vector3 ExitPoint(Vector3 factory, Vector3 exitDirection, float radius, float y)
        {
            Vector3 p = factory + exitDirection * radius;
            p.y = y;
            return p;
        }

        /// <summary>
        /// Where the robot APPEARS: in the doorway, hard against the factory wall on the face it is
        /// leaving by (YT-100). It then walks out to <see cref="ExitPoint"/>.
        ///
        /// The factory has no authored door — the greybox body is a plain box — so the mouth is
        /// wherever the exit direction meets that box, which is exactly what you want anyway: the
        /// door is on the side the robot is leaving from, and it moves around the building as the
        /// stream swings to follow Max.
        ///
        /// <paramref name="clearance"/> is pushed out past the wall so the robot's own body isn't
        /// born inside the factory's collider — an interpenetrating CharacterController gets ejected
        /// on its first move, which would fire it out of the shed rather than walk it out. Pass the
        /// robot's collider radius.
        /// </summary>
        public static Vector3 DoorPoint(Vector3 factory, Vector3 exitDirection, Vector3 bodySize,
                                        float clearance, float maxRadius, float y)
        {
            float t = SurfaceDistance(exitDirection, bodySize) + clearance;

            // Never further out than the exit point itself. A small enough factory (or a big enough
            // spawn radius) would otherwise put the door BEYOND the place it is walking to, and the
            // robot would emerge backwards, into the building.
            t = Mathf.Min(t, maxRadius);

            Vector3 p = factory + exitDirection * t;
            p.y = y;
            return p;
        }

        /// <summary>Distance from the centre of an axis-aligned box to its surface, along a flat
        /// direction. The map never rotates a factory, so the body stays axis-aligned and this is
        /// exact — the ray leaves through whichever face it reaches first.</summary>
        private static float SurfaceDistance(Vector3 direction, Vector3 bodySize)
        {
            Vector3 d = Flatten(direction);
            float halfX = Mathf.Abs(bodySize.x) * 0.5f;
            float halfZ = Mathf.Abs(bodySize.z) * 0.5f;

            float tx = Mathf.Abs(d.x) > 1e-4f ? halfX / Mathf.Abs(d.x) : float.MaxValue;
            float tz = Mathf.Abs(d.z) > 1e-4f ? halfZ / Mathf.Abs(d.z) : float.MaxValue;

            float t = Mathf.Min(tx, tz);
            return t == float.MaxValue ? 0f : t;   // degenerate direction: no wall to stand against
        }

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-6f ? Vector3.zero : v.normalized;
        }
    }
}
