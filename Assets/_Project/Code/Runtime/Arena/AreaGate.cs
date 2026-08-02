using System;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// A gated room boundary with its own HP, broken by sustained PRIMARY-weapon fire (v0.5 recut spec
    /// §1, WV-222). Unlike the scene-adopted <see cref="SubZoneGate"/> — which opens on a FACTORY's
    /// death and never takes damage itself — an area gate is a structure in its own right, the same
    /// shape of thing a <see cref="MowerHutch"/> is: it owns a <see cref="DestructibleHealth"/> and
    /// opens on ITS OWN destruction.
    ///
    /// Only <see cref="DamageSource.PrimaryWeapon"/> chips it — a Water Balloon or a future ability
    /// cannot skip the gate's pacing beat. Max HP is derived from <c>gateBreakSeconds</c> so continuous
    /// primary fire empties it in ~that many seconds (spec default 4 s); moving the Settings panel's
    /// "Gate break secs" slider changes how long a FRESHLY BUILT gate takes to break, not just a
    /// description of one (matches <c>gateBreakSeconds</c>/<c>gateRequiresClear</c> being read here,
    /// not just stored — WV-234 landed the settings, this ticket is what spends them).
    /// </summary>
    public sealed class AreaGate : MonoBehaviour, IDamageable
    {
        // Assumed sustained primary DPS used to size gate HP from gateBreakSeconds — the primary's own
        // authored base tick rate (damagePerTick 4 / fireInterval 0.1 s = 40 dps, WaterBlaster.cs). A
        // fixed reference number rather than one read off a live WaterBlaster instance: an area gate
        // can exist in a map with no player in it at all (the map editor, a test), and the promise is
        // about FOCUSED fire at the weapon's base rate, not whatever upgrades a given run happens to
        // carry.
        public const float AssumedPrimaryDps = 40f;

        private DestructibleHealth _health;
        private Collider _collider;

        public bool IsAlive => _health != null && _health.IsAlive;
        public Team Team => Team.Enemy; // the primary (Team.Player) can damage it; robots can't
        public float Normalized => _health?.Normalized ?? 0f;

        /// <summary>The gate's current HP ceiling — <c>gateBreakSeconds</c> (at build time) times
        /// <see cref="AssumedPrimaryDps"/>. Exposed so a test (or a future HUD bar) can read it without
        /// reverse-engineering the constant.</summary>
        public float MaxHp => _health?.Max ?? 0f;

        /// <summary>The way is open — Max can walk (and shoot) through. True the instant HP hits zero.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Whether this gate refuses primary damage until its room reads clear of robots
        /// (<c>gateRequiresClear</c>). Robot/room integration is WV-223's, not this ticket's — until
        /// something wires <see cref="RoomClear"/>, a gate with this on is treated as already clear
        /// (there is nothing to be blocked on), so the mechanic stays fully testable in isolation.</summary>
        public bool RequiresClear { get; private set; }

        /// <summary>Reports whether this gate's room is clear of robots. Left null (always "clear")
        /// until a robot-room system (WV-223) has something real to wire in.</summary>
        public Func<bool> RoomClear;

        /// <summary>Fired once, the instant the gate opens.</summary>
        public event Action Opened;

        private void Awake()
        {
            float breakSeconds = Mathf.Max(0.1f,
                DevTuning.Or(DevTuning.GateBreakSeconds, ArenaTuning.DefaultGateBreakSeconds));
            _health = new DestructibleHealth(breakSeconds * AssumedPrimaryDps);
            _health.Destroyed += Open;

            RequiresClear =
                DevTuning.Or(DevTuning.GateRequiresClear, ArenaTuning.DefaultGateRequiresClear) >= 0.5f;

            _collider = GetComponent<Collider>();
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return;
            if (info.Source != DamageSource.PrimaryWeapon) return; // only sustained primary fire counts
            if (RequiresClear && RoomClear != null && !RoomClear()) return;

            _health.TakeDamage(info.Amount);
        }

        private void Open()
        {
            if (IsOpen) return;
            IsOpen = true;

            // Passable (and see-through) immediately — the sinking/wreck theatre, if any, is a later
            // ticket's art pass, same split as SubZoneGate.Open vs. its Update-driven sink.
            if (_collider != null) _collider.enabled = false;

            Opened?.Invoke();
        }
    }
}
