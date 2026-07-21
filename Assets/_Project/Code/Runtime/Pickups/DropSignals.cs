using System;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Pickups
{
    /// <summary>
    /// The drop bus (YT-131). A robot announces its death here with its kind and position, and the
    /// <see cref="PickupDirector"/> decides what — if anything — falls out of it. Keeping the policy
    /// ("which robots drop, and what") in the director rather than in <c>RobotEnemy.Die</c> means the
    /// enemy doesn't need to know pickups exist, and the drop table (YT-133) has one place to grow.
    ///
    /// Null-safe like <c>HudSignals</c>: emitting with nobody listening (a headless test with no
    /// director) is a no-op.
    /// </summary>
    public static class DropSignals
    {
        /// <summary>A robot died. Args: where it died, and which archetype it was.</summary>
        public static event Action<Vector3, EnemyKind> RobotDied;

        public static void EmitRobotDied(Vector3 position, EnemyKind kind) =>
            RobotDied?.Invoke(position, kind);
    }
}
