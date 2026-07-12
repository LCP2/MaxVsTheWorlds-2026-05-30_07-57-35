using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Lets a beeline chaser get around a solid thing in its way (YT-68).
    ///
    /// The robots steer straight at Max with no NavMesh — fine on an empty plane, but the lawn now
    /// has cover in it. A CharacterController already slides along a surface it's pushed into, so a
    /// robot approaching cover at an angle rounds it by itself; what it CAN'T do is get off a wall
    /// it has hit dead-on, because the slide has no sideways component to work with. It just stands
    /// there pressing into the prop. That's the whole failure mode, and this fixes it: when a chaser
    /// is against a wall, it walks ALONG the wall instead of into it, and if it's hit the wall
    /// head-on it commits to one side and goes around.
    ///
    /// Pure maths, no physics — so it's unit-testable.
    /// </summary>
    public static class ObstacleSteering
    {
        /// <summary>Below this much sideways component, the chaser is effectively nose-on to the
        /// wall and sliding would leave it stuck — so it picks a side instead.</summary>
        private const float HeadOnThreshold = 0.2f;

        /// <summary>
        /// The direction to actually walk when <paramref name="desired"/> (the way Max is) runs into
        /// a wall with surface normal <paramref name="wallNormal"/>: the desired direction flattened
        /// onto the wall, i.e. walk along it in whichever direction still makes progress toward Max.
        /// Hitting it nose-on leaves no such direction, so <paramref name="preferSign"/> breaks the
        /// tie (+1 = go around one way, −1 = the other). Giving robots opposing signs makes a pack
        /// split and flow around cover from both sides rather than all piling on one face.
        ///
        /// All maths is on the XZ plane; Y is ignored. Returns a unit vector, or
        /// <paramref name="desired"/> unchanged when the normal is degenerate.
        /// </summary>
        public static Vector3 SlideAlongWall(Vector3 desired, Vector3 wallNormal, float preferSign)
        {
            desired.y = 0f;
            wallNormal.y = 0f;

            if (wallNormal.sqrMagnitude < 1e-6f || desired.sqrMagnitude < 1e-6f) return desired;

            wallNormal.Normalize();
            desired.Normalize();

            // Walking into the wall? If we're already moving away from it, the wall isn't in the way.
            if (Vector3.Dot(desired, wallNormal) >= 0f) return desired;

            Vector3 along = Vector3.ProjectOnPlane(desired, wallNormal);
            if (along.sqrMagnitude < HeadOnThreshold * HeadOnThreshold)
            {
                // Nose-on: no sideways component to slide on. Commit to a side and round the corner.
                along = Vector3.Cross(Vector3.up, wallNormal) * (preferSign >= 0f ? 1f : -1f);
            }

            return along.normalized;
        }

        /// <summary>A stable +1/−1 for an enemy, so the same robot always rounds cover the same way
        /// (no jitter) but the pack as a whole splits both ways.</summary>
        public static float PreferSignFor(int instanceId) => (instanceId & 1) == 0 ? 1f : -1f;
    }
}
