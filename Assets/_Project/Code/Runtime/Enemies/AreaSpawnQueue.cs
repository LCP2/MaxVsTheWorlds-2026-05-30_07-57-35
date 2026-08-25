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

        /// <summary>Active robots this queue is currently tracking, keyed by the area they were
        /// released for (MV-417). <see cref="MaxActive"/> is enforced per-area against this, not
        /// against the field-wide <see cref="ActiveCount"/> total - see <see cref="TryExtractEligible"/>.
        /// A robot alive in an area three rooms back must never be able to block a release into the
        /// room the player is standing in, which is exactly what a single shared cap did.</summary>
        private readonly Dictionary<int, int> _activeByArea = new Dictionary<int, int>();

        /// <summary>The concurrent-robot ceiling this queue enforces, per area (<c>maxActiveRobots</c>,
        /// MV-417: was field-wide before this ticket - see <see cref="_activeByArea"/>).</summary>
        public int MaxActive { get; }

        /// <summary>Robots this queue currently considers active - released but not yet reported
        /// destroyed. Field-wide total; see <see cref="ActiveCountForArea"/> for the per-area count
        /// <see cref="MaxActive"/> is actually checked against.</summary>
        public int ActiveCount { get; private set; }

        /// <summary>Robots this queue currently considers active for one specific area - what
        /// <see cref="MaxActive"/> is enforced against (MV-417).</summary>
        public int ActiveCountForArea(int areaIndex) =>
            _activeByArea.TryGetValue(areaIndex, out int count) ? count : 0;

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
        /// Launcher/Blinker (MV-293/MV-310) - all rare enough that their relative order almost never
        /// matters the way the rusher/bruiser interleave does.</summary>
        public void FillExact(DifficultyEngine.Composition composition, int areaIndex = 0)
        {
            FillInternal(areaIndex, composition.Bruiser, composition.Rusher, _ => EnemyKind.Bruiser);
            for (int i = 0; i < composition.Heavy; i++) _queued.Enqueue(new QueuedSpawn(areaIndex, EnemyKind.Heavy));
            for (int i = 0; i < composition.Brute; i++) _queued.Enqueue(new QueuedSpawn(areaIndex, EnemyKind.Brute));
            for (int i = 0; i < composition.Gunner; i++) _queued.Enqueue(new QueuedSpawn(areaIndex, EnemyKind.Gunner));
            for (int i = 0; i < composition.Launcher; i++) _queued.Enqueue(new QueuedSpawn(areaIndex, EnemyKind.Launcher));
            for (int i = 0; i < composition.Blinker; i++) _queued.Enqueue(new QueuedSpawn(areaIndex, EnemyKind.Blinker));
            for (int i = 0; i < composition.Bolter; i++) _queued.Enqueue(new QueuedSpawn(areaIndex, EnemyKind.Bolter));
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
        /// owns actually spawning it, and must call <see cref="ReportDestroyed(int)"/> once it dies.</summary>
        public bool TryRelease(out EnemyKind kind) => TryRelease(out _, out kind);

        /// <summary>Same as <see cref="TryRelease(out EnemyKind)"/>, but also hands back the area the
        /// released robot was originally queued for (MV-417) - the caller must place it there, not
        /// wherever it currently considers "current", or a room emptied by the concurrent cap can end
        /// up permanently unfilled from the player's point of view while its allotment materialises in
        /// whatever room the player has since walked into. Scans past a queued entry whose own area is
        /// already at <see cref="MaxActive"/> rather than blocking outright on it (MV-417) - a capped-out
        /// area three rooms back must not stall every other area's release just because it happens to
        /// sit earlier in the FIFO.</summary>
        public bool TryRelease(out int area, out EnemyKind kind) => TryExtractEligible(null, out area, out kind);

        /// <summary>Releases the next queued robot originally queued for <paramref name="areaIndex"/>
        /// specifically, still subject to that area's own <see cref="MaxActive"/> cap (MV-417) - lets a
        /// caller top up exactly the room it cares about (e.g. the one just filled, or the one the
        /// player is standing in) without disturbing any other area's queued backlog or release order.
        /// False if nothing is queued for that area, or that area is already at its own cap.</summary>
        public bool TryReleaseArea(int areaIndex, out EnemyKind kind) => TryExtractEligible(areaIndex, out _, out kind);

        /// <summary>Scans the queue in FIFO order for the first entry that is both a match for
        /// <paramref name="filterArea"/> (any area, if null) AND whose own area is currently under
        /// <see cref="MaxActive"/>, extracts it, and marks it active for that area. Entries skipped
        /// along the way (wrong area, or a match whose area is capped) are put back in their original
        /// relative order - this is a targeted extraction, not a reorder of the queue.</summary>
        private bool TryExtractEligible(int? filterArea, out int area, out EnemyKind kind)
        {
            int count = _queued.Count;
            for (int i = 0; i < count; i++)
            {
                QueuedSpawn next = _queued.Dequeue();
                if ((filterArea == null || next.Area == filterArea.Value) && ActiveCountForArea(next.Area) < MaxActive)
                {
                    area = next.Area;
                    kind = next.Kind;
                    Activate(area);
                    return true;
                }
                _queued.Enqueue(next);
            }

            area = 0;
            kind = default;
            return false;
        }

        /// <summary>Takes the next queued robot for <paramref name="areaIndex"/> straight out of the
        /// queue and marks it active, ignoring <see cref="MaxActive"/> entirely (MV-417) - a garrison
        /// seed must be guaranteed present the instant an area is first entered (or restored),
        /// independent of whatever concurrent-cap state the ambient top-up queue happens to be in. The
        /// caller is expected to place exactly <see cref="Garrison.SeedCount"/> of these per area, which
        /// is always at most that area's authored total, so this can never run the queue dry on its
        /// own.</summary>
        public bool TryTakeForGarrison(int areaIndex, out EnemyKind kind)
        {
            int count = _queued.Count;
            for (int i = 0; i < count; i++)
            {
                QueuedSpawn next = _queued.Dequeue();
                if (next.Area == areaIndex)
                {
                    kind = next.Kind;
                    Activate(areaIndex);
                    return true;
                }
                _queued.Enqueue(next);
            }

            kind = default;
            return false;
        }

        /// <summary>Same as <see cref="TryTakeForGarrison(int, out EnemyKind)"/>, but for an authored
        /// garrison slot (MV-559) that names a specific kind: takes THAT kind out of the queue rather
        /// than whatever is next, so an authored Blinker spot actually spawns a Blinker. Falls back to
        /// the ordinary any-kind behaviour above when <paramref name="requestedKind"/> is null (an
        /// unauthored/ring slot) or that kind is not queued for this area (a content mismatch — still
        /// places a robot rather than leaving the authored spot empty).</summary>
        public bool TryTakeForGarrison(int areaIndex, EnemyKind? requestedKind, out EnemyKind kind)
        {
            if (requestedKind.HasValue && TryTakeExactKind(areaIndex, requestedKind.Value, out kind)) return true;
            return TryTakeForGarrison(areaIndex, out kind);
        }

        private bool TryTakeExactKind(int areaIndex, EnemyKind requestedKind, out EnemyKind kind)
        {
            int count = _queued.Count;
            for (int i = 0; i < count; i++)
            {
                QueuedSpawn next = _queued.Dequeue();
                if (next.Area == areaIndex && next.Kind == requestedKind)
                {
                    kind = next.Kind;
                    Activate(areaIndex);
                    return true;
                }
                _queued.Enqueue(next);
            }

            kind = default;
            return false;
        }

        /// <summary>Puts a released-but-not-yet-placed entry back on the queue for <paramref name="areaIndex"/>
        /// (MV-417) - what a caller does when it pulled a robot off the queue but then couldn't find it
        /// a legal spawn point this tick (see <c>AreaAccumulationDirector.TryFindSpawnPoint</c>) and
        /// wants to retry on the next release interval, rather than either placing it somewhere wrong or
        /// silently losing it from the area's roster.</summary>
        public void Requeue(int areaIndex, EnemyKind kind)
        {
            _queued.Enqueue(new QueuedSpawn(areaIndex, kind));
            Deactivate(areaIndex);
        }

        private void Activate(int areaIndex)
        {
            ActiveCount++;
            _activeByArea[areaIndex] = ActiveCountForArea(areaIndex) + 1;
        }

        private void Deactivate(int areaIndex)
        {
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
            if (_activeByArea.TryGetValue(areaIndex, out int count))
                _activeByArea[areaIndex] = Mathf.Max(0, count - 1);
        }

        /// <summary>One active robot died - frees a slot under its area's cap so the next entry queued
        /// for that area can release. Area 0 for a caller that doesn't track per-area origin (e.g. a
        /// unit test exercising this queue directly via <see cref="Fill"/>).</summary>
        public void ReportDestroyed(int areaIndex = 0) => Deactivate(areaIndex);

        /// <summary>Drops everything queued and active - a fresh area starting clean.</summary>
        public void Clear()
        {
            _queued.Clear();
            ActiveCount = 0;
            _activeByArea.Clear();
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
