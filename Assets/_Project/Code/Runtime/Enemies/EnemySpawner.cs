using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Pools and spawns <see cref="RobotEnemy"/> instances for the slice (YT-36).
    /// Pooling (reuse on death via SetActive) keeps ~20–30 concurrent enemies free
    /// of per-spawn GC churn and leaks. Spawns on a ring around the target up to a
    /// live cap. Greybox enemy prefab is assigned in the editor; if none is set the
    /// spawner builds a primitive stand-in so it runs headless.
    /// </summary>
    public sealed class EnemySpawner : MonoBehaviour
    {
        [SerializeField] private RobotEnemy prefab;
        [SerializeField] private Transform target;
        [SerializeField] private int liveCap = 24;
        [SerializeField] private float spawnInterval = 0.4f;
        [SerializeField] private float spawnRadius = 12f;

        private readonly Stack<RobotEnemy> _pool = new Stack<RobotEnemy>();
        private readonly List<RobotEnemy> _live = new List<RobotEnemy>(32);
        private float _timer;

        public int LiveCount => _live.Count;

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < spawnInterval || _live.Count >= liveCap) return;
            _timer = 0f;
            SpawnOne();
        }

        private void SpawnOne()
        {
            RobotEnemy e = _pool.Count > 0 ? _pool.Pop() : CreateInstance();
            float ang = (_live.Count * 137.5f) * Mathf.Deg2Rad; // golden-angle spread
            Vector3 origin = target != null ? target.position : transform.position;
            Vector3 pos = origin + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * spawnRadius;
            pos.y = 1f;
            e.transform.position = pos;
            e.gameObject.SetActive(true);
            _live.Add(e);
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
