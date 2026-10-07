using UnityEngine;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// MV-1107: an authored door facing a factory body already knows, read by
    /// <see cref="MaxWorlds.VFX.FactoryDoorway"/> instead of probing for an open wall. Only
    /// <see cref="Replicator"/> implements this — <see cref="MaxWorlds.Arena.MapEntity.facing"/> is
    /// authored (and defaulted to "S") for a Replicator only, empty for every other entity kind, so a
    /// shed has no authored side to build onto and keeps resolving its door the old, probed way.
    /// </summary>
    public interface IFactoryDoorFacing
    {
        /// <summary>The world-XZ unit vector the authored facing names, or null if this body carries
        /// no authored facing.</summary>
        Vector3? AuthoredDoorOutward { get; }
    }
}
