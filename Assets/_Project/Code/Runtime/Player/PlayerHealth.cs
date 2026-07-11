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
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerHealth : MonoBehaviour, IDamageable
    {
        [SerializeField] private float maxHealth = 100f;

        private PlayerController _controller;
        private float _health;

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
            Changed?.Invoke(_health);
        }
    }
}
