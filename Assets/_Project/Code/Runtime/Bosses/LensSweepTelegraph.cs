namespace MaxWorlds.Bosses
{
    /// <summary>
    /// The Helmsman's lens-sweep attack (MV-1239): a gold arc drawn on the floor as a tell, then the
    /// same arc goes live for one damage pass, then cools down before repeating. Pure state, no
    /// MonoBehaviour — same "no scene needed to unit-test it" reasoning as <see cref="BigBermudaBrain"/>,
    /// so a test can drive it directly rather than through a live Update loop.
    /// </summary>
    public sealed class LensSweepTelegraph
    {
        private enum State { Cooldown, Tell, Active }

        /// <summary>How long the gold arc is drawn and visible before it goes live — the player's own
        /// warning window.</summary>
        public const float TellSeconds = 0.8f;

        /// <summary>How long the arc stays live once the tell ends — must stay longer than
        /// <see cref="TellSeconds"/>, or the "warning" would outlast the thing it is warning about.</summary>
        public const float ActiveSeconds = 1.2f;

        public const float ArcDegrees = 90f;
        public const float Damage = 25f;

        private float _cooldownSeconds;
        private State _state;
        private float _timer;

        public LensSweepTelegraph(float cooldownSeconds)
        {
            _cooldownSeconds = cooldownSeconds;
            _state = State.Cooldown;
            _timer = cooldownSeconds;
        }

        /// <summary>The floor arc is drawn and visible right now — the rig's cue to render it.</summary>
        public bool IsTelling => _state == State.Tell;

        /// <summary>The arc is live right now — the rig's cue to flash it as dangerous.</summary>
        public bool IsActive => _state == State.Active;

        /// <summary>True for exactly the tick the arc goes live — the "25 damage once" edge a caller
        /// should apply its hit-detection on.</summary>
        public bool JustActivated { get; private set; }

        /// <summary>The cooldown to use for the NEXT cycle (phase 1 vs phase 2 cadence differ) — applied
        /// only once the current cycle actually reaches <see cref="State.Cooldown"/> again, never
        /// rewinding a cycle already under way.</summary>
        public void SetCooldownSeconds(float seconds) => _cooldownSeconds = seconds;

        public void Tick(float dt)
        {
            JustActivated = false;
            _timer -= dt;
            if (_timer > 0f) return;

            switch (_state)
            {
                case State.Cooldown:
                    _state = State.Tell;
                    _timer = TellSeconds;
                    break;
                case State.Tell:
                    _state = State.Active;
                    _timer = ActiveSeconds;
                    JustActivated = true;
                    break;
                case State.Active:
                    _state = State.Cooldown;
                    _timer = _cooldownSeconds;
                    break;
            }
        }
    }
}
