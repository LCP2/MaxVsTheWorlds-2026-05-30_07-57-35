using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// MV-1311: the field-wide registry of every built <see cref="IRepairable"/> — what a Welder's repair
    /// tick (<see cref="MaxWorlds.Enemies.RobotEnemy.TickWelderRepair"/>) asks for "the nearest thing I can
    /// repair in my own area", generalizing the Welder's old hard-wired
    /// <c>AssemblyCradle.NearestDamagedInArea</c> call across both <see cref="AssemblyCradle"/> and
    /// <see cref="Pylon"/> without either knowing about the other.
    /// </summary>
    public static class RepairableRegistry
    {
        private static readonly List<IRepairable> _active = new List<IRepairable>(16);

        public static void Register(IRepairable r)
        {
            if (!_active.Contains(r)) _active.Add(r);
        }

        public static void Unregister(IRepairable r) => _active.Remove(r);

        /// <summary>Test-only reset (MV-465 idiom, same as AssemblyCradle/RobotEnemy/Pylon's own
        /// registry-clear methods) — an EditMode suite shares one physics scene for the whole cc-verify run
        /// with no per-test reset, so a repairable left behind by an earlier test would otherwise leak into
        /// a later test's "nearest" search.</summary>
        public static void ResetRegistry() => _active.Clear();

        /// <summary>The nearest repairable — below full health, or <see cref="IRepairable.IsDown"/> — in
        /// <paramref name="areaIndex"/>, within <paramref name="maxRange"/> of <paramref name="from"/>.
        /// Null if nothing in range qualifies. Every registered repairable is also a
        /// <see cref="Component"/> (both implementors today are <see cref="MonoBehaviour"/>s), so its live
        /// position comes from that cast rather than a dedicated interface member.</summary>
        public static IRepairable NearestDamagedInArea(Vector3 from, int areaIndex, float maxRange)
        {
            IRepairable best = null;
            float bestDistSqr = maxRange * maxRange;
            for (int i = 0; i < _active.Count; i++)
            {
                IRepairable r = _active[i];
                var comp = r as Component;
                if (comp == null) continue; // destroyed Unity object
                if (r.AreaIndex != areaIndex) continue;
                if (!r.IsDown && r.Normalized >= 1f) continue; // up and already full — nothing to repair
                float distSqr = (comp.transform.position - from).sqrMagnitude;
                if (distSqr <= bestDistSqr) { bestDistSqr = distSqr; best = r; }
            }
            return best;
        }
    }
}
