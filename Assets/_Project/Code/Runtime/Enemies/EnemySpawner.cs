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

        [Header("Mix (YT-66) — two enemy types, so the fight has texture")]
        [Tooltip("Every Nth robot is a bruiser. 1 in 4 keeps them a punctuation mark, not the norm.")]
        [SerializeField] private int bruiserEvery = 4;
        [Tooltip("No bruisers until this many robots have come out — the opening teaches the rusher " +
                 "first, and the bruiser lands as an escalation.")]
        [SerializeField] private int firstBruiserAt = 3;

        // One pool PER KIND. A single pool would hand a dead bruiser back as the next rusher, still
        // wearing its box body and its collider — the classic pooling bug.
        private readonly Dictionary<EnemyKind, Stack<RobotEnemy>> _pools =
            new Dictionary<EnemyKind, Stack<RobotEnemy>>();
        private readonly List<RobotEnemy> _live = new List<RobotEnemy>(32);
        private float _timer;
        private float _elapsed;
        private int _emitted;
        private Transform _target;

        public int LiveCount => _live.Count;

        /// <summary>Live count of one kind — lets a test prove the mix actually reaches the field.</summary>
        public int LiveCountOf(EnemyKind kind)
        {
            int n = 0;
            for (int i = 0; i < _live.Count; i++) if (_live[i].Kind == kind) n++;
            return n;
        }

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
            EnemyKind kind = EnemyMix.KindFor(_emitted, bruiserEvery, firstBruiserAt);
            EnemyArchetype archetype = EnemyArchetype.Of(kind);
            RobotEnemy e = Take(kind, archetype);

            // Out of the mouth, fanned, facing the way it's about to run.
            Vector3 dir = FactoryMouth.ExitDirection(
                ToTarget(), -transform.forward, _emitted++, mouthHalfAngle);

            e.transform.position = FactoryMouth.ExitPoint(
                transform.position, dir, spawnRadius, archetype.SpawnHeight);
            e.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            e.gameObject.SetActive(true);
            _live.Add(e);
        }

        private RobotEnemy Take(EnemyKind kind, in EnemyArchetype archetype)
        {
            if (_pools.TryGetValue(kind, out var pool) && pool.Count > 0) return pool.Pop();
            return CreateInstance(archetype);
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

        /// <summary>Builds one robot of the given kind. The body is the archetype's silhouette, and
        /// the CharacterController is sized to match what you can see — a controller inherits the
        /// transform's scale, so a bigger body with a default controller would fight a collider
        /// that's the wrong shape for it.</summary>
        private RobotEnemy CreateInstance(in EnemyArchetype a)
        {
            RobotEnemy e;
            if (prefab != null)
            {
                // Stats still differ, but both kinds wear the prefab's body. Fine while the prefab
                // is unset (the greybox path below is what ships); revisit when Phase C art lands
                // and each kind needs its own prefab.
                e = Instantiate(prefab, transform);
            }
            else
            {
                var go = GameObject.CreatePrimitive(
                    a.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
                go.name = $"RobotEnemy {a.Kind} (stand-in)";
                go.transform.SetParent(transform);
                go.transform.localScale = a.BodyScale;

                var cc = go.AddComponent<CharacterController>();
                // Undo the transform scale so the metres asked for are the metres you get.
                float lateral = Mathf.Max(a.BodyScale.x, a.BodyScale.z);
                cc.height = a.ColliderHeight / Mathf.Max(a.BodyScale.y, 1e-4f);
                cc.radius = a.ColliderRadius / Mathf.Max(lateral, 1e-4f);
                cc.center = Vector3.zero;   // primitives are centred on their origin

                e = go.AddComponent<RobotEnemy>();
            }

            e.Apply(a);                 // stats — after Awake, which seeded the defaults
            e.Died += OnEnemyDied;
            e.gameObject.SetActive(false);
            return e;
        }

        private void OnEnemyDied(RobotEnemy e)
        {
            _live.Remove(e);
            if (!_pools.TryGetValue(e.Kind, out var pool))
            {
                pool = new Stack<RobotEnemy>();
                _pools[e.Kind] = pool;
            }
            pool.Push(e); // back to its OWN pool; reused on the next spawn of this kind (no leak)
        }
    }
}
