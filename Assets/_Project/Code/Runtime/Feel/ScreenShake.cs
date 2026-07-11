using UnityEngine;

namespace MaxWorlds.Feel
{
    /// <summary>
    /// Camera shake and recoil kick (YT-52).
    ///
    /// Rides on top of the Cinemachine rig rather than fighting it. Two details make that work:
    ///
    /// * <b>Execution order.</b> CinemachineBrain writes the camera transform in LateUpdate, so
    ///   this runs at a late order to add its offset *after* the rig has had its say. The fixed
    ///   ~72° framing is therefore never overwritten — it's offset and then restored.
    /// * <b>It undoes its own offset first.</b> If the brain doesn't rewrite the transform on some
    ///   frame, blindly adding an offset every frame would drift the camera away for good. Each
    ///   frame it subtracts what it added last frame before computing the new one.
    ///
    /// Runs on unscaled time, so the shake keeps moving during a <see cref="HitStop"/> — a shake
    /// that freezes with the hit-stop is exactly the impact you were trying to sell, held still.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]   // after CinemachineBrain
    public sealed class ScreenShake : MonoBehaviour
    {
        [Header("Shake")]
        [Tooltip("Metres of camera displacement at full trauma. Small: this is polish, not a bus crash.")]
        [SerializeField] private float maxOffset = 0.42f;
        [Tooltip("Degrees of roll at full trauma.")]
        [SerializeField] private float maxRoll = 1.4f;
        [SerializeField] private float frequency = 22f;
        [Tooltip("Trauma bled off per second. Higher = snappier, shorter shakes.")]
        [SerializeField] private float decayPerSecond = 1.9f;

        [Header("Kick")]
        [Tooltip("Seconds for a recoil kick to fall back to zero.")]
        [SerializeField] private float kickDecay = 9f;
        [Tooltip("Ceiling on recoil displacement, metres.")]
        [SerializeField] private float maxKick = 0.22f;

        private float _trauma;
        private Vector3 _kick;
        private Vector3 _lastOffset;
        private Quaternion _lastRoll = Quaternion.identity;
        private float _seed;

        /// <summary>Current trauma, 0..1. Exposed for tests and tuning UI.</summary>
        public float Trauma => _trauma;

        private void Awake() => _seed = Random.value * 100f;

        /// <summary>Shake the camera. <paramref name="amount"/> is trauma (0..1); it adds to
        /// whatever is already in flight.</summary>
        public void AddTrauma(float amount) => _trauma = GameFeelTuning.AddTrauma(_trauma, amount);

        /// <summary>A directional recoil punch — the camera gets shoved back along the shot.</summary>
        public void Kick(Vector3 worldDirection, float strength)
        {
            if (worldDirection.sqrMagnitude < 1e-6f) return;
            _kick = Vector3.ClampMagnitude(_kick - worldDirection.normalized * strength, maxKick);
        }

        private void LateUpdate()
        {
            // Undo last frame's contribution so the offset never accumulates, whatever the rig did.
            transform.position -= _lastOffset;
            transform.rotation = Quaternion.Inverse(_lastRoll) * transform.rotation;

            float dt = Time.unscaledDeltaTime;
            _trauma = GameFeelTuning.DecayTrauma(_trauma, dt, decayPerSecond);
            _kick = Vector3.Lerp(_kick, Vector3.zero, 1f - Mathf.Exp(-kickDecay * dt));

            Vector3 shake = GameFeelTuning.ShakeOffset(
                _trauma, Time.unscaledTime + _seed, maxOffset, frequency);

            Vector3 offset = shake + _kick;
            float rollDeg = GameFeelTuning.ShakeAmount(_trauma) * maxRoll *
                            (Mathf.PerlinNoise((Time.unscaledTime + _seed) * frequency, 5f) * 2f - 1f);
            Quaternion roll = Quaternion.AngleAxis(rollDeg, transform.forward);

            transform.position += offset;
            transform.rotation = roll * transform.rotation;

            _lastOffset = offset;
            _lastRoll = roll;
        }

        private void OnDisable()
        {
            // Hand the camera back exactly as we found it.
            transform.position -= _lastOffset;
            transform.rotation = Quaternion.Inverse(_lastRoll) * transform.rotation;
            _lastOffset = Vector3.zero;
            _lastRoll = Quaternion.identity;
            _trauma = 0f;
            _kick = Vector3.zero;
        }
    }
}
