using UnityEngine;

namespace MaxWorlds.Feel
{
    /// <summary>
    /// Pure maths behind the game-feel pass (YT-52) — unit-testable with no scene, no camera
    /// and no clock.
    /// </summary>
    public static class GameFeelTuning
    {
        /// <summary>Trauma decays linearly; shake intensity is trauma SQUARED.
        ///
        /// The squaring is the whole trick: it makes a big hit hit hard while small, frequent
        /// hits stay almost imperceptible, and it makes the shake fall away sharply rather than
        /// wallowing. Linear trauma-to-shake reads as a constant wobble.</summary>
        public static float ShakeAmount(float trauma) => Mathf.Clamp01(trauma) * Mathf.Clamp01(trauma);

        /// <summary>Trauma left after <paramref name="dt"/> seconds.</summary>
        public static float DecayTrauma(float trauma, float dt, float perSecond)
        {
            return Mathf.Clamp01(trauma - perSecond * Mathf.Max(0f, dt));
        }

        /// <summary>Add trauma, clamped. Traumas add rather than overwrite, so a kill during a
        /// factory explosion shakes harder — but the clamp stops a crowd wipe from pegging it.</summary>
        public static float AddTrauma(float current, float amount)
        {
            return Mathf.Clamp01(current + Mathf.Max(0f, amount));
        }

        /// <summary>
        /// Shake offset at a moment in time, from Perlin noise (not Random): consecutive frames
        /// have to be *correlated* or the camera jitters like static instead of shaking like a
        /// camera. Each axis walks its own noise row.
        /// </summary>
        public static Vector3 ShakeOffset(float trauma, float time, float maxOffset, float frequency)
        {
            float amount = ShakeAmount(trauma);
            if (amount <= 0f) return Vector3.zero;

            float t = time * frequency;
            // Perlin returns 0..1; centre it to -1..1.
            float x = Mathf.PerlinNoise(t, 0f) * 2f - 1f;
            float y = Mathf.PerlinNoise(0f, t) * 2f - 1f;
            float z = Mathf.PerlinNoise(t, t) * 2f - 1f;

            return new Vector3(x, y, z) * (amount * maxOffset);
        }

        /// <summary>
        /// Whether a hit-stop is allowed to fire now. Rate-limited hard: a sustained water stream
        /// lands a damage tick every 0.1s on every enemy it touches, and freezing time on each one
        /// would turn the whole game into a stutter.
        /// </summary>
        public static bool CanHitStop(float now, float lastStopAt, float minInterval)
        {
            return now - lastStopAt >= minInterval;
        }
    }
}
