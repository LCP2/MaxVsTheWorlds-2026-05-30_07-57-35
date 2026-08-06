using System;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.UI;

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
    public sealed class AreaGate : MonoBehaviour, IDamageable, IHealthReadout
    {
        // Assumed sustained primary DPS used to size gate HP from gateBreakSeconds — the primary's own
        // authored base tick rate (damagePerTick 4 / fireInterval 0.1 s = 40 dps, WaterBlaster.cs). A
        // fixed reference number rather than one read off a live WaterBlaster instance: an area gate
        // can exist in a map with no player in it at all (the map editor, a test), and the promise is
        // about FOCUSED fire at the weapon's base rate, not whatever upgrades a given run happens to
        // carry.
        public const float AssumedPrimaryDps = 40f;

        // --- Health bar (MV-265): the gate is deliberately narrower than its own body — like the
        // Mower Hutch's bar (BarWorldWidth vs. a 3 m body), it's a readout of the damage you're
        // doing, not a piece of architecture. Fixed rather than derived from the gate's own (very
        // variable, 1-11 m) width, so a wide boss gate doesn't drown the room in bar.
        private const float BarWorldWidth = 1.8f;
        // Clearance above the gate's own top edge, in metres — same margin MowerHutch gives its bar
        // (BarHeightAboveCentre - halfHeight = 0.7) so the two structure bars read consistently.
        private const float BarHeightClearance = 0.7f;

        // --- Hinge-open animation (MV-265): a destroyed gate swings on one vertical edge like a
        // door, the visual half of "the gate is passable" (the collider drops the instant it dies —
        // see Open() — same split as SubZoneGate's sink). Past 90 degrees so the slab reads as
        // deliberately flung open rather than merely ajar.
        private const float HingeSwingDegrees = 100f;
        private const float HingeDuration = 0.5f;

        private DestructibleHealth _health;
        private Collider _collider;

        private bool _hinging;
        private float _hingeT;
        private Vector3 _hingePivot;
        private Vector3 _closedPosition;
        private Quaternion _closedRotation;

        public bool IsAlive => _health != null && _health.IsAlive;
        public Team Team => Team.Enemy; // the primary (Team.Player) can damage it; robots can't
        public float Normalized => _health?.Normalized ?? 0f;

        // --- IHealthReadout (YT-111): what the gate's floating bar reads. ---
        public float HealthNormalized => Normalized;
        public float HealthCurrent => _health?.Current ?? 0f;
        public string ReadoutName => "GATE";

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

            // Always shown, not earned by a hit (unlike a robot's) — the player needs to discover
            // the gate is a breakable target in the first place, not just watch it deplete once they
            // already found it (MV-265: Lee couldn't tell the gate was damageable at all).
            float halfHeight = transform.localScale.y * 0.5f;
            WorldHealthBar.Attach(gameObject, this, halfHeight + BarHeightClearance, BarWorldWidth,
                                  alwaysShow: true);
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

            // Passable immediately — the hinge swing that follows is theatre, same split as
            // SubZoneGate.Open vs. its Update-driven sink. Collision drops on this exact frame so
            // "destroyed" and "walkable" are never out of sync, whatever the swing is doing visually.
            if (_collider != null) _collider.enabled = false;

            StartHingeSwing();

            Opened?.Invoke();
        }

        /// <summary>Begin swinging the gate open on its left edge (local -X), the same edge for every
        /// gate since none of this map's links are ever authored rotated (§1: one straight chain of
        /// rooms). Reads the CURRENT transform as "closed" rather than caching it in Awake — Update
        /// re-derives the swing from this baseline every frame (never cumulative), so there is no
        /// drift to accumulate no matter how long the gate sits open.</summary>
        private void StartHingeSwing()
        {
            _closedPosition = transform.position;
            _closedRotation = transform.rotation;

            float halfWidth = transform.localScale.x * 0.5f;
            _hingePivot = _closedPosition - transform.right * halfWidth;

            _hingeT = 0f;
            _hinging = true;
        }

        private void Update()
        {
            if (!_hinging) return;

            _hingeT += Time.deltaTime;
            float k = HingeDuration > 0f ? Mathf.Clamp01(_hingeT / HingeDuration) : 1f;
            // Ease out — fast off the latch, settling into the open position, so the swing reads as
            // a snap rather than a slow architectural drift.
            float eased = 1f - (1f - k) * (1f - k);
            float angle = eased * HingeSwingDegrees;

            transform.position = _closedPosition;
            transform.rotation = _closedRotation;
            transform.RotateAround(_hingePivot, Vector3.up, angle);

            if (k >= 1f) _hinging = false;
        }
    }
}
