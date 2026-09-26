using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;

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

        public static IReadOnlyList<FallEventRecord> Events => _events;

        /// <summary>Test hygiene — EditMode tests run in one shared process/session.</summary>
        public static void Reset() => _events.Clear();

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
