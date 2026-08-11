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

        /// <summary>Refuses ALL damage regardless of source while true — the boss gate's
        /// <c>opensWith: all-sheds-destroyed</c> lock (World &amp; Difficulty Framework, MV-270). Unlike
        /// <see cref="RequiresClear"/>, which only pauses a normal gate until its room clears, a locked
        /// gate cannot be chipped down by fire at all; only <see cref="ForceOpen"/> opens it, once
        /// whatever external condition it is waiting on (here, <c>SupplyLineNetwork.AllShedsDestroyed</c>)
        /// is met. Every other gate leaves this false and behaves exactly as before.</summary>
        public bool Locked { get; set; }

        /// <summary>World-space direction from the room the player approaches this gate from toward
        /// the room beyond it (MV-320) — set by <see cref="MapRuntime"/> from the link's from/to zone
        /// centres, since a gate itself has no notion of "which side Max is standing on". Left
        /// <see cref="Vector3.zero"/> for a gate built without map context (e.g. a bare EditMode/
        /// PlayMode test fixture), in which case <see cref="SwingSign"/> keeps the old fixed swing.</summary>
        public Vector3 AwayFromPlayerDirection { get; set; }

        // Sign applied to the hinge angle each swing — captured once in StartHingeSwing rather than
        // recomputed every frame in Update, since transform.forward there is mid-swing and no longer
        // reads as "closed".
        private float _hingeSign = 1f;

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
            if (!IsAlive || Locked) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return;
            if (info.Source != DamageSource.PrimaryWeapon) return; // only sustained primary fire counts
            if (RequiresClear && RoomClear != null && !RoomClear()) return;

            _health.TakeDamage(info.Amount);
        }

        /// <summary>Opens the gate immediately regardless of remaining HP or <see cref="Locked"/> — how
        /// an <c>opensWith</c> condition other than sustained primary fire (currently just
        /// <c>all-sheds-destroyed</c>) actually resolves (MV-270). Goes through the same
        /// <see cref="DestructibleHealth.Destroyed"/> → <see cref="Open"/> path a normal break does, so
        /// the hinge swing and every other break-time effect fire exactly as they would from fire.</summary>
        public void ForceOpen()
        {
            if (!IsAlive) return;
            _health.TakeDamage(_health.Max);
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

        /// <summary>Begin swinging the gate open on its left edge (local -X) — the pivot side never
        /// changes, only <see cref="SwingSign"/> (which room the free edge sweeps toward) does. Reads
        /// the CURRENT transform as "closed" rather than caching it in Awake — Update re-derives the
        /// swing from this baseline every frame (never cumulative), so there is no drift to accumulate
        /// no matter how long the gate sits open.</summary>
        private void StartHingeSwing()
        {
            _closedPosition = transform.position;
            _closedRotation = transform.rotation;

            float halfWidth = transform.localScale.x * 0.5f;
            _hingePivot = _closedPosition - transform.right * halfWidth;

            _hingeSign = SwingSign(AwayFromPlayerDirection, transform.forward);

            _hingeT = 0f;
            _hinging = true;
        }

        /// <summary>+1 or -1 for the hinge angle in <see cref="Update"/>: a positive-angle
        /// <c>RotateAround(pivot, Vector3.up, angle)</c> always sweeps the free edge toward
        /// <c>-forward</c> (Unity's Y-rotation turns +right into -forward), so whenever the room beyond
        /// the gate sits on the <c>+forward</c> side the sign must flip to -1, or the door swings back
        /// into the room the player is standing in instead of the one ahead (MV-320). <c>awayFromPlayer
        /// == Vector3.zero</c> means no map context was wired in, so this keeps the untouched +1 every
        /// gate used before this ticket.</summary>
        public static float SwingSign(Vector3 awayFromPlayer, Vector3 forward)
        {
            if (awayFromPlayer == Vector3.zero) return 1f;
            return Vector3.Dot(awayFromPlayer, forward) > 0f ? -1f : 1f;
        }

        private void Update()
        {
            if (!_hinging) return;

            _hingeT += Time.deltaTime;
            float k = HingeDuration > 0f ? Mathf.Clamp01(_hingeT / HingeDuration) : 1f;
            // Ease out — fast off the latch, settling into the open position, so the swing reads as
            // a snap rather than a slow architectural drift.
            float eased = 1f - (1f - k) * (1f - k);
            float angle = _hingeSign * eased * HingeSwingDegrees;

            transform.position = _closedPosition;
            transform.rotation = _closedRotation;
            transform.RotateAround(_hingePivot, Vector3.up, angle);

            if (k >= 1f) _hinging = false;
        }
    }
}
