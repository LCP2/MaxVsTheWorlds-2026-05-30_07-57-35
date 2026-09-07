using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Arena
{
    /// <summary>Something that can slow a mover at a given world position (MV-692) — the hook
    /// <see cref="MaxWorlds.Player.PlayerController"/> and <see cref="RobotEnemy"/> both consult, kept
    /// as an interface so a future hazard is a second implementation rather than a fork of either
    /// mover's own movement code.</summary>
    public interface IMoveSpeedModifier
    {
        /// <summary>1 = no effect; below 1 = slowed. This hook only ever slows — a mover that should go
        /// faster is a different feature (<see cref="MaxWorlds.Upgrades.UpgradeState.MoveSpeedMultiplier"/>).</summary>
        float SpeedMultiplierAt(Vector3 worldPosition);
    }

    /// <summary>The single <see cref="IMoveSpeedModifier"/> every mover consults (MV-692) — reads
    /// whichever level <see cref="EnemyNavigation"/> already knows about, so a sludge rect authored
    /// anywhere in the map slows anything standing in it without Max or a robot needing to know the
    /// level's shape itself. Stateless; the one shared instance is enough.</summary>
    public sealed class MapSlowZones : IMoveSpeedModifier
    {
        public static readonly MapSlowZones Instance = new MapSlowZones();
        private MapSlowZones() { }

        public float SpeedMultiplierAt(Vector3 worldPosition) =>
            MapGeometry.SpeedMultiplierAt(EnemyNavigation.Map, worldPosition.x, worldPosition.z);
    }
}
