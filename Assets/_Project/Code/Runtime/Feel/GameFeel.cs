using UnityEngine;
using MaxWorlds.UI;
using MaxWorlds.Combat;

namespace MaxWorlds.Feel
{
    /// <summary>
    /// Maps combat events to feel (YT-52): hit-stop, screen shake, and a recoil kick while firing.
    ///
    /// Like the VFX director, it listens to the existing <see cref="HudSignals"/> bus and reads
    /// public state, so it adds no gameplay coupling and installs itself with no scene edit.
    ///
    /// The load-bearing decision here is restraint. The Water Blaster is a sustained volume weapon:
    /// it lands a damage tick every 0.1s on *every* enemy it touches, so at 20–30 enemies that's
    /// hundreds of damage events a second. Freezing time or shaking the camera on each one would be
    /// unplayable. So:
    ///
    /// * plain hits get a whisper of trauma and NO hit-stop;
    /// * hit-stop is reserved for kills and big events, and is rate-limited on top of that;
    /// * trauma is clamped, so a crowd wipe can't peg the shake.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameFeel : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<GameFeel>() != null) return;
            new GameObject("GameFeel").AddComponent<GameFeel>();
        }

        [Header("Hit-stop")]
        [SerializeField] private float killStopSeconds = 0.045f;
        [SerializeField] private float killStopScale = 0.08f;
        [SerializeField] private float bigStopSeconds = 0.11f;
        [SerializeField] private float bigStopScale = 0.05f;
        [Tooltip("Minimum real seconds between hit-stops. Without this, a stream through a crowd " +
                 "would freeze time several times a second and the game would stutter, not punch.")]
        [SerializeField] private float minStopInterval = 0.22f;

        [Header("Trauma")]
        [SerializeField] private float hitTrauma = 0.055f;
        [SerializeField] private float killTrauma = 0.2f;
        [SerializeField] private float factoryTrauma = 0.75f;
        [SerializeField] private float bossDefeatTrauma = 0.85f;

        [Header("Blaster kick")]
        [Tooltip("Recoil per fire tick while the stream is on.")]
        [SerializeField] private float fireKick = 0.05f;

        private ScreenShake _shake;
        private HitStop _stop;
        private WaterBlaster _blaster;
        private float _lastStopAt = -99f;

        private void Awake()
        {
            _stop = gameObject.AddComponent<HitStop>();
        }

        private void OnEnable()
        {
            HudSignals.DamageDealt += OnDamage;
            HudSignals.EnemyKilled += OnKill;
            HudSignals.FactoryDestroyed += OnFactory;
            HudSignals.BossDefeated += OnBossDefeated;
        }

        private void OnDisable()
        {
            HudSignals.DamageDealt -= OnDamage;
            HudSignals.EnemyKilled -= OnKill;
            HudSignals.FactoryDestroyed -= OnFactory;
            HudSignals.BossDefeated -= OnBossDefeated;
        }

        /// <summary>The shake lives on the camera, not here — it has to run after the Cinemachine
        /// brain writes the transform. Found lazily because the camera is built by the rig scaffold.</summary>
        private ScreenShake Shake()
        {
            if (_shake != null) return _shake;
            var cam = Camera.main;
            if (cam == null) return null;
            _shake = cam.GetComponent<ScreenShake>();
            if (_shake == null) _shake = cam.gameObject.AddComponent<ScreenShake>();
            return _shake;
        }

        private void OnDamage(Vector3 pos, float amount, bool crit)
        {
            // Deliberately no hit-stop here — see the class summary.
            Shake()?.AddTrauma(hitTrauma * (crit ? 2f : 1f));
        }

        private void OnKill(Vector3 pos)
        {
            Shake()?.AddTrauma(killTrauma);
            TryStop(killStopSeconds, killStopScale);
        }

        private void OnFactory(Vector3 pos)
        {
            Shake()?.AddTrauma(factoryTrauma);
            TryStop(bigStopSeconds, bigStopScale);
        }

        private void OnBossDefeated()
        {
            Shake()?.AddTrauma(bossDefeatTrauma);
            TryStop(bigStopSeconds, bigStopScale);
        }

        private void TryStop(float seconds, float scale)
        {
            float now = Time.unscaledTime;
            if (!GameFeelTuning.CanHitStop(now, _lastStopAt, minStopInterval)) return;
            _lastStopAt = now;
            _stop.Request(seconds, scale);
        }

        private void Update()
        {
            if (_blaster == null)
            {
                _blaster = FindFirstObjectByType<WaterBlaster>();
                if (_blaster == null) return;
            }

            // A gentle, continuous shove back along the stream while firing — a spray weapon
            // pushes, it doesn't punch, so this is a lean rather than a per-shot jolt.
            if (_blaster.IsFiring)
            {
                Shake()?.Kick(_blaster.transform.forward, fireKick * Time.unscaledDeltaTime);
            }
        }
    }
}
