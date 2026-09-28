using System;
using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Shared damage feedback for friendly units — Sentinels and Max (MV-1005). The robots already
    /// flash white on a landed hit (<see cref="RobotRig"/>); this is the mirror read for "our side",
    /// in red, so a glance at who just took a hit never confuses attacker and defender.
    ///
    /// One component, attached once (code-driven, no scene wiring) by whichever owner builds itself —
    /// <see cref="MaxWorlds.Arena.Sentinel.Init"/> and <see cref="MaxWorlds.Player.PlayerHealth.Initialize"/> —
    /// and driven by a direct call from that owner's own <c>TakeDamage</c>, after any absorb, only on
    /// real HP loss. There is exactly one of these per owner, so unlike <see cref="RobotRig"/>'s
    /// position-radius gate on a shared signal (needed because a whole pack listens to one event),
    /// nothing here needs to disambiguate which instance a hit belongs to.
    ///
    /// Idle cost is zero: the component starts (and returns to) <c>enabled = false</c>, so
    /// <see cref="Update"/>/<see cref="LateUpdate"/> never run for a healthy, unhit owner — it only
    /// ticks while the hit flash is still decaying or the low-HP smoke is active.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("damagefx")]
    public sealed class DamageFeedbackVfx : MonoBehaviour
    {
        /// <summary>Hot red the body tints toward on a landed hit — distinct from the robots' white
        /// flash by design (friendly units flash red, enemies flash white). Its red channel is exactly
        /// 1.0 on purpose: the emission colour written to the property block is this colour scaled by
        /// <see cref="_flashStrength"/>, so the block's own red channel IS the resolved strength — an
        /// EditMode test can read it straight off the renderer with no reflection into private state.</summary>
        private static readonly Color HitTintColor = new Color(1.0f, 0.22f, 0.15f);

        /// <summary>How strongly the tint reads the instant a hit lands (MV-1005 spec).</summary>
        private const float HitFlashStrength = 0.65f;

        /// <summary>Seconds the flash takes to decay to 0 — 9 frames at 60 fps; 8 is the visibility
        /// floor (MV-1005 spec).</summary>
        private const float HitFlashDecaySeconds = 0.15f;

        /// <summary>Below this HP fraction the smoke wisp runs; at or above it, it stops.</summary>
        private const float LowHealthFraction = 0.35f;

        private const float SmokePuffIntervalSeconds = 0.4f;
        private const float SmokePuffLifetimeSeconds = 0.8f;
        private const float SmokePuffSize = 0.3f;
        private static readonly Color SmokeColor = new Color(0.12f, 0.11f, 0.10f, 0.6f);

        private const int SparkCount = 6;
        private const float SparkSize = 0.06f;
        private const float FlashDiscSize = 0.25f;
        private const float SparkMinSpeed = 3f;
        private const float SparkMaxSpeed = 5f;
        private const float SparkLifetimeSeconds = 0.25f;

        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        // Shared, pooled bursts — one set for every Sentinel/Max in the scene (MV-1005: "pooled, no
        // allocation per hit"), same idiom as CombatVfx's per-effect VfxBurst fields.
        private static VfxBurst s_sparks;
        private static VfxBurst s_flashDisc;
        private static VfxBurst s_smoke;

        private Renderer[] _bodyRenderers;
        private MaterialPropertyBlock _mpb;
        private Func<float> _healthNormalized;

        /// <summary>0..1, decaying to 0 — the multiplier baked into the emission colour this instance
        /// just wrote to every body renderer's property block.</summary>
        private float _flashStrength;

        private float _smokeTimer;

        /// <summary>Whether the low-HP smoke wisp is currently emitting — a resolved state, not an
        /// authored constant (MV-1005 AC1).</summary>
        public bool SmokeActive { get; private set; }

        /// <summary>
        /// Builds the shared pooled bursts (once, lazily) and caches this owner's renderers. Exposed
        /// publicly, and NOT relied on via Awake alone, because Awake never runs as a side effect of
        /// AddComponent outside Play mode — the same reason <see cref="MaxWorlds.Player.PlayerHealth.Initialize"/>
        /// is itself public. Callable more than once; every step here is idempotent.
        /// </summary>
        public void Initialize()
        {
            _bodyRenderers = GetComponentsInChildren<Renderer>(true);
            _mpb ??= new MaterialPropertyBlock();
            EnsureBursts();
            enabled = false; // idle until a hit lands or Init() finds HP already low
        }

        /// <summary>Wires the HP source this instance reads for the low-HP smoke gate — a Sentinel's
        /// <c>Normalized</c> or Max's <c>Normalized</c>. One component works for either without knowing
        /// which it is attached to.</summary>
        public void Init(Func<float> healthNormalized)
        {
            _healthNormalized = healthNormalized;
            RefreshSmokeState();
            if (SmokeActive) enabled = true;
        }

        private static void EnsureBursts()
        {
            if (s_sparks != null) return;
            Texture2D glow = VfxMaterials.Glow();
            Material additive = VfxMaterials.Additive(glow);
            Material soft = VfxMaterials.AlphaBlend(glow);
            s_sparks = new VfxBurst("DamageFeedbackSparks", additive, 120, 0f, perFrameCap: 6);
            s_flashDisc = new VfxBurst("DamageFeedbackFlashDisc", additive, 30, 0f, perFrameCap: 6);
            s_smoke = new VfxBurst("DamageFeedbackSmoke", soft, 80, -0.15f, perFrameCap: 2);
        }

        /// <summary>Call on every landed hit, after any absorb — <see cref="MaxWorlds.Arena.Sentinel.TakeDamage"/>
        /// and <see cref="MaxWorlds.Player.PlayerHealth.TakeDamage"/> only reach this on real HP loss
        /// (MV-1005).</summary>
        public void OnHit(Vector3 worldPoint)
        {
            _flashStrength = HitFlashStrength;
            ApplyFlash();

            s_sparks?.Emit(worldPoint, SparkCount,
                axis: Vector3.up, spreadDegrees: 180f,
                speedMin: SparkMinSpeed, speedMax: SparkMaxSpeed,
                sizeMin: SparkSize, sizeMax: SparkSize,
                lifeMin: SparkLifetimeSeconds, lifeMax: SparkLifetimeSeconds,
                colorA: HitTintColor, colorB: Color.white);

            s_flashDisc?.Emit(worldPoint, 1,
                axis: Vector3.up, spreadDegrees: 0f,
                speedMin: 0f, speedMax: 0f,
                sizeMin: FlashDiscSize, sizeMax: FlashDiscSize,
                lifeMin: SparkLifetimeSeconds, lifeMax: SparkLifetimeSeconds,
                colorA: HitTintColor, colorB: HitTintColor);

            RefreshSmokeState();
            enabled = true;
        }

        /// <summary>Re-checks the HP gate — called on every hit, and (while already ticking) every
        /// frame, so regen crossing back over the threshold turns the smoke off without another hit
        /// having to land.</summary>
        private void RefreshSmokeState()
        {
            float normalized = _healthNormalized != null ? _healthNormalized() : 1f;
            SmokeActive = normalized < LowHealthFraction;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The actual per-frame work, split out with an explicit <paramref name="dt"/> so an
        /// EditMode test can drive the decay deterministically without a live clock — same idiom as
        /// <see cref="RobotRig"/>'s reflected-LateUpdate tests.</summary>
        private void Tick(float dt)
        {
            if (_flashStrength > 0f)
            {
                _flashStrength = Mathf.Max(0f, _flashStrength - (HitFlashStrength / HitFlashDecaySeconds) * dt);
                ApplyFlash();
            }

            RefreshSmokeState();

            if (SmokeActive)
            {
                _smokeTimer -= dt;
                if (_smokeTimer <= 0f)
                {
                    _smokeTimer = SmokePuffIntervalSeconds;
                    s_smoke?.Emit(transform.position, 1,
                        axis: Vector3.up, spreadDegrees: 15f,
                        speedMin: 0.2f, speedMax: 0.4f,
                        sizeMin: SmokePuffSize, sizeMax: SmokePuffSize,
                        lifeMin: SmokePuffLifetimeSeconds, lifeMax: SmokePuffLifetimeSeconds,
                        colorA: SmokeColor, colorB: SmokeColor);
                }
            }
            else
            {
                _smokeTimer = 0f;
            }

            if (_flashStrength <= 0f && !SmokeActive) enabled = false; // idle again (MV-1005 cost rule)
        }

        private void ApplyFlash()
        {
            if (_bodyRenderers == null) return;
            Color emission = HitTintColor * _flashStrength;
            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                Renderer r = _bodyRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionId, emission);
                r.SetPropertyBlock(_mpb);
            }
        }

        /// <summary>Refills every shared burst's per-frame budget. Safe to call from more than one
        /// instance's LateUpdate in the same frame — by the time ANY LateUpdate runs, Unity has already
        /// finished every object's Update/FixedUpdate this frame, so every hit that could have emitted
        /// already has, and a redundant reset here only prepares next frame's budget early.</summary>
        private void LateUpdate()
        {
            s_sparks?.EndFrame();
            s_flashDisc?.EndFrame();
            s_smoke?.EndFrame();
        }
    }
}
