using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Drives the gated arena's AMBIENT robot population (v0.5 recut spec §2, MV-242): the roaming
    /// crowd every one of the 10 areas carries regardless of a factory. Watches the player's current
    /// area — read straight off which "area&lt;N&gt;" zone they are standing in, per
    /// <see cref="AreaIndex"/> — and the first time each area is entered, queues that area's full
    /// population (<see cref="AreaPopulation.ComposeForArea"/> + <see cref="AreaPopulation.ToughSplitForArea"/>)
    /// into one field-wide <see cref="AreaSpawnQueue"/>, releasing queued robots one at a time as
    /// active ones die, under the <c>maxActiveRobots</c> concurrent cap.
    ///
    /// This is the runtime area index MV-223/MV-224's own doc comments said had no live hook yet —
    /// position-based rather than gate-event-driven, because an <see cref="AreaGate"/> only ever opens
    /// a doorway the player still has to walk through, so polling where Max actually stands is both
    /// simpler and exactly as responsive.
    ///
    /// A second, independent source from the three hutches: <see cref="EnemySpawner"/> keeps producing
    /// each factory's own local stream exactly as before (spec: "otherwise unchanged").
    /// </summary>
    public sealed class AreaAccumulationDirector : MonoBehaviour
    {
        /// <summary>Seconds between ambient releases — spaces a big area's population out instead of
        /// dumping it on the player the instant a gate opens.</summary>
        public const float ReleaseInterval = 0.35f;

        /// <summary>Kept clear of a room's walls so a robot never spawns inside geometry.</summary>
        private const float EdgeMargin = 3f;

        [SerializeField] private RobotEnemy prefab;

        private MapData _map;
        private Transform _target;
        private Transform _bodies;
        private AreaSpawnQueue _queue;
        private readonly HashSet<int> _filledAreas = new HashSet<int>();
        private readonly Dictionary<EnemyKind, Stack<RobotEnemy>> _pools = new Dictionary<EnemyKind, Stack<RobotEnemy>>();
        private Collider[] _playerColliders;
        private float _timer;

        /// <summary>The 1-based area the player is currently standing in (or last stood in, once past
        /// the final "area&lt;N&gt;" zone — the compost clearing does not advance it further).</summary>
        public int CurrentArea { get; private set; } = 1;

        /// <summary>Robots this director currently considers live on the field.</summary>
        public int ActiveCount => _queue?.ActiveCount ?? 0;

        /// <summary>Robots still queued for the current (or a past) area, not yet released.</summary>
        public int QueuedCount => _queue?.QueuedCount ?? 0;

        /// <summary>Wires this director to a built map and starts Area 1's population. Call once, right
        /// after <see cref="MapRuntime.Build"/>.</summary>
        public void Configure(MapData map)
        {
            _map = map;
            _queue = new AreaSpawnQueue(Mathf.RoundToInt(
                DevTuning.Or(DevTuning.MaxActiveRobots, RobotCompositionTuning.DefaultMaxActiveRobots)));
            _filledAreas.Clear();
            CurrentArea = 1;
            FillArea(1);
        }

        /// <summary>The 1-based area number of an "area&lt;N&gt;" zone id, or 0 for anything else
        /// (the compost clearing, an unrecognised id, standing in the void).</summary>
        public static int AreaIndexOf(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId) || !zoneId.StartsWith("area")) return 0;
            return int.TryParse(zoneId.Substring(4), out int n) ? n : 0;
        }

        private void Update()
        {
            if (_map == null || _queue == null) return;

            if (_target == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) _target = p.transform;
                if (_target == null) return;
            }

            MapZone zone = _map.ZoneAt(_target.position.x, _target.position.z);
            int area = zone == null ? 0 : AreaIndexOf(zone.id);
            if (area > CurrentArea)
            {
                CurrentArea = area;
                FillArea(area);
            }

            _timer += Time.deltaTime;
            if (_timer < ReleaseInterval) return;
            _timer = 0f;

            if (RobotEnemy.ActiveCount < EnemySpawner.GlobalMaxLiveEnemies && _queue.TryRelease(out EnemyKind kind))
                Spawn(kind);
        }

        private void FillArea(int areaIndex)
        {
            if (areaIndex <= 0 || !_filledAreas.Add(areaIndex)) return;

            var (large, small) = AreaPopulation.ComposeForArea(areaIndex,
                DevTuning.Or(DevTuning.StartLargeCount, RobotCompositionTuning.DefaultStartLargeCount),
                DevTuning.Or(DevTuning.StartSmallCount, RobotCompositionTuning.DefaultStartSmallCount),
                DevTuning.Or(DevTuning.AreaGrowthPct, RobotCompositionTuning.DefaultAreaGrowthPct),
                DevTuning.Or(DevTuning.LargeToSmallRatio, RobotCompositionTuning.DefaultLargeToSmallRatio),
                DevTuning.Or(DevTuning.LargeShareDriftPerArea, RobotCompositionTuning.DefaultLargeShareDriftPerArea));

            _queue.FillForArea(areaIndex, large, small,
                DevTuning.Or(DevTuning.HeavyIntroArea, RobotCompositionTuning.DefaultHeavyIntroArea),
                DevTuning.Or(DevTuning.BruteIntroArea, RobotCompositionTuning.DefaultBruteIntroArea),
                DevTuning.Or(DevTuning.ToughSubstitutionPct, RobotCompositionTuning.DefaultToughSubstitutionPct));
        }

        private void Spawn(EnemyKind kind)
        {
            EnemyArchetype archetype = EnemyArchetype.Of(kind)
                .WithHealthMultiplier(DevTuning.Or(DevTuning.RobotHealthMultiplier, EnemySpawner.DefaultRobotHealthMultiplier))
                .Toughened(DifficultyDirector.ToughnessMultiplier);

            RobotEnemy e = Take(kind, archetype);
            e.transform.position = SpawnPointInArea(CurrentArea, archetype.SpawnHeight);
            e.transform.rotation = Quaternion.identity;
            e.gameObject.SetActive(true);

            // Re-applied on every spawn, not just on creation — Unity drops an ignored collider pair
            // when the collider is disabled, and pooling disables it on every death.
            LetThePlayerThrough(e.gameObject);
        }

        private Vector3 SpawnPointInArea(int areaIndex, float height)
        {
            MapZone zone = _map.Zone($"area{areaIndex}");
            if (zone == null || zone.width <= EdgeMargin * 2f || zone.depth <= EdgeMargin * 2f)
                return _target != null ? _target.position : Vector3.zero;

            float x = Random.Range(zone.XMin + EdgeMargin, zone.XMax - EdgeMargin);
            float z = Random.Range(zone.ZMin + EdgeMargin, zone.ZMax - EdgeMargin);
            return new Vector3(x, height, z);
        }

        private RobotEnemy Take(EnemyKind kind, in EnemyArchetype archetype)
        {
            if (_pools.TryGetValue(kind, out var pool) && pool.Count > 0)
            {
                RobotEnemy pooled = pool.Pop();
                pooled.Apply(archetype);
                return pooled;
            }
            return CreateInstance(archetype);
        }

        private RobotEnemy CreateInstance(in EnemyArchetype a)
        {
            RobotEnemy e;
            if (prefab != null)
            {
                e = Instantiate(prefab, Bodies());
            }
            else
            {
                var go = GameObject.CreatePrimitive(
                    a.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
                go.name = $"RobotEnemy {a.Kind} (area spawn)";
                go.transform.SetParent(Bodies(), false);
                go.transform.localScale = a.BodyScale;

                var cc = go.AddComponent<CharacterController>();
                float lateral = Mathf.Max(a.BodyScale.x, a.BodyScale.z);
                cc.height = a.ColliderHeight / Mathf.Max(a.BodyScale.y, 1e-4f);
                cc.radius = a.ColliderRadius / Mathf.Max(lateral, 1e-4f);
                cc.center = Vector3.zero;

                e = go.AddComponent<RobotEnemy>();
            }

            e.Apply(a);
            e.Died += OnEnemyDied;
            e.gameObject.SetActive(false);
            return e;
        }

        private void OnEnemyDied(RobotEnemy e)
        {
            _queue?.ReportDestroyed();
            if (!_pools.TryGetValue(e.Kind, out var pool))
            {
                pool = new Stack<RobotEnemy>();
                _pools[e.Kind] = pool;
            }
            pool.Push(e);
        }

        private Transform Bodies()
        {
            if (_bodies == null)
                _bodies = new GameObject("Area Robots").transform;
            return _bodies;
        }

        private void LetThePlayerThrough(GameObject enemy)
        {
            if (_playerColliders == null || _playerColliders.Length == 0)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p == null) return;
                _playerColliders = p.GetComponents<Collider>();
            }

            var enemyColliders = enemy.GetComponents<Collider>();
            foreach (var ec in enemyColliders)
            {
                if (ec == null) continue;
                foreach (var pc in _playerColliders)
                {
                    if (pc == null) continue;
                    Physics.IgnoreCollision(ec, pc, true);
                }
            }
        }
    }
}
