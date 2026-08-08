using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Concurrent-cap spawn queue for an area's population (v0.5 recut spec §2, MV-223): an area's
    /// full robot count is queued up front, but only <see cref="MaxActive"/> are ever active at
    /// once — the room's remaining allotment releases one at a time as active robots are reported
    /// destroyed, so a large area population never floods the screen.
    ///
    /// Pure + unit-testable — no MonoBehaviour, no scene wiring. Landed as engine capability only,
    /// same idiom as <see cref="MaxWorlds.Arena.AreaGate"/> (MV-222): <see cref="EnemySpawner"/> has
    /// nothing calling this yet, since there's no live area index to fill it from (MV-222's own
    /// map-cutover follow-up still has to make that call).
    /// </summary>
    public sealed class AreaSpawnQueue
    {
        private readonly Queue<EnemyKind> _queued = new Queue<EnemyKind>();

        /// <summary>The concurrent-robot ceiling this queue enforces (<c>maxActiveRobots</c>).</summary>
        public int MaxActive { get; }

        /// <summary>Robots this queue currently considers active — released but not yet reported
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

        /// <summary>
        /// Queues one area's worth of population. Large and small are interleaved proportionally to
        /// their counts (rather than large-first/small-last) so an early release — before the whole
        /// area has drained — still reflects the area's actual composition instead of being skewed
        /// to whichever kind happened to be queued first.
        /// </summary>
        public void Fill(int largeCount, int smallCount) =>
            FillInternal(largeCount, smallCount, _ => EnemyKind.Bruiser);

        /// <summary>Queues one gated-arena area's worth of population, with its large slots further
        /// split into bruiser/heavy/brute via <see cref="AreaPopulation.ToughSplitForArea"/> (v0.5
        /// recut spec §2-3, MV-224) — otherwise identical to <see cref="Fill"/>, including the
        /// proportional large/small interleave.</summary>
        public void FillForArea(int areaIndex, int largeCount, int smallCount,
            float heavyIntroArea, float bruteIntroArea, float toughSubstitutionPct)
        {
            var (bruiserCount, heavyCount, bruteCount) = AreaPopulation.ToughSplitForArea(
                areaIndex, largeCount, heavyIntroArea, bruteIntroArea, toughSubstitutionPct);

            FillInternal(largeCount, smallCount, slot =>
            {
                if (slot < bruiserCount) return EnemyKind.Bruiser;
                return slot < bruiserCount + heavyCount ? EnemyKind.Heavy : EnemyKind.Brute;
            });
        }

        /// <summary>Queues an area's worth of population from an already-solved per-type composition
        /// (MV-268's difficulty engine, <see cref="DifficultyEngine.SolveComposition"/>) — the counts
        /// are exact, not re-derived from a large/small split, so a world's own budget solver is the
        /// only thing that decided how many of each type this area gets (MV-270). Rusher/bruiser are
        /// interleaved proportionally exactly as <see cref="Fill"/>/<see cref="FillForArea"/> do; heavy
        /// and brute queue after them, heavy first — both are already rare enough that their relative
        /// order almost never matters, and neither ever competes with the small/large interleave for
        /// legibility the way rusher/bruiser do.</summary>
        public void FillExact(DifficultyEngine.Composition composition)
        {
            FillInternal(composition.Bruiser, composition.Rusher, _ => EnemyKind.Bruiser);
            for (int i = 0; i < composition.Heavy; i++) _queued.Enqueue(EnemyKind.Heavy);
            for (int i = 0; i < composition.Brute; i++) _queued.Enqueue(EnemyKind.Brute);

            // MV-293's ranged/teleport kinds (MV-310) — queued last, same as heavy/brute: both are
            // already rare enough that their order relative to the rusher/bruiser interleave (or to
            // each other) almost never matters the way that interleave itself does.
            for (int i = 0; i < composition.Gunner; i++) _queued.Enqueue(EnemyKind.Gunner);
            for (int i = 0; i < composition.Bomber; i++) _queued.Enqueue(EnemyKind.Bomber);
            for (int i = 0; i < composition.Blinker; i++) _queued.Enqueue(EnemyKind.Blinker);
        }

        private void FillInternal(int largeCount, int smallCount, Func<int, EnemyKind> largeKindForSlot)
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

                if (takeLarge) { _queued.Enqueue(largeKindForSlot(placedLarge)); placedLarge++; }
                else { _queued.Enqueue(EnemyKind.Rusher); placedSmall++; }
            }
        }

        /// <summary>Releases the next queued robot if there's room under the concurrent cap. Returns
        /// false (and leaves the queue untouched) if the cap is full or nothing is queued. The caller
        /// owns actually spawning it, and must call <see cref="ReportDestroyed"/> once it dies.</summary>
        public bool TryRelease(out EnemyKind kind)
        {
            if (ActiveCount >= MaxActive || _queued.Count == 0)
            {
                kind = default;
                return false;
            }

            kind = _queued.Dequeue();
            ActiveCount++;
            return true;
        }

        /// <summary>One active robot died — frees a slot under the cap so the next queued robot can
        /// release.</summary>
        public void ReportDestroyed()
        {
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
        }

        /// <summary>Drops everything queued and active — a fresh area starting clean.</summary>
        public void Clear()
        {
            _queued.Clear();
            ActiveCount = 0;
        }
    }
}
