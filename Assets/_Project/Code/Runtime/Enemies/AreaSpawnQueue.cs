using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Concurrent-cap spawn queue for an area's population (v0.5 recut spec section 2, MV-223): an
    /// area's full robot count is queued up front, but only <see cref="MaxActive"/> are ever active
    /// at once - the room's remaining allotment releases one at a time as active robots are reported
    /// destroyed, so a large area population never floods the screen.
    ///
    /// Pure + unit-testable - no MonoBehaviour, no scene wiring.
    /// </summary>
    public sealed class AreaSpawnQueue
    {
        /// <summary>One queued robot, tagged with the area it was queued for (MV-417). An entry that
        /// overflows past <see cref="MaxActive"/> at fill time can release many areas later, once the
        /// caller has moved on to a different area - without this tag the caller has no way to place
        /// it back in the room it actually belongs to, and would default to wherever it currently
        /// considers "current", leaving the original room looking permanently empty.</summary>
        private readonly struct QueuedSpawn
        {
            public readonly int Area;
            public readonly EnemyKind Kind;
            public QueuedSpawn(int area, EnemyKind kind) { Area = area; Kind = kind; }
        }

        private readonly Queue<QueuedSpawn> _queued = new Queue<QueuedSpawn>();

        /// <summary>The concurrent-robot ceiling this queue enforces (<c>maxActiveRobots</c>).</summary>
        public int MaxActive { get; }

        /// <summary>Robots this queue currently considers active - released but not yet reported
        /// destroyed.</summary>
        public int ActiveCount { get; private set; }

        /// <summary>Robots still waiting for a slot under <see cref="MaxActive"/>.</summary>
        public int QueuedCount => _queued.Count;

        /// <summary>Everything this area still has left to put on the field, active or not.</summary>
        public int TotalRemaining => ActiveCount + _queued.Count;

        public AreaSpawnQueue(int maxActive)
        {
            MaxActive = Mathf.Max(1, maxActive);
        }

        /// <summary>Queues one area's worth of population, tagged with <paramref name="areaIndex"/>
        /// (0 - no real area - when a caller exercises this queue in isolation, e.g. a unit test).
        /// Large and small are interleaved proportionally to their counts (rather than
        /// large-first/small-last) so an early release, before the whole area has drained, still
        /// reflects the area's actual composition instead of being skewed to whichever kind happened
        /// to be queued first.</summary>
        public void Fill(int largeCount, int smallCount, int areaIndex = 0) =>
            FillInternal(areaIndex, largeCount, smallCount, _ => EnemyKind.Bruiser);

        /// <summary>Queues one gated-arena area's worth of population, with its large slots further
        /// split into bruiser/heavy/brute via <see cref="AreaPopulation.ToughSplitForArea"/> (v0.5
        /// recut spec section 2-3, MV-224) - otherwise identical to <see cref="Fill"/>, including the
        /// proportional large/small interleave.</summary>
        public void FillForArea(int areaIndex, int largeCount, int smallCount,
            float heavyIntroArea, float bruteIntroArea, float toughSubstitutionPct)
        {
            var split = AreaPopulation.ToughSplitForArea(
                areaIndex, largeCount, heavyIntroArea, bruteIntroArea, toughSubstitutionPct);
            int bruiserCount = split.Item1;
            int heavyCount = split.Item2;

            FillInternal(areaIndex, largeCount, smallCount, slot =>
            {
                if (slot < bruiserCount) return EnemyKind.Bruiser;
                return slot < bruiserCount + heavyCount ? EnemyKind.Heavy : EnemyKind.Brute;
            });
        }

        /// <summary>Queues an area's worth of population from an already-solved per-type composition
        /// (MV-268's difficulty engine) - the counts are exact, not re-derived from a large/small
        /// split. Rusher/bruiser are interleaved proportionally exactly as <see cref="Fill"/>/
        /// <see cref="FillForArea"/> do; heavy and brute queue after them, heavy first, then Gunner/
        /// Bomber/Blinker (MV-293/MV-310) - all rare enough that their relative order almost never
        /// matters the way the rusher/bruiser interleave does.</summary>
        public void FillExact(DifficultyEngine.Composition composition, int areaIndex = 0)
        {
            FillInternal(areaIndex, composition.Bruiser, composition.Rusher, _ => EnemyKind.Bruiser);
            for (int i = 0; i < composition.Heavy; i++) _queued.Enqueue(new QueuedSpawn(areaIndex, EnemyKind.Heavy));
            for (int i = 0; i < composition.Brute; i++) _queued.Enqueue(new QueuedSpawn(areaIndex, EnemyKind.Brute));
            for (int i = 0; i < composition.Gunner; i++) _queued.Enqueue(new QueuedSpawn(areaIndex, EnemyKind.Gunner));
            for (int i = 0; i < composition.Bomber; i++) _queued.Enqueue(new QueuedSpawn(areaIndex, EnemyKind.Bomber));
            for (int i = 0; i < composition.Blinker; i++) _queued.Enqueue(new QueuedSpawn(areaIndex, EnemyKind.Blinker));
        }

        private void FillInternal(int areaIndex, int largeCount, int smallCount, Func<int, EnemyKind> largeKindForSlot)
        {
            int large = Mathf.Max(0, largeCount);
            int small = Mathf.Max(0, smallCount);
            int placedLarge = 0, placedSmall = 0;

            for (int i = 0; i < large + small; i++)
            {
                bool takeLarge;
                if (placedLarge >= large) takeLarge = false;
                else if (placedSmall >= small) takeLarge = true;
                else takeLarge = (placedLarge + 1) * small <= (placedSmall + 1) * large;

                if (takeLarge) { _queued.Enqueue(new QueuedSpawn(areaIndex, largeKindForSlot(placedLarge))); placedLarge++; }
                else { _queued.Enqueue(new QueuedSpawn(areaIndex, EnemyKind.Rusher)); placedSmall++; }
            }
        }

        /// <summary>Releases the next queued robot if there is room under the concurrent cap. Returns
        /// false (and leaves the queue untouched) if the cap is full or nothing is queued. The caller
        /// owns actually spawning it, and must call <see cref="ReportDestroyed"/> once it dies.</summary>
        public bool TryRelease(out EnemyKind kind) => TryRelease(out _, out kind);

        /// <summary>Same as <see cref="TryRelease(out EnemyKind)"/>, but also hands back the area the
        /// released robot was originally queued for (MV-417) - the caller must place it there, not
        /// wherever it currently considers "current", or a room emptied by the concurrent cap can end
        /// up permanently unfilled from the player's point of view while its allotment materialises in
        /// whatever room the player has since walked into.</summary>
        public bool TryRelease(out int area, out EnemyKind kind)
        {
            if (ActiveCount >= MaxActive || _queued.Count == 0)
            {
                area = 0;
                kind = default;
                return false;
            }

            QueuedSpawn next = _queued.Dequeue();
            area = next.Area;
            kind = next.Kind;
            ActiveCount++;
            return true;
        }

        /// <summary>One active robot died - frees a slot under the cap so the next queued robot can
        /// release.</summary>
        public void ReportDestroyed()
        {
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
        }

        /// <summary>Drops everything queued and active - a fresh area starting clean.</summary>
        public void Clear()
        {
            _queued.Clear();
            ActiveCount = 0;
        }

        /// <summary>Drops every entry still queued (not yet released) for one area, leaving every
        /// other area's queued backlog and the global <see cref="ActiveCount"/> untouched (MV-427: an
        /// area reset on death must not disturb an earlier area's own leftover overflow). Active
        /// robots already released for this area are the caller's job to remove and report destroyed
        /// individually (each one flows back through <see cref="ReportDestroyed"/>) — this only clears
        /// the not-yet-placed backlog.</summary>
        public void RemoveQueued(int areaIndex)
        {
            int count = _queued.Count;
            for (int i = 0; i < count; i++)
            {
                QueuedSpawn next = _queued.Dequeue();
                if (next.Area != areaIndex) _queued.Enqueue(next);
            }
        }
    }
}
