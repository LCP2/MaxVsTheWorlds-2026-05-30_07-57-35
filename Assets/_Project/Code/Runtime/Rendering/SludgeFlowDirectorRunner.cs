using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// The one thing that drives <see cref="SludgeFlowDirector"/>'s clock (MV-873). Self-installing,
    /// the same pattern as <c>BlinkerSquadDirectorRunner</c>/<c>DifficultyDirectorRunner</c>: no scene
    /// wiring, so it exists in every scene — including a bare test fixture — with zero setup, and runs
    /// headlessly in CI.
    ///
    /// Looks the player up by tag rather than by <c>PlayerController</c> — <c>MaxWorlds.Gameplay</c>
    /// (where that type lives) already references <c>MaxWorlds.Rendering</c> (for
    /// <see cref="StormdrainKit"/> itself), so the reverse reference this runner would need to hold a
    /// <c>PlayerController</c> directly does not compile. The tag lookup is the same idiom
    /// <c>BlinkerSquadDirectorRunner</c> already uses for exactly this reason.
    /// </summary>
    public sealed class SludgeFlowDirectorRunner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<SludgeFlowDirectorRunner>() != null) return;
            new GameObject("SludgeFlowDirectorRunner").AddComponent<SludgeFlowDirectorRunner>();
        }

        private Transform _player;

        private void Update()
        {
            if (_player == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p == null) return;
                _player = p.transform;
            }

            SludgeFlowDirector.Tick(Time.deltaTime, _player.position);
        }
    }
}
