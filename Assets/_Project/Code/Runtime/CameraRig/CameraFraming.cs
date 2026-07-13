using UnityEngine;

namespace MaxWorlds.CameraRig
{
    /// <summary>
    /// How far back the camera has to sit to show a given amount of arena (YT-82). Pure, so the
    /// framing claim is arithmetic anyone can check rather than a number someone once liked.
    ///
    /// The one thing worth knowing: visible ground area grows with the SQUARE of the distance, not
    /// with the distance. Both the width and the depth of what you can see scale linearly as you
    /// pull back, so the area scales by their product. Wanting "1.5x more arena on screen" therefore
    /// means moving back by √1.5 ≈ 1.22x, not 1.5x — pulling back by 1.5x would have shown you 2.25x
    /// the ground and put Max in a wide shot.
    /// </summary>
    public static class CameraFraming
    {
        /// <summary>The framing the slice shipped with (YT-46), and the baseline every "how much
        /// more can I see now" figure below is measured against.</summary>
        public const float PreviousDistance = 20.5f;

        /// <summary>How much more ground YT-82 asked for: half again the visible area.</summary>
        public const float TargetAreaScale = 1.5f;

        /// <summary>Distance needed to multiply the visible ground area by
        /// <paramref name="areaScale"/>, holding the pitch and field of view fixed.</summary>
        public static float DistanceForAreaScale(float baseDistance, float areaScale)
        {
            return baseDistance * Mathf.Sqrt(Mathf.Max(0f, areaScale));
        }

        /// <summary>The inverse: how much more ground <paramref name="distance"/> shows compared to
        /// <paramref name="baseDistance"/>. 1.0 = no change, 2.0 = twice the arena on screen.</summary>
        public static float AreaScaleForDistance(float baseDistance, float distance)
        {
            if (baseDistance <= 0f) return 0f;
            float linear = distance / baseDistance;
            return linear * linear;
        }
    }
}
