namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-1162 — the idle-wave's clock, pulled out as a pure, scene-free class so the 20 s rule can be
    /// proven without a live <see cref="MaxRig"/> or a frame of Unity time (Tier 2 per MV-465). It
    /// knows nothing about transforms, cameras or AnimSequences — only "how long since anything
    /// happened" and "is a wave currently playing".
    ///
    /// <see cref="MaxRig"/> owns the actual gesture (its own fresh AnimSequence per trigger); this
    /// class only tells it WHEN to start one and WHEN input has interrupted it.
    /// </summary>
    public sealed class IdleWaveTracker
    {
        /// <summary>Lee, 10 Oct: "on an interval of about 20 seconds".</summary>
        public const float TriggerSeconds = 20f;

        private float _idleSeconds;
        private bool _waving;

        /// <summary>True from the tick <see cref="Tick"/> first returns true until either input cancels
        /// it (<see cref="Tick"/> again with <c>hasInput</c>) or the caller reports the gesture finished
        /// on its own via <see cref="EndWave"/>.</summary>
        public bool IsWaving => _waving;

        /// <summary>Seconds of continuous idle accumulated since the last reset — what a test reads to
        /// prove the clock itself, without reaching into a private field.</summary>
        public float IdleSeconds => _idleSeconds;

        /// <summary>
        /// Advance the idle clock by <paramref name="dt"/>. <paramref name="hasInput"/> is any of the
        /// ticket's reset triggers (move/aim stick, a shot, damage, death/respawn) already resolved by
        /// the caller into one bool. Returns true exactly on the tick a fresh wave should start.
        ///
        /// Input always wins over a running wave: this is the "any input instantly cancels the wave"
        /// rule, resolved here as the state change the caller reads via <see cref="IsWaving"/> going
        /// false on this same call.
        /// </summary>
        public bool Tick(float dt, bool hasInput)
        {
            if (hasInput)
            {
                _idleSeconds = 0f;
                _waving = false;
                return false;
            }

            if (_waving) return false; // already playing — MaxRig ends it via EndWave() when it finishes

            _idleSeconds += dt;
            if (_idleSeconds < TriggerSeconds) return false;

            _idleSeconds = 0f;
            _waving = true;
            return true;
        }

        /// <summary>The wave finished playing on its own (not by input) — called once the caller's own
        /// AnimSequence reports <c>IsComplete</c>. Lets the idle clock start counting fresh toward the
        /// next repeat while idle, per the ticket's "the clock restarts from 0, so it repeats every 20 s
        /// while he stays idle".</summary>
        public void EndWave() => _waving = false;
    }
}
