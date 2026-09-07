using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// The Pipe Turret's Toxic status effect (MV-691, the GDD's "Toxic"): standing in its coolant
    /// puddle (<see cref="CorrosionPuddle"/>) marks the receiver CORRODED — incoming damage taken at
    /// <see cref="DamageMultiplier"/> for <see cref="Duration"/> seconds after leaving it. Pure timer
    /// maths, shared by <see cref="MaxWorlds.Player.PlayerHealth"/> and <see cref="RobotEnemy"/> (the
    /// ticket's own "Max (and robots)"), so the resolved remaining-time/multiplier either receiver
    /// reads is provably the same rule, not two copies that could drift apart.
    /// </summary>
    public static class CorrodedStatus
    {
        public const float Duration = 4f;
        public const float DamageMultiplier = 1.25f;

        /// <summary>Refreshes the status to a full duration — standing in a second puddle (or a
        /// second glob) never STACKS the multiplier, it just resets the clock, same "never shortens,
        /// only ever refreshes to the authored ceiling" idiom as <see cref="RobotEnemy.ApplyHalt"/>.</summary>
        public static float Refresh() => Duration;

        public static float Tick(float timer, float dt) => Mathf.Max(0f, timer - dt);

        public static bool IsActive(float timer) => timer > 0f;

        /// <summary>What <see cref="RobotEnemy.TakeDamage"/>/<see cref="MaxWorlds.Player.PlayerHealth.TakeDamage"/>
        /// actually multiply an incoming hit by — 1x while inactive, never authored as a bare 1.25
        /// constant at either call site.</summary>
        public static float MultiplierFor(float timer) => IsActive(timer) ? DamageMultiplier : 1f;
    }
}
