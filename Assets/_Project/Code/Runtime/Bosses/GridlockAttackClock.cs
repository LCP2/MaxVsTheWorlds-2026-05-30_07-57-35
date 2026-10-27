using UnityEngine;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// Pure cadence ticker for GRIDLOCK's three attacks and her escalation clock (MV-1306) — same
    /// "no MonoBehaviour, unit-testable without a scene" shape as <see cref="BigBermudaBrain"/>/
    /// <see cref="BroodVolley"/>. Owns timing ONLY: each attack's warning-then-fire edge, and the
    /// spawn level. <see cref="GridlockBoss"/> reads the edges and does the actual spawning,
    /// teleporting and damage.
    /// </summary>
    public sealed class GridlockAttackClock
    {
        private enum AttackState { Idle, Warning }

        private AttackState _squadState, _jumpState;
        private float _squadTimer, _jumpTimer, _strikeTimer, _spawnLevelTimer;

        private float _squadIntervalLive = GridlockTuning.SquadInterval;
        private float _jumpIntervalLive = GridlockTuning.JumpInterval;
        private float _strikeIntervalLive = GridlockTuning.StrikeInterval;

        public bool SquadWarningActive => _squadState == AttackState.Warning;
        public bool SquadJustFired { get; private set; }
        public float SquadProgress01 => Mathf.Clamp01(_squadTimer / Mathf.Max(0.01f, _squadIntervalLive));

        public bool JumpWarningActive => _jumpState == AttackState.Warning;
        public bool JumpJustFired { get; private set; }
        public float JumpProgress01 => Mathf.Clamp01(_jumpTimer / Mathf.Max(0.01f, _jumpIntervalLive));

        public bool StrikeJustFired { get; private set; }
        public float StrikeProgress01 => Mathf.Clamp01(_strikeTimer / Mathf.Max(0.01f, _strikeIntervalLive));

        /// <summary>1..<see cref="GridlockTuning.MaxEscalationLevel"/>, same "SpawnLevel" naming
        /// <see cref="BigBermudaBrain"/> already uses (shared as-is by <c>SludgequeenBoss</c> and
        /// <c>HelmsmanBoss</c>) for <see cref="BossCensus.ReportSpawnLevel"/>.</summary>
        public int SpawnLevel { get; private set; } = 1;
        public float SpawnLevelProgress01 { get; private set; }

        /// <summary>0..1 toward whichever attack is closest to firing — what the roof dial's clock
        /// ring fills toward (the ticket's own "the dial's clock ring fills toward it").</summary>
        public float NextAttackProgress01 => Mathf.Max(SquadProgress01, Mathf.Max(JumpProgress01, StrikeProgress01));

        /// <summary>Advance every timer one tick. <paramref name="halfHealth"/> is the ticket's own
        /// "at half health, her lights run twice as fast and every timer above is 30% shorter" —
        /// applied to every cadence here except JUMP's, which carries its own explicit half-health
        /// number instead of the generic cut (see <see cref="TickJump"/>).</summary>
        public void Tick(float dt, bool halfHealth)
        {
            dt = Mathf.Max(0f, dt);
            SquadJustFired = false;
            JumpJustFired = false;
            StrikeJustFired = false;

            float scale = halfHealth ? GridlockTuning.HalfHealthTimerScale : 1f;
            TickSquad(dt, scale);
            TickJump(dt, halfHealth, scale);
            TickStrike(dt, scale);
            TickSpawnLevel(dt, scale);
        }

        private void TickSquad(float dt, float scale)
        {
            _squadIntervalLive = GridlockTuning.SquadInterval * scale;
            float warning = GridlockTuning.SquadWarning * scale;
            float warnAt = Mathf.Max(0f, _squadIntervalLive - warning);

            _squadTimer += dt;
            if (_squadState == AttackState.Idle && _squadTimer >= warnAt) _squadState = AttackState.Warning;

            if (_squadTimer >= _squadIntervalLive)
            {
                SquadJustFired = true;
                _squadState = AttackState.Idle;
                _squadTimer = 0f;
            }
        }

        private void TickJump(float dt, bool halfHealth, float scale)
        {
            _jumpIntervalLive = halfHealth ? GridlockTuning.JumpIntervalHalfHealth : GridlockTuning.JumpInterval;
            float warning = GridlockTuning.JumpWarning * scale;
            float warnAt = Mathf.Max(0f, _jumpIntervalLive - warning);

            _jumpTimer += dt;
            if (_jumpState == AttackState.Idle && _jumpTimer >= warnAt) _jumpState = AttackState.Warning;

            if (_jumpTimer >= _jumpIntervalLive)
            {
                JumpJustFired = true;
                _jumpState = AttackState.Idle;
                _jumpTimer = 0f;
            }
        }

        private void TickStrike(float dt, float scale)
        {
            _strikeIntervalLive = GridlockTuning.StrikeInterval * scale;
            _strikeTimer += dt;
            if (_strikeTimer >= _strikeIntervalLive)
            {
                StrikeJustFired = true;
                _strikeTimer = 0f;
            }
        }

        private void TickSpawnLevel(float dt, float scale)
        {
            if (SpawnLevel >= GridlockTuning.MaxEscalationLevel)
            {
                SpawnLevelProgress01 = 1f;
                return;
            }

            float interval = GridlockTuning.EscalationLevelInterval * scale;
            _spawnLevelTimer += dt;
            if (_spawnLevelTimer >= interval)
            {
                _spawnLevelTimer -= interval;
                SpawnLevel = Mathf.Min(GridlockTuning.MaxEscalationLevel, SpawnLevel + 1);
            }

            SpawnLevelProgress01 = SpawnLevel >= GridlockTuning.MaxEscalationLevel
                ? 1f
                : Mathf.Clamp01(_spawnLevelTimer / Mathf.Max(0.01f, interval));
        }
    }
}
