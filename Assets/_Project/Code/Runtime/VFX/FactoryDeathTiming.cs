using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The shape of the factory's death (YT-109), in one place so the beat can be read as a whole.
    ///
    /// It used to be a single frame: fire, chunks and smoke all emitted together, everything gone
    /// inside about a second. The thing the entire slice teaches you to do resolved faster than
    /// killing one of the robots it makes. These are the gaps that turn it into a sequence — it
    /// fails, it lets go, it burns, it settles.
    ///
    /// Constants rather than a ScriptableObject because they describe one another: the husk must
    /// still be standing when the main blast lands, and must be gone before the smoke thins. A tuning
    /// asset that let those drift apart would let someone tune the beat into nonsense a field at a time.
    /// </summary>
    public static class FactoryDeathTiming
    {
        /// <summary>First contained failure → the blast that takes the building.</summary>
        public const float FailToBlast = 0.55f;

        /// <summary>Blast → the smoke that hangs after it. Smoke arriving WITH the fire is just more
        /// stuff in the same puff; arriving after it, it reads as aftermath.</summary>
        public const float BlastToSmoke = 0.65f;

        /// <summary>Smoke → the last embers off the wreck.</summary>
        public const float SmokeToEmbers = 1.1f;

        /// <summary>How long the husk shudders before the blast. Ends exactly when the blast lands.</summary>
        public const float ShudderSeconds = FailToBlast;

        /// <summary>How long the husk takes to go down, once the blast has hit it.</summary>
        public const float CollapseSeconds = 1.05f;

        /// <summary>Total span from the killing shot to the last ember being emitted.</summary>
        public static float Total => FailToBlast + BlastToSmoke + SmokeToEmbers;

        /// <summary>
        /// How far through its collapse the husk is at <paramref name="t"/> seconds after death,
        /// 0 (standing) … 1 (gone). Flat until the blast — the building does not sag while it is
        /// merely failing, it stands there shaking and then it drops.
        /// </summary>
        public static float CollapseProgress(float t)
        {
            if (t <= ShudderSeconds) return 0f;
            if (CollapseSeconds <= 1e-4f) return 1f;
            float u = Mathf.Clamp01((t - ShudderSeconds) / CollapseSeconds);
            // Accelerating: it gives way slowly and then goes all at once, which is how a structure
            // falls. A linear sink reads as a lift being lowered.
            return u * u;
        }

        /// <summary>Sideways shudder offset, in metres, at <paramref name="t"/> seconds after death.
        /// Grows as the failure builds, and stops dead the moment the collapse starts.</summary>
        public static float ShudderOffset(float t, float amplitude)
        {
            if (t < 0f || t > ShudderSeconds) return 0f;
            float ramp = ShudderSeconds <= 1e-4f ? 1f : Mathf.Clamp01(t / ShudderSeconds);
            return Mathf.Sin(t * 46f) * amplitude * ramp;
        }
    }
}
