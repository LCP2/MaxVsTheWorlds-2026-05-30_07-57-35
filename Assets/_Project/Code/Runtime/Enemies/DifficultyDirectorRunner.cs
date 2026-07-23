using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// The one thing that drives <see cref="DifficultyDirector"/>'s clock (YT-181). Self-installing,
    /// the same pattern as <c>SettingsPanel</c>: no scene wiring, so it exists in every scene —
    /// including a bare test fixture — with zero setup, and runs headlessly in CI.
    ///
    /// Ticking lives here rather than on <see cref="EnemySpawner"/> so a level with two factories
    /// (YT-92) doesn't double the elapsed-time rate by having two Updates advance the same clock.
    /// </summary>
    public sealed class DifficultyDirectorRunner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<DifficultyDirectorRunner>() != null) return;
            new GameObject("DifficultyDirectorRunner").AddComponent<DifficultyDirectorRunner>();
        }

        private void OnEnable() => HudSignals.FactoryDestroyed += OnFactoryDestroyed;
        private void OnDisable() => HudSignals.FactoryDestroyed -= OnFactoryDestroyed;

        private void OnFactoryDestroyed(Vector3 _) => DifficultyDirector.ReportShedDestroyed();

        private void Update() => DifficultyDirector.Tick(Time.deltaTime);
    }
}
