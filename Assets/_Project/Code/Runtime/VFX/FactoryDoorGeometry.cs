using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The maths behind the factory's loading door and ramp (YT-108) — which wall the door goes on,
    /// how high the ramp is under a robot walking down it, and how far open the shutter is.
    ///
    /// Kept free of scene references on purpose. Every one of these is a decision that is wrong in a
    /// way you cannot see from a screenshot — a door on the wall facing the fence, a ramp that lifts a
    /// robot into the air, a shutter that lets robots through while it is still shut — so they are
    /// worth pinning in EditMode rather than discovering on a deploy.
    /// </summary>
    public static class FactoryDoorGeometry
    {
        /// <summary>The four walls a door can go on. The map never rotates a factory, so a factory is
        /// always an axis-aligned box and these are always its faces.</summary>
        public static readonly Vector3[] Faces =
        {
            Vector3.forward, Vector3.back, Vector3.right, Vector3.left,
        };

        /// <summary>
        /// Which face the door belongs on, given how much open ground each one has in front of it.
        ///
        /// Clearance decides it: a door is for walking out of, so it goes on the side with somewhere
        /// to walk. In the shipped map that is what puts the Mower Hutch's door on its west wall — the
        /// shed is symmetric, but the west wall has the opening onto the lawn, so a probe that way
        /// travels out into the lawn while the other three stop at a wall.
        ///
        /// MV-1107: a face <paramref name="blocked"/> marks is skipped outright, whatever its own
        /// clearance reads — a raycast-only clearance can't see a colliderless prop (a decorative pipe
        /// run, a kerb) sitting right where the ramp would go, which is how a door used to land facing
        /// one. There is no player tie-break any more: the face choice must never depend on where the
        /// player happens to stand at build time (MV-1107's own root cause), so a genuine tie resolves
        /// to the lowest face index — deterministic, not positional.
        /// </summary>
        /// <param name="clearances">Metres of open ground off each face, indexed like <see cref="Faces"/>.</param>
        /// <param name="blocked">True for a face whose own candidate ramp footprint is occupied by
        /// some other renderer — never chosen unless every face is. Null treats every face as open.</param>
        public static int ChooseFace(float[] clearances, bool[] blocked)
        {
            if (clearances == null || clearances.Length == 0) return 0;

            int best = -1;
            float bestClear = float.NegativeInfinity;

            for (int i = 0; i < clearances.Length && i < Faces.Length; i++)
            {
                if (blocked != null && i < blocked.Length && blocked[i]) continue;
                if (clearances[i] > bestClear) { bestClear = clearances[i]; best = i; }
            }

            // Every face blocked — a door still has to go somewhere, so fall back to whichever one
            // has the most open ground rather than building nothing.
            if (best < 0)
            {
                for (int i = 0; i < clearances.Length && i < Faces.Length; i++)
                    if (clearances[i] > bestClear) { bestClear = clearances[i]; best = i; }
            }

            return best < 0 ? 0 : best;
        }

        /// <summary>MV-1107: the world AABB a ramp built outward from <paramref name="doorway"/> along
        /// <paramref name="outward"/> would occupy — used to test a candidate face against scene
        /// renderers BEFORE anything is built there (see <see cref="ChooseFace(float[], bool[])"/>'s own
        /// caller). <paramref name="outward"/> is always one of <see cref="Faces"/>, so the box this
        /// describes is always axis-aligned and a plain min/max suffices.</summary>
        public static Bounds RampFootprint(Vector3 doorway, Vector3 outward, float sillHeight, float rampRun, float halfWidth)
        {
            Vector3 across = Vector3.Cross(Vector3.up, outward);
            Vector3 corner0 = doorway - across * halfWidth;
            Vector3 corner1 = doorway + across * halfWidth + outward * rampRun + Vector3.up * sillHeight;

            var bounds = new Bounds();
            bounds.SetMinMax(Vector3.Min(corner0, corner1), Vector3.Max(corner0, corner1));
            return bounds;
        }

        /// <summary>
        /// Height of the ramp surface <paramref name="distanceFromWall"/> metres out from the door.
        /// Full <paramref name="sill"/> at the doorway, ground at the bottom of the run, and flat
        /// ground either side of it — so a robot that has finished walking down is standing on the
        /// lawn, not hovering a hand's width above it.
        /// </summary>
        public static float RampHeightAt(float distanceFromWall, float sill, float run)
        {
            if (run <= 1e-4f) return 0f;
            float t = Mathf.Clamp01(distanceFromWall / run);
            return Mathf.Lerp(sill, 0f, t) * (distanceFromWall < 0f ? 0f : 1f);
        }

        /// <summary>
        /// Ramp height under a world position, or 0 if that position is not on this ramp.
        /// <paramref name="outward"/> and <paramref name="across"/> are the ramp's own axes.
        /// </summary>
        public static float RampLiftAt(Vector3 worldPos, Vector3 doorway, Vector3 outward,
                                       Vector3 across, float sill, float run, float halfWidth)
        {
            Vector3 offset = worldPos - doorway;
            offset.y = 0f;

            float along = Vector3.Dot(offset, outward);
            if (along < 0f || along > run) return 0f;
            if (Mathf.Abs(Vector3.Dot(offset, across)) > halfWidth) return 0f;

            return RampHeightAt(along, sill, run);
        }

        /// <summary>
        /// How far open the shutter is, 0 shut … 1 fully up, for a door that has been opening (or
        /// closing) for <paramref name="elapsed"/> seconds.
        /// </summary>
        public static float Openness(float elapsed, float travelSeconds, bool opening)
        {
            if (travelSeconds <= 1e-4f) return opening ? 1f : 0f;
            float t = Mathf.Clamp01(elapsed / travelSeconds);
            // Smoothed rather than linear: a shutter has mass, and a linear slide reads as a texture
            // being scrolled rather than as a door being hauled up.
            float eased = Mathf.SmoothStep(0f, 1f, t);
            return opening ? eased : 1f - eased;
        }
    }
}
