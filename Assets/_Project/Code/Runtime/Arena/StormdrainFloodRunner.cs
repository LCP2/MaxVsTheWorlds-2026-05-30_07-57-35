using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// The one thing that drives <see cref="StormdrainFlood"/>'s clock and applies its damage-over-time
    /// (MV-774) — self-installing, same pattern as <see cref="DifficultyDirectorRunner"/>: no scene
    /// wiring, so it exists in every scene, including a bare test fixture, with zero setup.
    ///
    /// The flood's slow is applied passively, through <see cref="MapSlowZones"/> (every mover already
    /// consults that hook every frame) — only the damage-over-time needs a per-frame driver, since
    /// "flooded ground hurts" has no other natural home the way "flooded ground slows" already does.
    /// </summary>
    public sealed class StormdrainFloodRunner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<StormdrainFloodRunner>() != null) return;
            new GameObject("StormdrainFloodRunner").AddComponent<StormdrainFloodRunner>();
        }

        private Transform _target;
        private IDamageable _targetDamageable;
        private FloodDamageTicker _playerTicker;

        // Keyed by instance rather than removed on death: RobotEnemy is pooled (the same instance comes
        // back to life for a later spawn), and a stray few-hundred-millisecond accumulator residue
        // carried into a fresh spawn is harmless — Rule 2 already guarantees flooded ground is never
        // more than a slow + damage-over-time, never instant.
        private readonly Dictionary<RobotEnemy, FloodDamageTicker> _robotTickers =
            new Dictionary<RobotEnemy, FloodDamageTicker>();

        private void Update()
        {
            float dt = Time.deltaTime;
            StormdrainFlood.Tick(dt, FactoryCensus.ReplicatorsAlive, livePumpHousings: 0);

            if (_target == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null)
                {
                    _target = p.transform;
                    _targetDamageable = p.GetComponent<IDamageable>();
                }
            }

            if (_target != null)
                _playerTicker.Tick(dt, StormdrainFlood.IsFlooded(_target.position), _targetDamageable, _target.position);

            IReadOnlyList<RobotEnemy> active = RobotEnemy.Active;
            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy r = active[i];
                if (r == null || !r.IsAlive) continue;

                _robotTickers.TryGetValue(r, out FloodDamageTicker ticker);
                ticker.Tick(dt, StormdrainFlood.IsFlooded(r.transform.position), r, r.transform.position);
                _robotTickers[r] = ticker;
            }
        }
    }
}
