using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// MV-1240: ticks the plasma vent cycle and applies <see cref="MapVentDamage.FireAt"/> to the player,
    /// every live <see cref="RobotEnemy"/> and every live <see cref="Sentinel"/> the instant the column
    /// fires. Self-installing: no scene wiring, so it exists in every scene — including a bare test
    /// fixture — with zero setup, same idiom as <see cref="MapSludgeDamageRunner"/>.
    ///
    /// The cycle is ONE shared clock for the whole map, not one per vent — every vent in the game fires
    /// in lockstep (the design image shows several firing together), so there is nothing to desync.
    /// </summary>
    [MaxWorlds.Core.PerfSection("vents")]
    public sealed class MapVentDamageRunner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<MapVentDamageRunner>() != null) return;
            new GameObject("MapVentDamageRunner").AddComponent<MapVentDamageRunner>();
        }

        private static MapVentDamageRunner s_instance;

        private Transform _target;
        private IDamageable _targetDamageable;

        private float _age;
        private float _nextFireAge = MapVentDamage.GlowDuration;
        private bool _wasPaused;

        /// <summary>Seconds into the current cycle (0 at the start of the glow tell), for the visual to
        /// read — <see cref="MaxWorlds.VFX.PlasmaVentVisual"/> never ticks its own clock, it only ever
        /// reads this one, so a vent's look and its damage can never drift apart.</summary>
        public static float CyclePhase => s_instance != null ? s_instance._age % MapVentDamage.CycleInterval : 0f;

        private void Awake() => s_instance = this;

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        private void Update() => TickVentDamage(Time.deltaTime);

        /// <summary>The runner's whole per-frame job, pulled out so a test can drive it directly at a
        /// cadence it controls (same "a caller decides the cadence" idiom as
        /// <see cref="MapSludgeDamageRunner.TickSludgeDamage"/>).
        ///
        /// A dt of 0 (the game paused — Time.timeScale == 0, e.g. a menu) marks the cycle as paused and
        /// advances nothing: "Cycle pauses" (ticket table). The NEXT non-zero dt after a pause resets the
        /// clock to the very start of a fresh glow — "restarts from the glow on resume" (ticket table) —
        /// rather than resuming mid-column with no tell.</summary>
        public void TickVentDamage(float dt)
        {
            if (dt <= 0f) { _wasPaused = true; return; }

            if (_wasPaused)
            {
                _wasPaused = false;
                _age = 0f;
                _nextFireAge = MapVentDamage.GlowDuration;
            }

            _age += dt;
            while (_age >= _nextFireAge)
            {
                FireColumn();
                _nextFireAge += MapVentDamage.CycleInterval;
            }
        }

        private void FireColumn()
        {
            MapData map = EnemyNavigation.Map;
            if (map == null) return;

            if (_target == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null)
                {
                    _target = p.transform;
                    _targetDamageable = p.GetComponent<IDamageable>();
                }
            }
            if (_target != null) MapVentDamage.FireAt(map, _targetDamageable, _target.position);

            var robots = RobotEnemy.Active;
            for (int i = 0; i < robots.Count; i++)
            {
                RobotEnemy r = robots[i];
                if (r == null) continue;
                MapVentDamage.FireAt(map, r, r.transform.position);
            }

            var sentinels = Sentinel.Active;
            for (int i = 0; i < sentinels.Count; i++)
            {
                Sentinel s = sentinels[i];
                if (s == null) continue;
                MapVentDamage.FireAt(map, s, s.transform.position);
            }
        }
    }
}
