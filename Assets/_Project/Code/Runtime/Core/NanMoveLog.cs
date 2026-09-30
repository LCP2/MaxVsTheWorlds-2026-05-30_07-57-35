using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// MV-1043: the NaN firewall's own evidence trail. World 1 a23's mobile shed went non-finite on
    /// Lee's TestFlight device with nothing recorded on disk to say where from (the same gap MV-1039
    /// closed for falls) -- every refusal <see cref="CharacterControllerMotion.SafeMove"/> and the
    /// direct-write guards make now writes ONE row per object per <see cref="RepeatWindowSeconds"/>
    /// through the same no-op-without-a-recorder events-CSV contract <c>WaterBlaster</c>'s MVHIT and
    /// <see cref="CharacterControllerSafety.LogRefusal"/>'s cct-refused rows already use. Rate-limited
    /// per object name so a mover stuck feeding a non-finite value every frame writes evidence once,
    /// not a CSV-sized flood.
    /// </summary>
    public static class NanMoveLog
    {
        private const float RepeatWindowSeconds = 10f;

        private static readonly Dictionary<string, float> _lastLoggedAt = new Dictionary<string, float>();

        /// <summary>Test hygiene — EditMode tests run in one shared process/session.</summary>
        public static void Reset() => _lastLoggedAt.Clear();

        /// <summary>Records one <c>nan-move</c> evidence row for <paramref name="objectName"/>, unless
        /// this exact object already logged one within <see cref="RepeatWindowSeconds"/>.
        /// <paramref name="extra"/> carries whatever additional context the caller has to hand (MowerHutch
        /// and RobotEnemy pass their own state/target position; a generic SafeMove call site has none).</summary>
        public static void Record(string objectName, string site, Vector3 displacement, Vector3 positionBefore,
            float dt, string extra = null)
        {
            float now = Time.realtimeSinceStartup;
            if (_lastLoggedAt.TryGetValue(objectName, out float last) && now - last < RepeatWindowSeconds)
                return;
            _lastLoggedAt[objectName] = now;

            string context = $"site={site} object={objectName} displacement={displacement} " +
                $"positionBefore={positionBefore} dt={dt:F4}" +
                (string.IsNullOrEmpty(extra) ? string.Empty : $" {extra}");

            Debug.LogWarning($"[NanMoveLog] {context}");
            Bootstrap.ActiveSessionRecorder?.RecordEvent(DateTime.UtcNow, "nan-move", context);
        }

        /// <summary>Guards a direct <c>transform.position =</c> write: writes <paramref name="newPosition"/>
        /// only if it's finite, otherwise logs and leaves <paramref name="t"/> untouched. The shared idiom
        /// for every raw position write this ticket found bypassing <see cref="CharacterControllerMotion.SafeMove"/>
        /// / <see cref="CharacterControllerSafety.SafeReposition"/> (MowerHutch's ClampToGroundY/
        /// ApplyDestructionEffects, RobotEnemy's ClampToDeckFootprint/TickReplicatorSeeking stall step).</summary>
        public static bool GuardedWrite(Transform t, Vector3 newPosition, string objectName, string site,
            float dt, string extra = null)
        {
            if (CharacterControllerSafety.IsFinite(newPosition))
            {
                t.position = newPosition;
                return true;
            }

            Record(objectName, site, Vector3.zero, t.position, dt, extra);
            return false;
        }
    }
}
