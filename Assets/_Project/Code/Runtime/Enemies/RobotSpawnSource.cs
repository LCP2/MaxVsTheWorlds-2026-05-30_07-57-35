using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// MV-350 diagnostic tag: which system's <c>CreateInstance</c> actually built this robot —
    /// <see cref="EnemySpawner"/> (a factory) or <see cref="AreaAccumulationDirector"/> (the gated
    /// arena). Stamped once, at creation, by whichever spawner just built the primitive stand-in; a
    /// pooled robot's spawn source never changes across a later respawn, because both spawners only
    /// ever pull from their own pool.
    ///
    /// Exists purely so <see cref="MaxWorlds.VFX.RobotSkinDiagnostics"/> can name the origin of a
    /// robot that shows up tan in the console log — MV-350 has already ruled out "one spawn path
    /// skips the skin" as the mechanism (<c>AreaPopulation.ComposeForArea</c> is dead code), but the
    /// hunt still needs to know whether the tan robots cluster on one live spawner or spread across
    /// both. Read-only outside this assembly; nothing but a spawner's own <c>CreateInstance</c> should
    /// ever call <see cref="Mark"/>.
    /// </summary>
    public sealed class RobotSpawnSource : MonoBehaviour
    {
        public string Source { get; private set; } = "unknown";

        public void Mark(string source) => Source = source;
    }
}
