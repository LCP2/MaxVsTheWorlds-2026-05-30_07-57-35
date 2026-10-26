using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Enemies
{
    /// <summary>MV-1278: an elite's optional modifier. Appended, not inserted — same rule as
    /// <see cref="RobotEnemy.State"/>/<see cref="EnemyKind"/>. Every entry ships NOT implemented by
    /// this ticket (see <see cref="EliteAffixes.IsImplemented"/>); E2-E5 each flip their own.</summary>
    public enum EliteAffix { None, Shielded, Molten, Linked, Hasted }

    /// <summary>The affix implementation registry (MV-1278) — the one place that knows whether a
    /// world-authored affix actually does anything yet. A world's <see cref="WorldDials.eliteAffixes"/>
    /// is filtered through <see cref="IsImplemented"/> before the roll (<see cref="EliteRoll"/>), so an
    /// elite whose filtered list comes up empty is a plain elite (<see cref="EliteAffix.None"/>) rather
    /// than refusing to roll at all.</summary>
    public static class EliteAffixes
    {
        /// <summary>Every affix ships false here — E2 (Shielded), E3 (Molten), E4 (Linked) and E5
        /// (Hasted) each flip their own entry true once they land.</summary>
        public static bool IsImplemented(EliteAffix affix) => false;

        public static bool TryParse(string s, out EliteAffix affix) =>
            System.Enum.TryParse(s, ignoreCase: true, out affix);
    }

    /// <summary>MV-1278: the elite roll shared by every world-source spawn path (garrison/area via
    /// <see cref="AreaAccumulationDirector"/>, factory via <see cref="EnemySpawner.SpawnKind"/>) — rolled
    /// once per spawn, by the caller, before the robot's <see cref="EnemyArchetype"/> is handed to
    /// <see cref="RobotEnemy.Apply"/> (carried on the archetype, the way <see cref="EnemyArchetype.Splicer"/>
    /// is). Never called for a boss add (<see cref="MaxWorlds.Bosses.BigBermudaBoss"/> and the other
    /// brood spawners build their own archetype and never route through this), a Sludger split
    /// (<see cref="RobotEnemy.SpawnSludgerSplit"/> goes straight through <see cref="AreaAccumulationDirector.TakeForSplit"/>,
    /// bypassing every call site that rolls) — both are "never elite" by simply never being asked.</summary>
    public static class EliteRoll
    {
        /// <summary>Resolved max health scales by this much for an elite (MV-1278 point 5) — applied by
        /// <see cref="RobotEnemy.Apply"/> and re-applied by <see cref="RobotEnemy.Retoughen"/>, on top
        /// of archetype/world-override/Toughened, never baked into <see cref="EnemyArchetype.MaxHealth"/>
        /// itself.</summary>
        public const float MaxHealthMultiplier = 2.0f;

        /// <summary>Kinds that may never roll elite (MV-1278 point 4), regardless of world/area —
        /// Turret and Dreadnought are stationary/garrison-only emplacements, Lurker's whole kit is a
        /// hide-and-strike gimmick an elite ring would spoil, Skitter is a tiny phase-hopper the ring
        /// would visually swallow. Boss brood adds, Sludger split children and a captured/converted
        /// robot are "never elite" too, but by construction — see this class's own doc comment.</summary>
        public static bool IsEligibleKind(EnemyKind kind) =>
            kind != EnemyKind.Turret && kind != EnemyKind.Dreadnought &&
            kind != EnemyKind.Lurker && kind != EnemyKind.Skitter;

        /// <summary>Rolls elite status + affix for one robot of <paramref name="kind"/> about to be
        /// spawned into <paramref name="areaIndex"/> (1-based; 0/unresolved never rolls — there is no
        /// area to cap against). Returns false (affix <see cref="EliteAffix.None"/>) when
        /// <paramref name="dials"/> is null, the kind is ineligible, the area already holds
        /// <see cref="WorldDials.eliteMaxPerArea"/> live elites (<see cref="RobotEnemy.LiveEliteCountInArea"/>),
        /// or the <see cref="WorldDials.eliteChancePct"/> roll itself misses.</summary>
        public static bool TryRoll(WorldDials dials, EnemyKind kind, int areaIndex, out EliteAffix affix)
        {
            affix = EliteAffix.None;
            if (dials == null || dials.eliteChancePct <= 0f) return false;
            if (areaIndex <= 0 || !IsEligibleKind(kind)) return false;
            if (RobotEnemy.LiveEliteCountInArea(areaIndex) >= Mathf.Max(0, dials.eliteMaxPerArea)) return false;
            if (Random.value >= dials.eliteChancePct / 100f) return false;

            affix = RollAffix(dials.eliteAffixes);
            return true;
        }

        /// <summary>Picks one IMPLEMENTED affix at random from <paramref name="authoredAffixes"/>
        /// (MV-1278 point 2) — <see cref="EliteAffix.None"/> (a plain elite) when the authored list is
        /// empty or every authored entry filters out as not-yet-implemented, which is every world's
        /// outcome today since <see cref="EliteAffixes.IsImplemented"/> is false for all four.</summary>
        private static EliteAffix RollAffix(string[] authoredAffixes)
        {
            if (authoredAffixes == null || authoredAffixes.Length == 0) return EliteAffix.None;

            var implemented = new List<EliteAffix>(authoredAffixes.Length);
            foreach (string s in authoredAffixes)
            {
                if (EliteAffixes.TryParse(s, out EliteAffix parsed) && EliteAffixes.IsImplemented(parsed))
                    implemented.Add(parsed);
            }

            return implemented.Count == 0 ? EliteAffix.None : implemented[Random.Range(0, implemented.Count)];
        }
    }
}
