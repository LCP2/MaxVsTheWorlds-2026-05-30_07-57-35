using UnityEngine;

namespace MaxWorlds.Bosses
{
    /// <summary>The Big Bermuda attack cycle phases (YT-27, slice).</summary>
    public enum BossAction { Reposition, ChargeWindup, Charge, Recover }

    /// <summary>
    /// Pure attack-cycle sequencer for the slice Big Bermuda boss (YT-27). Cycles
    /// Reposition → ChargeWindup → Charge → Recover on tunable timers; the MonoBehaviour
    /// executes the physical effect of whatever phase is <see cref="Current"/> and reacts to
    /// <see cref="JustEntered"/> transitions. Below the enrage threshold it reports
    /// <see cref="Enraged"/> and scales every timer down (faster, angrier) — the slice's
    /// "enrage at low health" in place of the full M2 two-phase choreography (spec §4.7).
    /// No MonoBehaviour, so the sequence and timings are unit-testable.
    /// </summary>
    public sealed class BigBermudaBrain
    {
        private static readonly BossAction[] Cycle =
            { BossAction.Reposition, BossAction.ChargeWindup, BossAction.Charge, BossAction.Recover };

        // Base seconds per phase (index matches Cycle), from BossTuning (YT-94) — the wind-up is the
        // dodge window, and it is not a number that should live in two places.
        private static readonly float[] BaseDuration =
        {
            BossTuning.Reposition, BossTuning.ChargeWindup, BossTuning.ChargeTime, BossTuning.Recover,
        };

        private readonly float _enrageThreshold;
        private readonly float _enrageTimeScale;

        private int _index;
        private float _timer;

        public BossAction Current => Cycle[_index];

        /// <summary>True on the first Tick after a phase change (the MonoBehaviour fires the
        /// phase's effect — start a charge, drop a telegraph — on this edge).</summary>
        public bool JustEntered { get; private set; }

        /// <summary>True while HP is at/below the enrage threshold — drives blade-rain + speed.</summary>
        public bool Enraged { get; private set; }

        /// <summary>Defaults come from <see cref="BossTuning"/> — the enrage used to scale the TELL
        /// down along with the attack (0.65), which made the fight harder to read exactly as it got
        /// harder to survive (YT-94).</summary>
        public BigBermudaBrain(float enrageThreshold = BossTuning.EnrageThreshold,
                               float enrageTimeScale = BossTuning.EnrageTimeScale)
        {
            _enrageThreshold = Mathf.Clamp01(enrageThreshold);
            _enrageTimeScale = Mathf.Clamp(enrageTimeScale, 0.2f, 1f);
            _index = 0;
            _timer = ScaledDuration(0);
            JustEntered = true; // the opening phase must be executed
        }

        /// <summary>Advance the cycle. Call once per frame with deltaTime and current HP fraction.</summary>
        public void Tick(float dt, float hpNormalized)
        {
            JustEntered = false;
            Enraged = hpNormalized <= _enrageThreshold;

            _timer -= Mathf.Max(0f, dt);
            if (_timer <= 0f)
            {
                _index = (_index + 1) % Cycle.Length;
                _timer += ScaledDuration(_index);
                if (_timer <= 0f) _timer = ScaledDuration(_index); // guard against huge dt
                JustEntered = true;
            }
        }

        private float ScaledDuration(int index)
            => BaseDuration[index] * (Enraged ? _enrageTimeScale : 1f);
    }
}
