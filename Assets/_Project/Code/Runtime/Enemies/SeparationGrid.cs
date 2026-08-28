using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// A coarse XZ spatial hash over the field-wide robot roster (MV-611) — what makes
    /// <see cref="RobotEnemy.TickChase"/>'s neighbour lookup for <see cref="EnemySeparation.Push"/>
    /// bounded by LOCAL density instead of by the level's whole accumulated population.
    ///
    /// Before this, every chasing robot re-scanned the WHOLE field-wide roster itself, every tick —
    /// even a squared-distance pre-filter is still an O(n) walk of every other robot, so the total work
    /// across a frame stayed O(n²) (the ticket's own measurement: "3,600 position copies and distance
    /// tests per frame" at 60 field-wide survivors). This inverts that with an INCREMENTALLY maintained
    /// grid: <see cref="UpdatePosition"/> moves one owner between at most two buckets — O(1) amortized,
    /// called once per active robot per tick, from <c>RobotEnemy.Update</c> rather than only while
    /// chasing, so a Dormant/Telegraphing/Lunging robot still shows up at its real position for
    /// everyone else's neighbour query. <see cref="CollectNearby"/> then only ever visits the 3x3 block
    /// of cells around a query point, so its own cost scales with how crowded that neighbourhood
    /// actually is, never with the total roster size.
    ///
    /// Deliberately NOT rebuilt from scratch once per frame: EditMode drives <c>RobotEnemy.TickChase</c>
    /// by direct reflection call, with no real Unity frame loop and no reliable <c>Time.frameCount</c>
    /// advance between calls (<c>MV428MeleeReadabilityTests</c>/<c>MV434BodySeparationTests</c>/
    /// <c>RobotStandoffBandTests</c> all drive several ticks of a moving robot in a row this way) — a
    /// "rebuild once per frame" scheme would silently serve a STALE build across every one of those
    /// ticks. Each owner refreshing its own single entry on its own tick sidesteps that: correct
    /// whether ticks come from a real engine loop or a test's own manual drive.
    ///
    /// Keyed by an opaque integer id (<c>RobotEnemy.GetInstanceID()</c> in practice) rather than a
    /// direct reference, so this stays pure maths — no Unity Object, no transform, no clock — testable
    /// with plain ints and positions the same way <see cref="EnemySeparation"/> itself is.
    /// </summary>
    public sealed class SeparationGrid
    {
        private readonly float _cellSize;
        private readonly Dictionary<int, Vector3> _positionByOwner = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, (int, int)> _cellByOwner = new Dictionary<int, (int, int)>();
        private readonly Dictionary<(int, int), List<int>> _owners = new Dictionary<(int, int), List<int>>();

        /// <param name="cellSize">Edge length of one bucket, in metres — must be at least the largest
        /// <c>minDistance</c> any later <see cref="CollectNearby"/> call will use, or the 3x3
        /// neighbourhood search could miss a true neighbour sitting just across a cell boundary.</param>
        public SeparationGrid(float cellSize)
        {
            _cellSize = Mathf.Max(0.01f, cellSize);
        }

        /// <summary>
        /// Insert <paramref name="ownerId"/> if new, or move it to <paramref name="position"/>'s cell if
        /// it changed cell since the last call — O(1) amortized: touches only the old and new bucket,
        /// never the whole registry. Call once per owner per tick, regardless of that owner's own
        /// state — see this class's own doc comment for why a Dormant/Telegraphing/Lunging robot still
        /// needs to be found here.
        /// </summary>
        public void UpdatePosition(int ownerId, Vector3 position)
        {
            (int, int) newCell = CellOf(position);

            if (_cellByOwner.TryGetValue(ownerId, out (int, int) oldCell))
            {
                _positionByOwner[ownerId] = position;
                if (oldCell == newCell) return;
                _owners[oldCell].Remove(ownerId);
            }
            else
            {
                _positionByOwner[ownerId] = position;
            }

            _cellByOwner[ownerId] = newCell;
            if (!_owners.TryGetValue(newCell, out List<int> bucket))
            {
                bucket = new List<int>(4);
                _owners[newCell] = bucket;
            }
            bucket.Add(ownerId);
        }

        /// <summary>Drop <paramref name="ownerId"/> entirely — call when its owner is disabled/despawned,
        /// or it stays a phantom neighbour forever at its last-known position.</summary>
        public void Remove(int ownerId)
        {
            if (_cellByOwner.TryGetValue(ownerId, out (int, int) cell))
            {
                _owners[cell].Remove(ownerId);
                _cellByOwner.Remove(ownerId);
            }
            _positionByOwner.Remove(ownerId);
        }

        /// <summary>Drop every entry — level reset / test teardown hygiene, same idiom as
        /// <c>RobotEnemy.ResetRegistry</c>.</summary>
        public void Clear()
        {
            _positionByOwner.Clear();
            _cellByOwner.Clear();
            _owners.Clear();
        }

        /// <summary>
        /// Fills <paramref name="results"/> (cleared first) with only the tracked positions within
        /// <paramref name="minDistance"/> of <paramref name="selfPos"/>, excluding <paramref name="selfOwnerId"/>
        /// itself — the 3x3 block of cells around <paramref name="selfPos"/>'s own cell, never the whole
        /// roster. XZ-plane only, same convention as <see cref="EnemySeparation.Push"/>.
        /// </summary>
        public void CollectNearby(int selfOwnerId, Vector3 selfPos, float minDistance, List<Vector3> results)
        {
            results.Clear();
            float minDistanceSqr = minDistance * minDistance;
            (int cx, int cz) = CellOf(selfPos);

            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!_owners.TryGetValue((cx + dx, cz + dz), out List<int> bucket)) continue;

                for (int k = 0; k < bucket.Count; k++)
                {
                    int ownerId = bucket[k];
                    if (ownerId == selfOwnerId) continue;

                    Vector3 otherPos = _positionByOwner[ownerId];
                    Vector3 away = selfPos - otherPos;
                    away.y = 0f;
                    float sqr = away.sqrMagnitude;
                    if (sqr < 1e-8f || sqr >= minDistanceSqr) continue;   // same near/far floor as Push
                    results.Add(otherPos);
                }
            }
        }

        /// <summary>How many owners this grid is currently tracking — test/diagnostic only.</summary>
        public int Count => _positionByOwner.Count;

        private (int, int) CellOf(Vector3 p) =>
            (Mathf.FloorToInt(p.x / _cellSize), Mathf.FloorToInt(p.z / _cellSize));
    }
}
