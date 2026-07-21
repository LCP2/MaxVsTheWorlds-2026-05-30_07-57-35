using System;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.UI;

namespace MaxWorlds.Player
{
    /// <summary>
    /// Minimal player damage receiver (slice). Implements <see cref="IDamageable"/>
    /// so enemy contact damage (YT-36) has a target, and ignores hits while the
    /// <see cref="PlayerController"/> dash i-frames are active — which is what makes
    /// the dash actually dodge a contact hit. HP binds to the HUD (YT-30) via
    /// <see cref="Normalized"/> + <see cref="Changed"/>.
    ///
    /// Since YT-80 it also trickles back up out of combat (<see cref="Regenerate"/>). Health only
    /// ever fell before, so a scrape in the first factory was carried to the boss, and the run was
    /// effectively decided minutes before it ended. The trickle pays out breathing room you had to
    /// earn by disengaging — and it is far too slow to stand in a pack and out-heal it.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerHealth : MonoBehaviour, IDamageable, IHealthReadout
    {
        [SerializeField] private float maxHealth = 69.82f;   // YT-106: Lee's on-device number (was 100)

        // Regen is authored in PlayerTuning, NOT as [SerializeField]s — a serialized field on a
        // component that lives in Backyard_Slice.unity gets baked into the scene, and the scene then
        // silently outranks the code (YT-80).

        private PlayerController _controller;
        private float _health;
        private float _timeSinceDamage;

        public bool IsAlive => _health > 0f;
        public Team Team => Team.Player;

        /// <summary>Effective max HP — the dev tuning panel may be overriding it this session
        /// (YT-105). Everything downstream (regen ceiling, the HUD bar) reads through here, so a
        /// slider move is felt everywhere at once.</summary>
        public float Max => DevTuning.Or(DevTuning.PlayerMaxHealth, maxHealth);

        /// <summary>The authored max, ignoring any dev override — the panel's 100% reference.</summary>
        public float AuthoredMax => maxHealth;

        public float Current => _health;
        public float Normalized => Max > 0f ? _health / Max : 0f;

        /// <summary>
        /// Re-settle current HP against a max that just changed underneath it (YT-105). Raising the
        /// ceiling leaves Max where he stood — you get headroom, not a free heal — while lowering it
        /// has to clamp or the bar would read over 100%. Either way the HUD is told, because it
        /// binds <see cref="Changed"/> and would otherwise keep drawing the old fraction.
        /// </summary>
        public void RefreshMax()
        {
            _health = Mathf.Min(_health, Max);
            Changed?.Invoke(_health);
        }

        /// <summary>Fired when HP changes (HUD subscribes). Arg = current HP.</summary>
        public event Action<float> Changed;

        // --- IHealthReadout (YT-111): what the floating bar over Max reads. ---
        public float HealthNormalized => Normalized;
        public float HealthCurrent => _health;
        public string ReadoutName => "MAX";

        /// <summary>Metres above Max's origin his stack floats. His capsule is 2 m tall with its
        /// origin at the centre, so his head is at +1.0 and this clears it.</summary>
        private const float BarHeight = 1.55f;
        private const float BarWidth = 1.5f;
        private static readonly Color WaterColor = new Color(0.20f, 0.62f, 0.92f); // #33A0EB

        private MaxWorlds.Combat.WaterBlaster _blaster;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _health = Max;

            // Max's whole status now lives over his head (YT-121): the water gauge stacked directly
            // above his life bar, and no top-of-screen HUD. Always shown, unlike a robot's — you
            // should be able to find your own health without waiting to be hit.
            WorldHealthBar.Attach(gameObject, this, BarHeight, BarWidth, alwaysShow: true,
                                  secondary: WaterNormalized, secondaryColor: WaterColor);
        }

        /// <summary>Max's blaster tank, 0..1, for the floating water gauge. Resolved lazily and
        /// cached — the blaster attaches itself to Max and may not exist on the frame this runs.</summary>
        private float WaterNormalized()
        {
            if (_blaster == null) _blaster = GetComponent<MaxWorlds.Combat.WaterBlaster>();
            return _blaster != null && _blaster.Energy != null ? _blaster.Energy.Normalized : 1f;
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (DevMode.IsInvincible) return;                      // dev/filming only; off by default (YT-60)
            if (!DamageRules.Applies(info.Attacker, Team)) return; // no friendly fire
            if (_controller != null && _controller.IsInvulnerable) return; // dash dodge
            _health = Mathf.Max(0f, _health - info.Amount);
            // Only a hit that LANDS stalls the regen. A hit dashed through costs Max nothing —
            // neither health nor his recovery — which is the reward a clean dodge should carry.
            _timeSinceDamage = 0f;
            Changed?.Invoke(_health);
        }

        /// <summary>
        /// HP after <paramref name="dt"/> seconds of regen (YT-80). Pure, so the trickle can be
        /// tested without a scene or a clock, per the house rule.
        ///
        /// Never revives a corpse and never overfills: at 0 HP the run is over, and Max is dead
        /// before this is ever reached.
        /// </summary>
        public static float Regenerate(float current, float max, float timeSinceDamage,
                                       float delay, float perSec, float dt)
        {
            if (current <= 0f || current >= max) return current;
            if (timeSinceDamage < delay) return current;
            return Mathf.Min(max, current + Mathf.Max(0f, perSec) * Mathf.Max(0f, dt));
        }

        private void Update()
        {
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            _timeSinceDamage += dt;

            float before = _health;
            _health = Regenerate(_health, Max, _timeSinceDamage,
                                 PlayerTuning.RegenDelay, PlayerTuning.RegenPerSec, dt);
            if (!Mathf.Approximately(before, _health)) Changed?.Invoke(_health);
        }
    }
}
