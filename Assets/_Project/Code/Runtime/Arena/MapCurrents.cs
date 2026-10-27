using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Arena
{
    /// <summary>The single current-lane drift query every mover consults (MV-1300) — mirrors
    /// <see cref="MapSlowZones"/>'s own shape: stateless, one shared instance, reads whichever level
    /// <see cref="EnemyNavigation"/> already knows about, so an authored current rect pushes anything
    /// standing in it without Max or a robot needing to know the level's shape itself.</summary>
    public sealed class MapCurrents
    {
        public static readonly MapCurrents Instance = new MapCurrents();
        private MapCurrents() { }

        /// <summary>This frame's additive drift velocity (m/s) at a world position — zero outside every
        /// lane, summed across however many overlap (MapGeometry.CurrentDriftAt's own "several sources
        /// add" rule).</summary>
        public Vector3 DriftAt(Vector3 worldPosition) =>
            MapGeometry.CurrentDriftAt(EnemyNavigation.Map, worldPosition.x, worldPosition.y, worldPosition.z);
    }
}
