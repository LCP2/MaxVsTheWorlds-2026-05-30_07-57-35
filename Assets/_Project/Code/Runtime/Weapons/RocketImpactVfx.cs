using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The Shoulder Rack rocket's impact (MV-702, closing a gap MV-694 left as damage-only): a ground
    /// ring sized to the rocket's own real splash radius, and a smaller pop for each cluster bomblet
    /// <see cref="PlayerRocket"/> already damages with. Reuses <see cref="GroundRing"/> directly rather
    /// than a whole particle-burst system — <see cref="MaxWorlds.VFX.WaterBalloonSplashVfx"/>'s own ring
    /// is the same shape, just scorched-orange instead of water-cyan so the two splash weapons don't
    /// read as the same attack.
    /// </summary>
    internal static class RocketImpactVfx
    {
        private const float SplashRingLifetimeSeconds = 0.3f;
        private const float BombletRingLifetimeSeconds = 0.2f;
        private const float BombletRingRadius = 1.2f;
        private const float RingStartRadius = 0.05f;

        private static readonly Color RingColor = new Color(0.85f, 0.55f, 0.25f, 0.8f);

        /// <summary>The rocket's own splash — sized to the real <see cref="PlayerRocket"/> splash radius
        /// (the ticket's "2 m splash ring" is the base-tuning value; a levelled-up radius must still
        /// draw the ring it actually damages, not a fixed 2 m).</summary>
        public static void PlaySplashRing(Vector3 point, float radius) =>
            Spawn("RocketSplashRing", point, Mathf.Max(0.3f, radius), SplashRingLifetimeSeconds);

        /// <summary>One small pop per cluster bomblet landing point.</summary>
        public static void PlayBombletPop(Vector3 point) =>
            Spawn("RocketBombletPop", point, BombletRingRadius, BombletRingLifetimeSeconds);

        private static void Spawn(string name, Vector3 point, float maxRadius, float lifetime)
        {
            var ring = GroundRing.Create(name);
            ring.gameObject.AddComponent<RingLifetime>().Begin(ring, point, maxRadius, lifetime);
        }

        /// <summary>Grows the ring to <c>maxRadius</c> over <c>lifetime</c>, fading it out, then destroys
        /// its own GameObject — the same shape <see cref="MaxWorlds.VFX.WaterBalloonSplashVfx"/>'s own
        /// ring animates on, just self-contained since a rocket detonation has no owning component left
        /// alive to drive it (<see cref="PlayerRocket.Detonate"/> destroys itself the same frame).</summary>
        private sealed class RingLifetime : MonoBehaviour
        {
            private GroundRing _ring;
            private Vector3 _origin;
            private float _maxRadius;
            private float _life;
            private float _t;

            public void Begin(GroundRing ring, Vector3 origin, float maxRadius, float life)
            {
                _ring = ring;
                _origin = origin;
                _maxRadius = maxRadius;
                _life = life;
                _ring.Show(_origin, RingStartRadius, RingColor);
            }

            private void Update()
            {
                _t += Time.deltaTime;
                float f = Mathf.Clamp01(_t / _life);
                _ring.Show(_origin, Mathf.Lerp(RingStartRadius, _maxRadius, f),
                    new Color(RingColor.r, RingColor.g, RingColor.b, RingColor.a * (1f - f)));
                if (f >= 1f)
                {
                    _ring.Hide();
                    Destroy(gameObject);
                }
            }
        }
    }
}
