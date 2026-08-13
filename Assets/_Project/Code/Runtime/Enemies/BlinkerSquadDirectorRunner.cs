using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// The one thing that drives <see cref="BlinkerSquadDirector"/>'s clock (MV-366). Self-installing,
    /// the same pattern as <see cref="DifficultyDirectorRunner"/>: no scene wiring, so it exists in
    /// every scene — including a bare test fixture — with zero setup, and runs headlessly in CI.
    /// </summary>
    public sealed class BlinkerSquadDirectorRunner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<BlinkerSquadDirectorRunner>() != null) return;
            new GameObject("BlinkerSquadDirectorRunner").AddComponent<BlinkerSquadDirectorRunner>();
        }

        private Transform _target;

        private void Update()
        {
            if (_target == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) _target = p.transform;
                if (_target == null) return;
            }

            BlinkerSquadDirector.Tick(Time.deltaTime, _target.position);
        }
    }
}
