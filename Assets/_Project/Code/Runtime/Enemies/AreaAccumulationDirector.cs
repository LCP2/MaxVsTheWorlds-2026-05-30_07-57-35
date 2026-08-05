using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Drives the gated arena's AMBIENT robot population (v0.5 recut spec §2, MV-242): the roaming
    /// crowd every one of the 10 areas carries regardless of a factory. The first time each area is
    /// entered, it queues that area's full population (<see cref="AreaPopulation.ComposeForArea"/> +
    /// <see cref="AreaPopulation.ToughSplitForArea"/>) into one field-wide <see cref="AreaSpawnQueue"/>
    /// and immediately releases everything that fits under the <c>maxActiveRobots</c> concurrent cap —
    /// the room's crowd is standing and active before the player can see it, not trickling in while
    /// they watch (MV-245). Only a roster that overflows the cap stays queued, releasing one at a time
    /// as active robots die (<see cref="Update"/>), the same as before.
    ///
    /// Entry is now GATE-event-driven (<see cref="EnterArea"/>, wired to each <see cref="AreaGate"/>'s
    /// <see cref="AreaGate.Opened"/> by <see cref="BackyardPath"/>): breaking a gate is what actually
    /// grants access to the next room, and it happens a few steps before the player walks through the
    /// doorway — exactly the window MV-245 needs to have the room populated ahead of arrival. This
    /// reverses MV-223/224's original call (position-based, "simpler and exactly as responsive") now
    /// that responsiveness alone is not what the ticket needs — position-crossing is kept in
    /// <see cref="Update"/> purely as a fallback for a zone entered without its gate ever firing
    /// (e.g. area 1, which nothing gates).
    ///
    /// A second, independent source from the three hutches: <see cref="EnemySpawner"/> keeps producing
    /// each factory's own local stream exactly as before (spec: "otherwise unchanged").
    /// </summary>
    public sealed class AreaAccumulationDirector : MonoBehaviour
    {
        /// <summary>Seconds between ambient releases — spaces overflow (over-the-cap) robots out as
        /// slots free, instead of dumping a whole area's death-freed backlog on the player at once.</summary>
        public const float ReleaseInterval = 0.35f;

        /// <summary>Kept clear of a room's walls so a robot never spawns inside geometry.</summary>
        private const float EdgeMargin = 3f;

        /// <summary>Minimum gap kept between a newly placed robot and any other already-active one, or
        /// a cover prop's footprint — placement retries rather than allowing an overlap.</summary>
        private const float PlacementSpacing = 1.5f;

        /// <summary>Candidate points tried before giving up and taking whatever the last attempt found —
        /// placement must always succeed, even in a room too small to satisfy every preference.</summary>
        private const int MaxPlacementAttempts = 12;

        [SerializeField] private RobotEnemy prefab;

        private MapData _map;
        private IReadOnlyList<CoverPiece> _cover = System.Array.Empty<CoverPiece>();
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
        /// after <see cref="MapRuntime.Build"/>. <paramref name="cover"/> is the cover the same build
        /// actually placed — used so a robot is never spawned on top of a hedge or planter.</summary>
        public void Configure(MapData map, IReadOnlyList<CoverPiece> cover)
        {
            _map = map;
            _cover = cover ?? System.Array.Empty<CoverPiece>();
            _queue = new AreaSpawnQueue(Mathf.RoundToInt(
                DevTuning.Or(DevTuning.MaxActiveRobots, RobotCompositionTuning.DefaultMaxActiveRobots)));
            _filledAreas.Clear();
            CurrentArea = 1;
            FillArea(1);
        }

        /// <summary>Grants a room's population a head start: called the instant the gate into it breaks
        /// (<see cref="AreaGate.Opened"/>), which is before the player has actually walked through the
        /// doorway. Idempotent — <see cref="FillArea"/> only ever queues a given area once, so a late
        /// position-crossing fallback call can never double-populate it.</summary>
        public void EnterArea(int areaIndex)
        {
            if (areaIndex <= CurrentArea) return;
            CurrentArea = areaIndex;
            FillArea(areaIndex);
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

            // Fallback only — the real trigger is EnterArea, fired off the gate that guards this zone.
            // Kept for area 1 (nothing gates it) and as a safety net should a gate event ever be missed.
            MapZone zone = _map.ZoneAt(_target.position.x, _target.position.z);
            int area = zone == null ? 0 : AreaIndexOf(zone.id);
            if (area > CurrentArea)
            {
                CurrentArea = area;
                FillArea(area);
            }

            // Overflow only, by now — FillArea already released everything a fresh room could fit
            // under the cap. This is what lets the rest in as the field's live count drops, spaced out
            // rather than dumped all at once.
            _timer += Time.deltaTime;
            if (_timer < ReleaseInterval) return;
            _timer = 0f;

            if (RobotEnemy.ActiveCount < EnemySpawner.GlobalMaxLiveEnemies && _queue.TryRelease(out EnemyKind kind))
                Spawn(kind);
        }

        private void FillArea(int areaIndex)
        {
            if (areaIndex <= 0 || !_filledAreas.Add(areaIndex)) return;

            // The lead-in/entry room (area1's "Patio & Back Door") is where Max spawns — it must stay
            // empty so a fresh run has a safe beat to orient before meeting a robot (MV-256). Marked
            // filled above so nothing re-queues it later; just never queues anything into it now.
            MapZone zone = _map.Zone($"area{areaIndex}");
            if (zone != null && zone.Kind == ZoneKind.Entry) return;

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

            // Instantly, not paced — this room's population must already be standing by the time the
            // player can see it (MV-245). Only what does not fit under the concurrent cap stays queued.
            while (RobotEnemy.ActiveCount < EnemySpawner.GlobalMaxLiveEnemies && _queue.TryRelease(out EnemyKind kind))
                Spawn(kind);
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

        /// <summary>Picks a point inside the room, clear of walls, cover and other active robots, and —
        /// when a camera exists to ask — outside its view: an overflow robot that only gets to enter
        /// once the player has already been fighting in the room for a while must never be seen popping
        /// into existence (MV-245). Placement always succeeds; if nothing in budget satisfies every
        /// preference (a small room, a crowded one), the last candidate tried is used rather than
        /// refusing to place the robot at all.</summary>
        private Vector3 SpawnPointInArea(int areaIndex, float height)
        {
            MapZone zone = _map.Zone($"area{areaIndex}");
            if (zone == null || zone.width <= EdgeMargin * 2f || zone.depth <= EdgeMargin * 2f)
                return _target != null ? _target.position : Vector3.zero;

            Camera cam = Camera.main;
            Vector3 candidate = default;

            for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
            {
                float x = Random.Range(zone.XMin + EdgeMargin, zone.XMax - EdgeMargin);
                float z = Random.Range(zone.ZMin + EdgeMargin, zone.ZMax - EdgeMargin);
                candidate = new Vector3(x, height, z);

                if (OverlapsCoverOrRobot(candidate)) continue;
                if (cam != null && IsOnScreen(cam, candidate)) continue;

                return candidate;
            }

            return candidate;
        }

        private bool OverlapsCoverOrRobot(Vector3 point)
        {
            var flat = new Vector2(point.x, point.z);

            foreach (CoverPiece piece in _cover)
                if (piece.Cover.DistanceTo(flat) < MapValidation.SpawnClearance)
                    return true;

            foreach (RobotEnemy robot in RobotEnemy.Active)
            {
                if (robot == null) continue;
                Vector3 p = robot.transform.position;
                if ((new Vector2(p.x, p.z) - flat).sqrMagnitude < PlacementSpacing * PlacementSpacing)
                    return true;
            }

            return false;
        }

        private static bool IsOnScreen(Camera cam, Vector3 point)
        {
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
            return GeometryUtility.TestPlanesAABB(planes, new Bounds(point, Vector3.one));
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
