using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// MV-1018: Anchorhead, World 3's a30 final boss — today built as a plain reskinned
    /// <see cref="BigBermudaBoss"/> with no behaviour of its own. <see cref="MaxWorlds.Arena.MapRuntime.BuildBoss"/>
    /// still builds the boss AS a <see cref="BigBermudaBoss"/> (HP bar, <see cref="BossCensus"/>, victory,
    /// the finale orb/door all keep working through it, untouched) and adds this component alongside it
    /// only for the entity whose authored id is <c>"anchorhead"</c> — this is the ONLY thing that makes
    /// Anchorhead play differently.
    ///
    /// Two phases, keyed off the shared health this class only READS
    /// (<see cref="BigBermudaBoss.HealthFraction01"/>): dragging above 50% HP, awake below it. On its
    /// own cadence (6s dragging, 3.5s awake) it slams its anchor down (a floor telegraph,
    /// <see cref="TelegraphProgress01"/>, for the rig to ring), then hauls it back — pulling Max toward
    /// the boss if he's close enough. Nothing here is Big Bermuda's own attack: the brood volley and
    /// contact damage are still entirely <see cref="BigBermudaBoss"/>'s, unmodified.
    ///
    /// The design brief's own phase-2 line — "it uses Big Bermuda's existing charge attack in between" —
    /// describes a mechanic that no longer exists: MV-588 removed Big Bermuda's ram/charge entirely
    /// before this ticket landed. There is nothing left to reuse, and no acceptance criterion here tests
    /// it, so it is flagged in the MV-1018 hand-off comment rather than rebuilt from scratch.
    /// </summary>
    [RequireComponent(typeof(BigBermudaBoss))]
    [PerfSection("bosses")]
    public sealed class AnchorheadBoss : MonoBehaviour
    {
        public enum AnchorPhase { Dragging, Awake }

        private enum DragState { Idle, Telegraph, Pulling }

        private const float PhaseThreshold01 = 0.5f;

        private const float DraggingSlamInterval = 6f;
        private const float AwakeSlamInterval = 3.5f;
        private const float TelegraphSeconds = 1.0f;

        private const float PullRange = 10f;
        private const float PullDistance = 3f;
        private const float PullDuration = 0.8f;

        /// <summary>60% of Big Bermuda's own move speed while dragging, 110% once awake — stacked onto
        /// <see cref="BigBermudaBoss.ExternalSpeedScale"/>, which the base class multiplies into its
        /// own approach speed on top of its enrage scale.</summary>
        private const float DraggingSpeedScale = 0.6f;
        private const float AwakeSpeedScale = 1.1f;

        private BigBermudaBoss _boss;
        private Transform _target;
        private CharacterController _targetCc;

        private DragState _state = DragState.Idle;
        private float _timer;
        private Vector3 _pullDir;
        private float _pullMoved;

        /// <summary>Read-only fight state for the art layer (AnchorheadRig) and for a test, same
        /// "getter over state this class already owns, nothing outside can write to the fight" shape
        /// <see cref="BigBermudaBoss.Enraged"/>/<see cref="BigBermudaBoss.SpawnLevel"/> already use.
        /// Computed, not cached: reading it right after a damage application (the normal
        /// <see cref="IDamageable.TakeDamage"/> path) must see the new phase immediately, with no tick
        /// in between.</summary>
        public AnchorPhase Phase =>
            _boss != null && _boss.HealthFraction01 <= PhaseThreshold01 ? AnchorPhase.Awake : AnchorPhase.Dragging;

        /// <summary>0 shut .. 1 about to slam — the anchor telegraph, for the rig to ring the floor
        /// with. 0 outside the telegraph window; the drag itself is not a telegraph any more.</summary>
        public float TelegraphProgress01 =>
            _state == DragState.Telegraph ? 1f - Mathf.Clamp01(_timer / TelegraphSeconds) : 0f;

        /// <summary>True while Max is actually being hauled toward the boss — the rig's cue for the
        /// taut-chain beat, separate from the floor telegraph above.</summary>
        public bool IsPulling => _state == DragState.Pulling;

        private void Awake()
        {
            _boss = GetComponent<BigBermudaBoss>();
            _timer = DraggingSlamInterval;
            AcquireTarget();
        }

        private void Update()
        {
            if (_boss == null || !_boss.IsAlive) return;
            Tick(Time.deltaTime);
        }

        /// <summary>The actual step, dt-parameterized like <see cref="BigBermudaBoss.TickApproach"/> so
        /// an EditMode test can drive it directly against a synthetic target instead of needing a live
        /// Update loop.</summary>
        public void Tick(float dt)
        {
            if (_boss == null) return;
            _boss.ExternalSpeedScale = Phase == AnchorPhase.Awake ? AwakeSpeedScale : DraggingSpeedScale;

            if (_target == null) { AcquireTarget(); if (_target == null) return; }

            switch (_state)
            {
                case DragState.Idle:
                    _timer -= dt;
                    if (_timer <= 0f) { _state = DragState.Telegraph; _timer = TelegraphSeconds; }
                    break;

                case DragState.Telegraph:
                    _timer -= dt;
                    if (_timer <= 0f) BeginPullOrSkip();
                    break;

                case DragState.Pulling:
                    TickPull(dt);
                    break;
            }
        }

        /// <summary>The telegraph resolved: haul the anchor back if Max is within <see cref="PullRange"/>
        /// of where the boss stood when it slammed, otherwise there is nothing on the chain and the
        /// cycle simply resets for the next slam.</summary>
        private void BeginPullOrSkip()
        {
            Vector3 toBoss = transform.position - _target.position;
            toBoss.y = 0f;
            float dist = toBoss.magnitude;

            if (dist > PullRange || dist < 0.0001f)
            {
                ResetForNextSlam();
                return;
            }

            _pullDir = toBoss.normalized;
            _pullMoved = 0f;
            _state = DragState.Pulling;
        }

        /// <summary>Hauls Max <see cref="PullDistance"/> toward the boss over <see cref="PullDuration"/>,
        /// along the direction fixed the instant the drag began — a knock-TOWARD, not damage. Goes
        /// through <see cref="CharacterControllerMotion.SafeMove"/> when Max carries a real
        /// CharacterController (the normal in-game case); falls back to a raw transform move for a
        /// bare test double that carries none.</summary>
        private void TickPull(float dt)
        {
            float speed = PullDistance / PullDuration;
            float step = Mathf.Min(speed * dt, PullDistance - _pullMoved);
            if (step > 0f)
            {
                Vector3 displacement = _pullDir * step;
                if (_targetCc != null) CharacterControllerMotion.SafeMove(_targetCc, displacement);
                else _target.position += displacement;
                _pullMoved += step;
            }

            if (_pullMoved >= PullDistance - 1e-4f) ResetForNextSlam();
        }

        private void ResetForNextSlam()
        {
            _state = DragState.Idle;
            _timer = Phase == AnchorPhase.Awake ? AwakeSlamInterval : DraggingSlamInterval;
        }

        private void AcquireTarget()
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return;
            _target = p.transform;
            _targetCc = p.GetComponent<CharacterController>();
        }
    }
}
