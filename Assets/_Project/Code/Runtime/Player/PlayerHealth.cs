using System;
using UnityEngine;
using MaxWorlds.Core;

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
    public sealed class PlayerHealth : MonoBehaviour, IDamageable
    {
        [SerializeField] private float maxHealth = 100f;

        // Regen is authored in PlayerTuning, NOT as [SerializeField]s — a serialized field on a
        // component that lives in Backyard_Slice.unity gets baked into the scene, and the scene then
        // silently outranks the code (YT-80).

        private PlayerController _controller;
        private float _health;
        private float _timeSinceDamage;

        public bool IsAlive => _health > 0f;
        public Team Team => Team.Player;
        public float Max => maxHealth;
        public float Current => _health;
        public float Normalized => maxHealth > 0f ? _health / maxHealth : 0f;

        /// <summary>Fired when HP changes (HUD subscribes). Arg = current HP.</summary>
        public event Action<float> Changed;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _health = maxHealth;
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
            _health = Regenerate(_health, maxHealth, _timeSinceDamage,
                                 PlayerTuning.RegenDelay, PlayerTuning.RegenPerSec, dt);
            if (!Mathf.Approximately(before, _health)) Changed?.Invoke(_health);
        }
    }
}
