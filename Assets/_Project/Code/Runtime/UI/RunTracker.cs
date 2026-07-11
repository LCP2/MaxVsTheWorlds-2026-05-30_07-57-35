using UnityEngine;
using MaxWorlds.Player;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Watches a Backyard run and shows the Result screen when it ends (YT-31). Accumulates the
    /// slice stats — run time, robots killed, factory destroyed — off <see cref="HudSignals"/>,
    /// ends in Victory when the boss is defeated and Defeat when Max dies, then hands the stats
    /// to a <see cref="ResultScreen"/>. Code-driven and self-wiring (finds PlayerHealth by type),
    /// so it runs headlessly and on the WebGL build with no editor setup.
    /// </summary>
    public sealed class RunTracker : MonoBehaviour
    {
        private readonly RunStats _stats = new RunStats();
        private PlayerHealth _health;
        private bool _ended;

        private void Awake()
        {
            _health = FindFirstObjectByType<PlayerHealth>();
        }

        private void OnEnable()
        {
            HudSignals.EnemyKilled += OnKill;
            HudSignals.FactoryDestroyed += OnFactory;
            HudSignals.BossDefeated += OnBossDefeated;
            if (_health != null) _health.Changed += OnHealthChanged;
        }

        private void OnDisable()
        {
            HudSignals.EnemyKilled -= OnKill;
            HudSignals.FactoryDestroyed -= OnFactory;
            HudSignals.BossDefeated -= OnBossDefeated;
            if (_health != null) _health.Changed -= OnHealthChanged;
        }

        private void Update()
        {
            if (!_ended) _stats.Tick(Time.deltaTime);
        }

        private void OnKill(Vector3 _) => _stats.AddKill();
        private void OnFactory(Vector3 _) => _stats.MarkFactoryDestroyed();
        private void OnBossDefeated() => End(RunOutcome.Victory);

        private void OnHealthChanged(float _)
        {
            if (_health != null && !_health.IsAlive) End(RunOutcome.Defeat);
        }

        private void End(RunOutcome outcome)
        {
            if (_ended) return;
            _ended = true;
            _stats.Finish(outcome);

            var go = new GameObject("Result Screen");
            go.AddComponent<ResultScreen>().Show(_stats);
        }
    }
}
