using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Save;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Watches a Backyard run and shows the Result screen when the boss falls (YT-31). Accumulates
    /// the slice stats — run time, robots killed, factory destroyed — off <see cref="HudSignals"/>,
    /// then hands them to a <see cref="ResultScreen"/> once Victory is sealed. Code-driven and
    /// self-wiring, so it runs headlessly and on the WebGL build with no editor setup.
    ///
    /// MV-427: a death no longer seals a Defeat outcome here — Max dying now continues the run
    /// (<see cref="WorldRunner"/> handles the respawn), so the only way this run ever ends is Victory.
    ///
    /// YT-152 — the win doesn't cut straight to the card. Beating the boss <em>seals</em> the outcome
    /// (the clock stops on the kill), but the results are held back: the blow-up, the flung parts and
    /// the walk-out to the exit gate play first, and the card only lands on
    /// <see cref="HudSignals.BossPayoffFinished"/> — raised by <c>BossVictoryPayoff</c> when Max steps
    /// through the gate, or when that sequence times out. A safety timeout guarantees the card
    /// appears even if that director is absent, so a win can never strand the player on a
    /// frozen-in-nothing arena.
    /// </summary>
    public sealed class RunTracker : MonoBehaviour
    {
        /// <summary>Backstop, realtime seconds: if the win is sealed but the payoff director never
        /// raises <see cref="HudSignals.BossPayoffFinished"/> (e.g. it isn't in the scene), show the
        /// card anyway. Longer than the payoff's own timeout, so in the real game the walk-out drives
        /// the timing and this never fires.</summary>
        private const float VictorySafetyTimeout = 20f;

        private readonly RunStats _stats = new RunStats();

        private bool _sealed;              // outcome decided; the clock stops and nothing may override it
        private bool _shown;              // the result card has been built
        private bool _awaitingPayoff;     // a victory is sealed and we're holding for the payoff to finish
        private float _sealedRealtime;

        private void OnEnable()
        {
            HudSignals.EnemyKilled += OnKill;
            HudSignals.FactoryDestroyed += OnFactory;
            HudSignals.BossDefeated += OnBossDefeated;
            HudSignals.BossPayoffFinished += OnPayoffFinished;
        }

        private void OnDisable()
        {
            HudSignals.EnemyKilled -= OnKill;
            HudSignals.FactoryDestroyed -= OnFactory;
            HudSignals.BossDefeated -= OnBossDefeated;
            HudSignals.BossPayoffFinished -= OnPayoffFinished;
        }

        private void Update()
        {
            if (!_sealed)
            {
                _stats.Tick(Time.deltaTime);
                _stats.RecordDifficultyPeak(DifficultyDirector.Normalized);
                return;
            }

            // Held for the victory payoff: unscaled, because the world is still live at timeScale 1
            // until the card lands, but this is a real-time backstop regardless.
            if (_awaitingPayoff && !_shown
                && Time.unscaledTime - _sealedRealtime >= VictorySafetyTimeout)
                ShowResults();
        }

        private void OnKill(Vector3 _) => _stats.AddKill();
        private void OnFactory(Vector3 _) => _stats.MarkFactoryDestroyed();

        // The boss is down: lock in the win and start holding for the payoff. The card waits.
        private void OnBossDefeated()
        {
            if (_sealed) return;
            Seal(RunOutcome.Victory);
            _awaitingPayoff = true;
        }

        // The payoff played out (Max reached the gate, or it timed out) — now show the sealed win.
        private void OnPayoffFinished()
        {
            if (_sealed) ShowResults();
        }

        private void Seal(RunOutcome outcome)
        {
            if (_sealed) return;
            _sealed = true;
            _sealedRealtime = Time.unscaledTime;
            _stats.Finish(outcome);

            // MV-427: deaths taken is the new personal-best discriminator — the peak-Domination %
            // stopped meaning anything once a death no longer ends the run (every player eventually
            // reaches 100%). Still only banked on a run that actually finishes (Victory); bailing out
            // early via the HOME button records nothing (YT-218).
            SaveSystem.RecordResult(SaveSystem.ActiveSlot, DeathRunState.DeathsTaken);
        }

        private void ShowResults()
        {
            if (_shown) return;
            _shown = true;

            var go = new GameObject("Result Screen");
            go.AddComponent<ResultScreen>().Show(_stats);
        }
    }
}
