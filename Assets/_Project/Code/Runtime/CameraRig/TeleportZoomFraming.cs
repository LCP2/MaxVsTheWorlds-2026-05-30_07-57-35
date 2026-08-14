using UnityEngine;

namespace MaxWorlds.CameraRig
{
    /// <summary>
    /// How far the camera needs to pull back so a circle of a given radius around the look-at point
    /// is fully on screen (MV-371: teleport's selectable range already exceeds what the player can
    /// see at the upper ability levels). Pure ray/ground-plane trig — the same load-bearing pitch
    /// used by <see cref="FixedAngleCameraRig.ComputeOffset"/> — so "does the range fit" is exact
    /// arithmetic, not eyeballed.
    ///
    /// The ground-plane intersection of any fixed viewport direction scales EXACTLY linearly with
    /// camera distance, for a fixed pitch/FOV/aspect and a look-at point fixed at the origin: the
    /// camera's position and orientation are both unchanged in shape as distance scales (only the
    /// position slides along the same ray), so every frustum-edge/ground-plane hit point scales by
    /// the same factor as the distance. That's what lets <see cref="DistanceForVisibleRadius"/> just
    /// measure the unit-distance case and scale it, rather than solving per-target.
    /// </summary>
    public static class TeleportZoomFraming
    {
        /// <summary>Ground-plane distance from the look-at point (world origin) to the screen edge,
        /// along the tightest of the four cardinal viewport directions (near/far/left/right), at
        /// camera <paramref name="distance"/>. This is the radius of the largest circle around the
        /// look-at point guaranteed fully on screen — the near edge (toward the camera) is usually
        /// the tightest for a camera pitched back rather than tipped fully overhead.</summary>
        public static float SafeVisibleRadius(
            float distance, float pitchDegrees, float verticalFovDegrees, float aspect)
        {
            if (distance <= 0f) return 0f;

            float pitch = pitchDegrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(pitch);
            float cos = Mathf.Cos(pitch);

            float vHalf = verticalFovDegrees * 0.5f * Mathf.Deg2Rad;
            float tanV = Mathf.Tan(vHalf);
            float hHalf = Mathf.Atan(tanV * Mathf.Max(0.01f, aspect));
            float tanH = Mathf.Tan(hHalf);

            // Camera position relative to the look-at point (matches FixedAngleCameraRig.ComputeOffset).
            Vector3 camPos = new Vector3(0f, distance * sin, -distance * cos);

            // forward = (0, -sin, cos); up = (0, cos, sin); right = (1, 0, 0) — the rig never yaws or rolls.
            Vector3 near = new Vector3(0f, -sin - cos * tanV, cos - sin * tanV);   // bottom of screen, toward the camera
            Vector3 far = new Vector3(0f, -sin + cos * tanV, cos + sin * tanV);    // top of screen, away from the camera
            Vector3 side = new Vector3(tanH, -sin, cos);                          // left/right are symmetric

            float rNear = GroundRadius(camPos, near);
            float rFar = GroundRadius(camPos, far);
            float rSide = GroundRadius(camPos, side);

            return Mathf.Min(Mathf.Min(rNear, rFar), rSide);
        }

        /// <summary>Where the ray from <paramref name="origin"/> along <paramref name="dir"/> crosses
        /// the ground plane y=0, as a horizontal distance from the world origin. Infinity if the ray
        /// points at or above the horizon and never reaches the ground.</summary>
        private static float GroundRadius(Vector3 origin, Vector3 dir)
        {
            if (dir.y >= -1e-5f) return float.PositiveInfinity;
            float t = -origin.y / dir.y;
            Vector3 hit = origin + dir * t;
            return new Vector2(hit.x, hit.z).magnitude;
        }

        /// <summary>The camera distance whose <see cref="SafeVisibleRadius"/> equals
        /// <paramref name="desiredRadius"/> — the unit-distance radius scaled up, per the class
        /// summary. Falls back to <see cref="FixedAngleCameraRig.MaxDistance"/> if the geometry can't
        /// resolve a finite radius at all (e.g. a FOV so narrow the ground never fills frame).</summary>
        public static float DistanceForVisibleRadius(
            float desiredRadius, float pitchDegrees, float verticalFovDegrees, float aspect)
        {
            if (desiredRadius <= 0f) return 0f;

            float unitRadius = SafeVisibleRadius(1f, pitchDegrees, verticalFovDegrees, aspect);
            if (unitRadius <= 0f || float.IsInfinity(unitRadius)) return FixedAngleCameraRig.MaxDistance;

            return desiredRadius / unitRadius;
        }
    }
}
