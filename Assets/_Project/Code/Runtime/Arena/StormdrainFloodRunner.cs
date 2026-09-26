using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// The one thing that drives <see cref="StormdrainFlood"/>'s clock and applies its damage-over-time
    /// (MV-774) — self-installing, same pattern as <see cref="DifficultyDirectorRunner"/>: no scene
    /// wiring, so it exists in every scene, including a bare test fixture, with zero setup.
    ///
    /// The flood's slow is applied passively, through <see cref="MapSlowZones"/> (every mover already
    /// consults that hook every frame) — every mover, robots included, still slows in flooded ground.
    /// The damage-over-time here is the player's ALONE (MV-795): the flood is meant as pressure on the
    /// player's own route against the clock, not a second, uncontrolled source of robot deaths — MV-774's
    /// own robot loop killed every robot standing in flooded ground, which emptied the world of its own
    /// population and, through <see cref="MaxWorlds.Feel.GameFeel"/>'s damage/kill trauma, pinned the
    /// camera shake on permanently once enough robots were dying in the flood at once.
    /// </summary>
    [MaxWorlds.Core.PerfSection("sludge")]
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

        private void Update() => TickFlood(Time.deltaTime);

        /// <summary>The runner's whole per-frame job, pulled out so a test can drive it directly at a
        /// cadence it controls — same "a caller decides the cadence, a test drives it directly" idiom
        /// <see cref="MaxWorlds.Factories.Replicator.TickLure"/> / <see cref="MaxWorlds.Factories.MowerHutch.TickMobility"/>
        /// already use.</summary>
        public void TickFlood(float dt)
        {
            // MV-836: FloodEnabled is off by design — never ticks the flood, never damages anyone.
            if (!StormdrainFlood.FloodEnabled) return;

            StormdrainFlood.Tick(dt, FactoryCensus.ReplicatorsAlive, StormdrainDressing.PumpHousingsAlive);

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
        }
    }
}
