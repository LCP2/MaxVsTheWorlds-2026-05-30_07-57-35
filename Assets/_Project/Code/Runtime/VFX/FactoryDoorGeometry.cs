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
        /// <paramref name="towardPlayer"/> breaks ties, which matters because a factory centred in a
        /// symmetric room has two equally open sides and picking by array order would put the door on
        /// whichever one the enum happened to list first. Ties are common, not exotic.
        /// </summary>
        /// <param name="clearances">Metres of open ground off each face, indexed like <see cref="Faces"/>.</param>
        /// <param name="towardPlayer">Direction to the player. Zero if there isn't one yet.</param>
        public static int ChooseFace(float[] clearances, Vector3 towardPlayer)
        {
            if (clearances == null || clearances.Length == 0) return 0;

            Vector3 pull = towardPlayer;
            pull.y = 0f;
            pull = pull.sqrMagnitude < 1e-6f ? Vector3.zero : pull.normalized;

            int best = 0;
            float bestClear = float.NegativeInfinity;
            float bestPull = float.NegativeInfinity;

            for (int i = 0; i < clearances.Length && i < Faces.Length; i++)
            {
                float clear = clearances[i];
                float toward = pull == Vector3.zero ? 0f : Vector3.Dot(Faces[i], pull);

                // Within a whisker of each other counts as the same clearance — otherwise a 1 cm
                // difference in where a probe happened to land silently outranks facing the player.
                bool clearlyBetter = clear > bestClear + 0.25f;
                bool tiedButFacesPlayer = clear > bestClear - 0.25f && toward > bestPull;

                if (clearlyBetter || tiedButFacesPlayer)
                {
                    best = i;
                    bestClear = Mathf.Max(clear, bestClear);
                    bestPull = toward;
                }
            }

            return best;
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
