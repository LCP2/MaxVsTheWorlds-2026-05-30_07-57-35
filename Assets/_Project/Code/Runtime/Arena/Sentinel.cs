using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Arena
{
    /// <summary>Which of Max's deployable structures this is (MV-362).</summary>
    public enum SentinelKind { Wall, Gunner }

    /// <summary>
    /// Base for Max's deployable Sentinels (MV-362): a Wall (Blocker) or a Gunner (Attack turret),
    /// placed at Max's own position, with their own HP. Deployed sentinels are permanent until
    /// destroyed — no repair, no recall (DECISION, Lee 15 Aug 2026) — so unlike
    /// <see cref="MaxWorlds.Enemies.RobotEnemy"/> they are never pooled; <see cref="Die"/> destroys
    /// the GameObject outright, the same one-shot lifecycle <see cref="MaxWorlds.Factories.MowerHutch"/>
    /// uses for its own death.
    ///
    /// <see cref="Team"/> is <see cref="Team.Player"/> — Max's own device. <see cref="DamageRules"/>'s
    /// same-team rejection means a robot (Team.Enemy) CAN hit it, and Max's own primary (Team.Player)
    /// CANNOT — <c>WaterBlaster.FireTick</c> already skips every <c>Team.Player</c> receiver, so
    /// nothing extra is needed to stop Max from shooting his own wall.
    /// </summary>
    public abstract class Sentinel : MonoBehaviour, IDamageable, IHealthReadout
    {
        private static readonly List<Sentinel> _active = new List<Sentinel>(8);

        /// <summary>Every sentinel deployed right now, across both kinds — what
        /// <see cref="MaxWorlds.Enemies.RobotEnemy"/>'s retargeting reads to find the nearest one, and
        /// what <see cref="MaxWorlds.Weapons.PlayerAbilities"/> counts against the shared Deployment
        /// Count cap (DECISION: "shared across both types").</summary>
        public static IReadOnlyList<Sentinel> Active => _active;

        /// <summary>Empties the registry ONLY — mirrors <see cref="MaxWorlds.Enemies.RobotEnemy.ResetRegistry"/>'s
        /// list-only contract for test isolation. Does not destroy any GameObject; see
        /// <see cref="DestroyAllActive"/> for the real teardown a fresh level or a gate crossing needs.</summary>
        public static void ResetRegistry() => _active.Clear();

        /// <summary>Destroys every deployed sentinel and empties the registry. Sentinels aren't
        /// pooled (unlike robots), so a full reset has to tear the GameObjects down too, not just
        /// forget them. Two call sites: <see cref="MaxWorlds.Arena.Map.MapRuntime"/> on a fresh level
        /// build, and every <see cref="AreaGate.Opened"/> (MV-362 spec: "they do not travel between
        /// areas... passing a gate clears them and refunds the slots") — the "refund" is automatic
        /// here, since the Deployment Count cap is always checked live against
        /// <see cref="Active"/>.Count, never a separately-tracked balance.</summary>
        public static void DestroyAllActive()
        {
            if (_active.Count == 0) return;
            var snapshot = new List<Sentinel>(_active);
            _active.Clear();
            foreach (Sentinel s in snapshot)
            {
                if (s == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(s.gameObject);
                else UnityEngine.Object.DestroyImmediate(s.gameObject);
            }
        }

        public abstract SentinelKind Kind { get; }

        private DestructibleHealth _health;

        public bool IsAlive => _health != null && _health.IsAlive;

        public Team Team => Team.Player;

        public float Normalized => _health?.Normalized ?? 0f;
        public float HealthNormalized => Normalized;
        public float HealthCurrent => _health?.Current ?? 0f;
        public abstract string ReadoutName { get; }

        /// <summary>Fired once, the instant this sentinel is destroyed.</summary>
        public event Action<Sentinel> Died;

        protected void InitHealth(float maxHp)
        {
            _health = new DestructibleHealth(maxHp);
            _health.Destroyed += Die;

            // Registered here, not left to OnEnable alone: a sentinel is only "deployed" once Init
            // has actually run, and this guarantees the registry sees it the instant deployment
            // completes regardless of Unity's own OnEnable timing for a freshly-scripted GameObject
            // (belt-and-braces — OnEnable below still covers the ordinary enable/disable case).
            if (!_active.Contains(this)) _active.Add(this);
        }

        protected virtual void OnEnable()
        {
            if (!_active.Contains(this)) _active.Add(this);
        }

        protected virtual void OnDisable() => _active.Remove(this);

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return;
            _health.TakeDamage(info.Amount);
        }

        private void Die()
        {
            Died?.Invoke(this);
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }
    }
}
