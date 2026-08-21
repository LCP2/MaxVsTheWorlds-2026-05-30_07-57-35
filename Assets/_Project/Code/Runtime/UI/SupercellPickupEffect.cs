using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Pure motion/fade curves for the Supercell "definite pickup event" (MV-519): a burst at the pickup
    /// point, a "+10" that travels to the cell readout, and the readout counting up — one self-
    /// terminating ~0.6s beat, nothing left on screen afterwards. Separated from <see cref="HudController"/>'s
    /// rendering so the timing maths is unit-testable without a canvas or real elapsed time, same idiom
    /// as <see cref="FloatingTextMotion"/>.
    /// </summary>
    public static class SupercellPickupEffect
    {
        /// <summary>Total lifetime, seconds — "roughly 0.6s" per the ticket.</summary>
        public const float Duration = 0.6f;

        /// <summary>Whether the event still has anything to show at <paramref name="age"/> seconds in.</summary>
        public static bool IsActive(float age) => age >= 0f && age < Duration;

        /// <summary>Normalised life progress 0..1 for an age against <see cref="Duration"/>.</summary>
        public static float Progress(float age) => Mathf.Clamp01(age / Duration);

        /// <summary>The burst's scale at <paramref name="age"/> — a quick ease-out expansion.</summary>
        public static float BurstScale(float age)
        {
            float t = Progress(age);
            float eased = 1f - (1f - t) * (1f - t);
            return Mathf.Lerp(0.6f, 2.2f, eased);
        }

        /// <summary>The burst's alpha at <paramref name="age"/> — fades linearly across the whole beat.</summary>
        public static float BurstAlpha(float age) => 1f - Progress(age);

        /// <summary>0..1 travel fraction from the pickup point toward the cell readout, eased in/out so
        /// the "+10" launches and arrives rather than drifting at a constant speed.</summary>
        public static float TravelT(float age)
        {
            float t = Progress(age);
            return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
        }

        /// <summary>The "+10" label's alpha — solid for most of the flight, fading only in the final
        /// fifth as it "lands" on the readout, so it never just vanishes mid-flight.</summary>
        public static float LabelAlpha(float age)
        {
            float t = Progress(age);
            const float fadeFrom = 0.8f;
            return t <= fadeFrom ? 1f : 1f - (t - fadeFrom) / (1f - fadeFrom);
        }

        /// <summary>The cell readout's own displayed count while it "counts up" from <paramref name="from"/>
        /// to <paramref name="to"/> across the beat — an integer lerp, so the read is a whole cell count
        /// at every sampled frame, never a fractional value.</summary>
        public static int CountAt(int from, int to, float age) =>
            Mathf.RoundToInt(Mathf.Lerp(from, to, Progress(age)));
    }
}
