using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// MV-1286: the one shared "is this point inside a Jammer's field" query —
    /// <see cref="MaxWorlds.Weapons.PlayerAbilities"/>'s triggered abilities (Force Field, Trap,
    /// Sentinel deploy, Teleport, Water Balloon) and <see cref="MaxWorlds.Arena.Sentinel"/>'s own
    /// fire gate both read this rather than each re-deriving the same registry walk. True within
    /// <see cref="JamRadius"/> (flat XZ, same <see cref="CombatLevel"/>) of any live, awake,
    /// un-captured <see cref="EnemyKind.Jammer"/> — the same "Team == Team.Enemy" capture test
    /// <see cref="RobotEnemy.HasDome"/> already uses for the Shield Projector dome. Max's primary
    /// weapon and auto-firing secondaries never read this (ticket item 4) — only the Try* abilities
    /// and Sentinel.TickSentinel's fire gate call in.
    /// </summary>
    public static class AbilityJam
    {
        public const float JamRadius = 6f;

        public static bool IsJammedAt(Vector3 position)
        {
            MapData map = EnemyNavigation.Map;
            IReadOnlyList<RobotEnemy> active = RobotEnemy.Active;
            float radiusSq = JamRadius * JamRadius;

            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy r = active[i];
                if (r == null || r.Kind != EnemyKind.Jammer) continue;
                if (!r.IsAwake || r.Team != Team.Enemy) continue;
                if (!CombatLevel.SameLevel(map, position, r.transform.position)) continue;

                float dx = r.transform.position.x - position.x;
                float dz = r.transform.position.z - position.z;
                if (dx * dx + dz * dz <= radiusSq) return true;
            }
            return false;
        }
    }
}
