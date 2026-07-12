using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Pools and spawns <see cref="RobotEnemy"/> instances for the slice (YT-36).
    /// Pooling (reuse on death via SetActive) keeps ~20–30 concurrent enemies free
    /// of per-spawn GC churn and leaks. Greybox enemy prefab is assigned in the editor; if none is
    /// set the spawner builds a primitive stand-in so it runs headless.
    ///
    /// Robots leave through the factory's <see cref="FactoryMouth"/> — out of the face pointing at
    /// Max — and then chase him down the lawn (YT-70). They used to appear on a ring centred on
    /// whatever this spawner was attached to, which read as teleporting-in rather than as a stream
    /// pouring out of a source.
    /// </summary>
    public sealed class EnemySpawner : MonoBehaviour
    {
        [SerializeField] private RobotEnemy prefab;

        [Tooltip("Who the stream flows toward (Max). Found by the 'Player' tag if left empty.")]
        [SerializeField] private Transform target;

        [Header("Swarm tuning (YT-63 kiteability — dense but survivable)")]
        [Tooltip("Max robots alive at once. Kept modest so the player can kite instead of drown.")]
        [SerializeField] private int maxLiveEnemies = 12;
        [Tooltip("Seconds between spawns at run start (breathable).")]
        [SerializeField] private float spawnIntervalStart = 1.8f;
        [Tooltip("Seconds between spawns at steady state (peak pressure).")]
        [SerializeField] private float spawnIntervalMin = 1.2f;
        [Tooltip("Seconds over which the cadence ramps from start to min.")]
        [SerializeField] private float rampSeconds = 45f;

        [Header("Mouth (YT-70) — robots pour OUT of the factory, toward Max")]
        [Tooltip("How far in front of the factory a robot lands. Must clear the factory body.")]
        [SerializeField] private float spawnRadius = 3.5f;
        [Tooltip("Half-width of the emission fan, in degrees. Wide enough to read as a stream, " +
                 "narrow enough that nothing appears behind the shed.")]
        [SerializeField] private float mouthHalfAngle = 55f;

        private readonly Stack<RobotEnemy> _pool = new Stack<RobotEnemy>();
        private readonly List<RobotEnemy> _live = new List<RobotEnemy>(32);
        private float _timer;
        private float _elapsed;
        private int _emitted;
        private Transform _target;

        public int LiveCount => _live.Count;

        /// <summary>Current seconds-between-spawns for the run time so far.</summary>
        public float CurrentInterval =>
            SpawnCadence.IntervalAt(_elapsed, spawnIntervalStart, spawnIntervalMin, rampSeconds);

        private void Update()
        {
            float dt = Time.deltaTime;
            _elapsed += dt;
            _timer += dt;
            if (_timer < CurrentInterval || _live.Count >= maxLiveEnemies) return;
            _timer = 0f;
            SpawnOne();
        }

        private void SpawnOne()
        {
            RobotEnemy e = _pool.Count > 0 ? _pool.Pop() : CreateInstance();

            // Out of the mouth, fanned, facing the way it's about to run.
            Vector3 dir = FactoryMouth.ExitDirection(
                ToTarget(), -transform.forward, _emitted++, mouthHalfAngle);

            e.transform.position = FactoryMouth.ExitPoint(transform.position, dir, spawnRadius, 1f);
            e.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            e.gameObject.SetActive(true);
            _live.Add(e);
        }

        /// <summary>Direction from the factory to Max — the way the stream flows. Zero if there's
        /// nobody to flow toward, which makes the mouth fall back to the factory's own front face.</summary>
        private Vector3 ToTarget()
        {
            if (_target == null) _target = target;
            if (_target == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) _target = p.transform;
            }
            return _target != null ? _target.position - transform.position : Vector3.zero;
        }

        private RobotEnemy CreateInstance()
        {
            RobotEnemy e;
            if (prefab != null)
            {
                e = Instantiate(prefab, transform);
            }
            else
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "RobotEnemy (stand-in)";
                go.transform.SetParent(transform);
                go.AddComponent<CharacterController>();
                e = go.AddComponent<RobotEnemy>();
            }
            e.Died += OnEnemyDied;
            e.gameObject.SetActive(false);
            return e;
        }

        private void OnEnemyDied(RobotEnemy e)
        {
            _live.Remove(e);
            _pool.Push(e); // returns to pool; reused on next spawn (no leak)
        }
    }
}
