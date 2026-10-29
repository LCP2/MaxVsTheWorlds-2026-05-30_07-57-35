namespace MaxWorlds.Core
{
    /// <summary>
    /// MV-1311: a structure a Welder can repair. <see cref="MaxWorlds.Factories.AssemblyCradle"/> (continuous
    /// HP/s while alive-but-damaged) and <see cref="MaxWorlds.Factories.Pylon"/> (a one-shot full revive once
    /// <see cref="IsDown"/>) both implement this and register with
    /// <see cref="MaxWorlds.Factories.RepairableRegistry"/>, replacing the Welder's own old hard-wired
    /// <c>AssemblyCradle.NearestDamagedInArea</c> call with one area-scoped lookup that works across either
    /// kind without either knowing about the other.
    /// </summary>
    public interface IRepairable
    {
        /// <summary>Which 1-based area this repairable was placed into — same stamp convention as
        /// <see cref="MaxWorlds.Enemies.RobotEnemy.AreaIndex"/>.</summary>
        int AreaIndex { get; }

        /// <summary>0..1 current health fraction.</summary>
        float Normalized { get; }

        /// <summary>True once this repairable has been fully destroyed and is waiting on a Welder's
        /// one-shot rebuild beam, rather than a continuous HP/s trickle — see each implementor's own doc
        /// comment for which path it actually supports.</summary>
        bool IsDown { get; }

        /// <summary>Restore HP (or, while <see cref="IsDown"/>, revive outright) — see each implementor's
        /// own doc comment for its exact semantics.</summary>
        void Heal(float amount);
    }
}
