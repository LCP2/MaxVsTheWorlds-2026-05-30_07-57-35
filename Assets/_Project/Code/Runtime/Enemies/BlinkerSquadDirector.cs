using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Coordinates the Blinker group jump (MV-366): drafts a small knot of eligible Blinkers
    /// (<see cref="RobotEnemy.IsEligibleForGroupTeleport"/>) and sends them all to one shared
    /// destination computed by <see cref="BlinkerSquadTeleport"/>, on a cooldown loose enough to keep
    /// it reading as a moment rather than a mechanic on a metronome. Solo blinks (<see cref="RobotEnemy.TickChase"/>'s
    /// own per-robot check) are untouched — this only ever drafts robots that were already eligible to
    /// blink on their own.
    ///
    /// Global and static, the same shape as <see cref="DifficultyDirector"/>: there is exactly one
    /// squad clock for a run. <see cref="BlinkerSquadDirectorRunner"/> is the only thing that calls
    /// <see cref="Tick"/>, once per frame.
    /// </summary>
    public static class BlinkerSquadDirector
    {
        /// <summary>How long between successful squad jumps, picked fresh (not fixed) each time so
        /// the beat never reads as scheduled.</summary>
        public const float MinCooldown = 9f;
        public const float MaxCooldown = 16f;

        /// <summary>How soon to check again after a tick found nothing to draft — short enough that a
        /// squad forms the moment enough Blinkers are chasing at once, without scanning every frame.</summary>
        public const float RetryInterval = 2f;

        /// <summary>The range a freshly-landed squad commits from — the same fraction of melee range
        /// the solo blink lands at (see <see cref="RobotEnemy.LungeRange"/> and <c>TickChase</c>'s own
        /// <c>lungeRange * 0.85f</c>), read off the first drafted robot since every participant is the
        /// same archetype.</summary>
        private const float PreferredRangeFraction = 0.85f;

        private static float _cooldown;
        private static readonly List<RobotEnemy> _participants = new List<RobotEnemy>(BlinkerSquadTeleport.MaxGroupSize);
        private static readonly List<float> _distances = new List<float>(32);
        private static readonly List<bool> _isParticipant = new List<bool>(32);

        /// <summary>Fresh cooldown for a new run — called alongside every other per-run reset
        /// (<c>MapRuntime.Build</c>) so a squad jump can't fire in the first instant of a level off
        /// whatever the last run's clock happened to leave behind.</summary>
        public static void Reset() => _cooldown = MinCooldown;

        /// <summary>Advance the cooldown and, once it lapses, try to draft and launch a squad jump at
        /// <paramref name="targetPos"/> (Max's current position). Negative/garbage dt is clamped to
        /// zero, same convention as <see cref="DifficultyDirector.Tick"/>.</summary>
        public static void Tick(float dt, Vector3 targetPos)
        {
            _cooldown -= Mathf.Max(0f, dt);
            if (_cooldown > 0f) return;

            _cooldown = TryLaunch(targetPos)
                ? UnityEngine.Random.Range(MinCooldown, MaxCooldown)
                : RetryInterval;
        }

        private static bool TryLaunch(Vector3 targetPos)
        {
            _participants.Clear();
            _distances.Clear();
            _isParticipant.Clear();

            IReadOnlyList<RobotEnemy> active = RobotEnemy.Active;
            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy r = active[i];
                float dist = Vector3.Distance(Flatten(r.transform.position), Flatten(targetPos));
                bool eligible = r.IsEligibleForGroupTeleport(targetPos);

                _distances.Add(dist);
                _isParticipant.Add(eligible);
                if (eligible) _participants.Add(r);
            }

            if (_participants.Count < BlinkerSquadTeleport.MinGroupSize) return false;

            float nearestPackDistance = BlinkerSquadTeleport.NearestPackDistance(_distances, _isParticipant);
            if (!BlinkerSquadTeleport.CanLandCloserThanPack(nearestPackDistance)) return false;

            // Closest-to-Max eligible Blinkers first — the ones already leading the charge form the
            // squad, not a random draw from wherever the rest of the eligible pool happens to be.
            _participants.Sort((a, b) =>
                Vector3.Distance(Flatten(a.transform.position), Flatten(targetPos))
                    .CompareTo(Vector3.Distance(Flatten(b.transform.position), Flatten(targetPos))));
            int groupSize = Mathf.Min(_participants.Count, BlinkerSquadTeleport.MaxGroupSize);

            Vector3 packPos = Vector3.zero;
            for (int i = 0; i < groupSize; i++) packPos += _participants[i].transform.position;
            packPos /= groupSize;

            float preferred = _participants[0].LungeRange * PreferredRangeFraction;
            float distance = BlinkerSquadTeleport.LandingDistance(nearestPackDistance, preferred);
            float sign = UnityEngine.Random.value < 0.5f ? 1f : -1f;
            Vector3 destination = BlinkerSquadTeleport.GroupFlankPoint(targetPos, packPos, distance, sign);

            int launched = 0;
            for (int i = 0; i < groupSize; i++)
                if (_participants[i].TryBeginGroupTeleport(destination)) launched++;

            return launched >= BlinkerSquadTeleport.MinGroupSize;
        }

        private static Vector3 Flatten(Vector3 v) { v.y = 0f; return v; }
    }
}
