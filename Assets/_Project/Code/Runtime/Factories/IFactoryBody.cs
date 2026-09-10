namespace MaxWorlds.Factories
{
    /// <summary>
    /// The common surface a destructible factory building offers the art layer that runs around it
    /// (MV-756) — <see cref="MaxWorlds.VFX.FactoryLife"/>, <see cref="MaxWorlds.VFX.FactoryHusk"/> and
    /// <see cref="MaxWorlds.VFX.FactoryDoorway"/> were all hard-typed to <see cref="MowerHutch"/>, so a
    /// <see cref="Replicator"/> got none of their shudder/collapse/sink death sequence. Both factory
    /// types already expose exactly this shape; implementing it costs them nothing.
    /// </summary>
    public interface IFactoryBody
    {
        bool IsAlive { get; }
        float Normalized { get; }
    }
}
