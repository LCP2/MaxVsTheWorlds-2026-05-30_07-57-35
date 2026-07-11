using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Pure motion/fade curves for floating combat text (YT-30 HUD): damage numbers and
    /// pickup text that "float up and fade". Separated from the rendering layer so the
    /// timing maths is unit-testable without a Camera or Canvas. Distances are in screen
    /// pixels; callers scale by DPI as needed.
    /// </summary>
    public static class FloatingTextMotion
    {
        /// <summary>Screen-space rise, in pixels, over a full life (ease-out so it decelerates).</summary>
        public const float RisePixels = 64f;

        /// <summary>Extra scale a crit "pops" to at birth before settling to 1 (0 = none).</summary>
        public const float CritPopScale = 0.35f;

        /// <summary>Normalised life progress 0..1 for an age against a lifetime.</summary>
        public static float Progress(float age, float lifetime)
        {
            if (lifetime <= 0f) return 1f;
            return Mathf.Clamp01(age / lifetime);
        }

        /// <summary>Alpha over life: solid for the first ~40%, then linear fade to 0.
        /// Matches the spec's "small, fade-out" damage numbers.</summary>
        public static float AlphaAt(float progress)
        {
            progress = Mathf.Clamp01(progress);
            const float holdUntil = 0.4f;
            if (progress <= holdUntil) return 1f;
            return 1f - (progress - holdUntil) / (1f - holdUntil);
        }

        /// <summary>Upward offset in pixels at a given life progress (ease-out rise).</summary>
        public static float RiseAt(float progress)
        {
            progress = Mathf.Clamp01(progress);
            float eased = 1f - (1f - progress) * (1f - progress); // ease-out quad
            return eased * RisePixels;
        }

        /// <summary>Scale at a given life progress. Crits pop large then settle to 1;
        /// normal text stays at 1.</summary>
        public static float ScaleAt(float progress, bool crit)
        {
            if (!crit) return 1f;
            progress = Mathf.Clamp01(progress);
            // Pop from (1+CritPop) down to 1 over the first 25% of life.
            float t = Mathf.Clamp01(progress / 0.25f);
            return Mathf.Lerp(1f + CritPopScale, 1f, t);
        }
    }
}
