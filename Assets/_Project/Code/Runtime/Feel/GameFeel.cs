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

        [Header("Missile (MV-349/MV-351)")]
        [Tooltip("Trauma per point of damage a missile actually dealt — proportionate feedback per " +
                 "AC2, so a graze and a full hit don't feel the same, and a miss (0 damage) is silent. " +
                 "MV-351: raised from 0.008 — at a Bomber's 22 damage, the old value only ever reached " +
                 "trauma 0.176, and shake intensity is trauma SQUARED (GameFeelTuning.ShakeAmount), so " +
                 "that was an all-but-invisible 0.013m of camera offset. Lee reported he couldn't feel it.")]
        [SerializeField] private float missileTraumaPerDamage = 0.022f;
        [SerializeField] private float missileMaxTrauma = 0.6f;
        [Tooltip("Brief freeze on a real missile hit (MV-351 AC3) — the punch a shake alone can't " +
                 "sell. Softer than the factory/boss big-stop; a splash hit is a real event, not the " +
                 "single biggest moment in the slice.")]
        [SerializeField] private float missileStopSeconds = 0.09f;
        [SerializeField] private float missileStopScale = 0.07f;

        [Header("Teleport")]
        [Tooltip("MV-338 AC3: a brief slow-mo while Max's teleport VFX plays. Noticeably softer than a " +
                 "kill/factory hit-stop (which reads as a near-freeze) — this has to read as time " +
                 "SLOWING for the blink, not stopping dead.")]
        [SerializeField] private float teleportSlowSeconds = 0.22f;
        [SerializeField] private float teleportSlowScale = 0.3f;

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
            HudSignals.MaxTeleported += OnMaxTeleported;
            HudSignals.MissileImpact += OnMissileImpact;
        }

        private void OnDisable()
        {
            HudSignals.DamageDealt -= OnDamage;
            HudSignals.EnemyKilled -= OnKill;
            HudSignals.FactoryDestroyed -= OnFactory;
            HudSignals.BossDefeated -= OnBossDefeated;
            HudSignals.MaxTeleported -= OnMaxTeleported;
            HudSignals.MissileImpact -= OnMissileImpact;
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

        /// <summary>AC2's "screen feedback proportionate to the damage": a miss (the blast landed on
        /// empty ground, damage 0) is silent, and a real hit shakes in proportion to what it actually
        /// dealt rather than a single flat jolt every time.</summary>
        private void OnMissileImpact(Vector3 pos, float damage)
        {
            if (damage <= 0f) return;
            Shake()?.AddTrauma(Mathf.Min(missileMaxTrauma, missileTraumaPerDamage * damage));
            TryStop(missileStopSeconds, missileStopScale);
        }

        /// <summary>MV-338 AC3. Requests directly rather than through <see cref="TryStop"/> — Teleport
        /// is already rate-limited by its own cooldown (spec: no button spams it several times a
        /// second the way a damage stream can), so the shared <see cref="minStopInterval"/> guard built
        /// for that spam case doesn't need to gate this too.</summary>
        private void OnMaxTeleported(Vector3 from, Vector3 to) => _stop.Request(teleportSlowSeconds, teleportSlowScale);

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
