using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// MV-955: Lee's World 1 g20 fall-through-the-world could not be reproduced in EditMode, and he plays
    /// on TestFlight with no console open. Rather than ask him to reproduce it with one, every entity's
    /// own <see cref="FallRecoveryState"/> recovery (Max, a Sentinel, a robot — MV-946/MV-952) now writes
    /// one line here, into a small ring buffer, the instant it fires — the next live fall names its own
    /// cause. <see cref="MaxWorlds.Dev.Mv503DiagnosticOverlay"/> renders the buffer as a "FALLS" section;
    /// <see cref="Record"/> also <see cref="Debug.LogWarning"/>s each line so it shows up in a captured
    /// device log too.
    ///
    /// MV-1039: Lee's World 1 a22 fall/bounce (no shed nearby, weakening MV-1022's shed hypothesis) went
    /// entirely unrecorded on disk — this ring buffer and its log line are read only if someone is
    /// watching at the time. <see cref="Record"/> now also writes ONE row per fall to the session events
    /// CSV (<see cref="Bootstrap.ActiveSessionRecorder"/>, same no-op-without-a-recorder contract as
    /// <c>WaterBlaster</c>'s MVHIT row), carrying two ground-truth <see cref="Physics.Raycast"/> floor
    /// probes so the next live fall names its own cause without anyone reproducing it first.
    /// </summary>
    public readonly struct FallEventRecord
    {
        public readonly string EntityKind;
        public readonly string AreaId;
        public readonly Vector3 FirstOutOfPlayPosition;
        public readonly Vector3 LastGroundedPosition;
        public readonly float DeltaTime;
        public readonly int MaxSubStepCount;
        public readonly float MaxSubStepSize;
        public readonly bool OversizedMoveDetected;
        public readonly string NearestGateId;

        public FallEventRecord(string entityKind, string areaId, Vector3 firstOutOfPlayPosition,
            Vector3 lastGroundedPosition, float deltaTime, int maxSubStepCount, float maxSubStepSize,
            bool oversizedMoveDetected, string nearestGateId)
        {
            EntityKind = entityKind;
            AreaId = areaId;
            FirstOutOfPlayPosition = firstOutOfPlayPosition;
            LastGroundedPosition = lastGroundedPosition;
            DeltaTime = deltaTime;
            MaxSubStepCount = maxSubStepCount;
            MaxSubStepSize = maxSubStepSize;
            OversizedMoveDetected = oversizedMoveDetected;
            NearestGateId = nearestGateId;
        }
    }

    public static class FallEventLog
    {
        /// <summary>The ticket's own "last 8".</summary>
        public const int Capacity = 8;

        /// <summary>The ticket's own "nearest gate id within 5 m".</summary>
        public const float GateSearchRadius = 5f;

        // Same "fixed capacity List, RemoveAt(0) then Add" idiom as Mv503DiagnosticOverlay._lines --
        // no reallocation once the list has grown to Capacity, so a recovered fall costs one shift-and-
        // append, never a per-frame allocation (a fall itself is already the rare case this only runs on).
        private static readonly List<FallEventRecord> _events = new List<FallEventRecord>(Capacity);

        /// <summary>MV-1039: recent fall timestamps per entity kind — the CSV row's own "bounce-loop
        /// signature" (a running count of how many FALL rows THIS entity has written in the last
        /// <see cref="RecentWindowSeconds"/>). <see cref="Time.realtimeSinceStartup"/>, not
        /// <see cref="Time.time"/> — the same clock <see cref="MaxWorlds.Dev.Mv503DiagnosticOverlay"/>'s
        /// own cache already uses, and one that actually advances in EditMode (a batch-mode test process
        /// never enters Play mode).</summary>
        private static readonly Dictionary<string, List<float>> _recentFallTimesByEntity = new Dictionary<string, List<float>>();

        private const float RecentWindowSeconds = 10f;

        public static IReadOnlyList<FallEventRecord> Events => _events;

        /// <summary>Test hygiene — EditMode tests run in one shared process/session.</summary>
        public static void Reset()
        {
            _events.Clear();
            _recentFallTimesByEntity.Clear();
        }

        /// <summary>Called the instant a <see cref="FallRecoveryState"/>'s own <c>Tick</c> returns a
        /// recovery position — never per frame otherwise (AC3's allocation guard).</summary>
        public static void Record(string entityKind, MapData map, Vector3 firstOutOfPlayPosition,
            Vector3 lastGroundedPosition, float dt)
        {
            string areaId = map?.ZoneAt(lastGroundedPosition.x, lastGroundedPosition.z)?.id ?? "unknown";
            string gateId = NearestGateId(map, firstOutOfPlayPosition) ?? "none";

            var record = new FallEventRecord(
                entityKind,
                areaId,
                firstOutOfPlayPosition,
                lastGroundedPosition,
                dt,
                CharacterControllerMotion.LargestRecentSubStepCount(),
                CharacterControllerMotion.LargestRecentSubStepSize(),
                CharacterControllerMotion.AnyRecentOversizedMove(),
                gateId);

            if (_events.Count >= Capacity) _events.RemoveAt(0);
            _events.Add(record);

            Debug.LogWarning(FormatLine(record));

            // MV-1039: same no-op-without-a-recorder contract as WaterBlaster's MVHIT row -- skip
            // building the context string (two raycasts, a scene lookup) entirely when nothing is
            // listening, rather than throwing the result away.
            PerfSessionRecorder recorder = Bootstrap.ActiveSessionRecorder;
            if (recorder != null)
                recorder.RecordEvent(DateTime.UtcNow, "FALL", BuildEventContext(record, areaId, gateId));
        }

        private const float ProbeUpOffsetPrimary = 1f;
        private const float ProbeUpOffsetSecondary = 3f;
        private const float ProbeDownDistance = 4f;

        /// <summary>MV-1039: the ticket's own two ground-truth checks -- "is there actually a floor
        /// collider under the recover-to point" and "under the point Max first left the playable area".
        /// All layers, triggers ignored (a trigger volume is never what holds Max up), so this answers
        /// "is there real, solid ground here", not "does anything overlap this point at all".</summary>
        private static string ProbeFloor(Vector3 basePosition, float upOffset)
        {
            Vector3 origin = basePosition + Vector3.up * upOffset;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, ProbeDownDistance, ~0,
                    QueryTriggerInteraction.Ignore))
                return "none";

            Collider c = hit.collider;
            return $"{c.name}(enabled={c.enabled},trigger={c.isTrigger},active={c.gameObject.activeInHierarchy}," +
                   $"y={hit.point.y:F2},layer={LayerMask.LayerToName(c.gameObject.layer)})";
        }

        /// <summary>MV-1039: the running "how many times has THIS entity fallen in the last 10s" count
        /// -- the bounce-loop signature Lee's a22 report described ("fell through the floor and bounced
        /// up and down again"). Mutates <see cref="_recentFallTimesByEntity"/>, so this must be called
        /// exactly once per recorded fall.</summary>
        private static int RecordAndCountRecentFalls(string entityKind)
        {
            float now = Time.realtimeSinceStartup;
            if (!_recentFallTimesByEntity.TryGetValue(entityKind, out List<float> times))
            {
                times = new List<float>();
                _recentFallTimesByEntity[entityKind] = times;
            }

            times.Add(now);
            times.RemoveAll(t => now - t > RecentWindowSeconds);
            return times.Count;
        }

        private static string BuildEventContext(in FallEventRecord r, string areaId, string gateId)
        {
            var director = UnityEngine.Object.FindFirstObjectByType<AreaAccumulationDirector>();
            string areaTracker = director != null
                ? $"currentArea={director.CurrentArea} physicalArea={director.PhysicalArea}"
                : "currentArea=none physicalArea=none";
            bool gateActive = MapStaticBatchRoot.Active != null && MapStaticBatchRoot.Active.IsZoneActive(areaId);

            return $"entity={r.EntityKind} area={areaId} gate={gateId} " +
                   $"firstOut={r.FirstOutOfPlayPosition:F2} lastGrounded={r.LastGroundedPosition:F2} " +
                   $"dt={r.DeltaTime:F3} subSteps={r.MaxSubStepCount}@{r.MaxSubStepSize:F2}m oversized={r.OversizedMoveDetected} " +
                   $"floorProbe1={ProbeFloor(r.LastGroundedPosition, ProbeUpOffsetPrimary)} " +
                   $"floorProbe2={ProbeFloor(r.FirstOutOfPlayPosition, ProbeUpOffsetSecondary)} " +
                   $"{areaTracker} gateActive={gateActive} " +
                   $"recentFalls10s={RecordAndCountRecentFalls(r.EntityKind)}";
        }

        /// <summary>Nearest <see cref="EntityKind.Gate"/>/<see cref="EntityKind.AreaGate"/> entity to
        /// <paramref name="position"/>, XZ-plane only (map entities carry no Y), or null if none sits
        /// within <see cref="GateSearchRadius"/>.</summary>
        private static string NearestGateId(MapData map, Vector3 position)
        {
            if (map?.entities == null) return null;

            string best = null;
            float bestDistSq = GateSearchRadius * GateSearchRadius;
            foreach (MapEntity e in map.entities)
            {
                if (e == null) continue;
                EntityKind kind = e.Kind;
                if (kind != EntityKind.Gate && kind != EntityKind.AreaGate) continue;

                float dx = e.x - position.x;
                float dz = e.z - position.z;
                float distSq = dx * dx + dz * dz;
                if (distSq > bestDistSq) continue;

                bestDistSq = distSq;
                best = e.id;
            }

            return best;
        }

        public static string FormatLine(in FallEventRecord r) =>
            $"[MV-955] {r.EntityKind} fell in {r.AreaId} at {r.FirstOutOfPlayPosition.ToString("F1")} " +
            $"(last grounded {r.LastGroundedPosition.ToString("F1")}, dt={r.DeltaTime:F3}s, " +
            $"subSteps={r.MaxSubStepCount}@{r.MaxSubStepSize:F2}m, oversizedMove={r.OversizedMoveDetected}, " +
            $"nearestGate={r.NearestGateId})";
    }
}
