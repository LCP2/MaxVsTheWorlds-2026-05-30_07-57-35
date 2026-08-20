namespace MaxWorlds.Enemies
{
    /// <summary>
    /// The wake test a dormant robot (MV-363) checks every tick (MV-478): BOTH the robot's body must
    /// sit inside the gameplay camera's frustum AND the sight-line to Max must be clear. Pulled out
    /// of <see cref="RobotEnemy"/> for the same reason <see cref="PursuitStall"/>/<see cref="WallLatch"/>/
    /// <see cref="ZoneHysteresis"/> were — plain booleans in, no Camera, no Physics query, no clock,
    /// testable without a GameObject.
    ///
    /// <see cref="MaxWorlds.Arena.LineOfSight"/> is symmetric geometry (the robot sees Max iff Max
    /// sees the robot), so "sight clear" alone was never a meaningful gate — a concealed robot the
    /// player had never looked at still woke the instant its own raycast to Max cleared, which is
    /// every robot the player has ever walked near. <c>onScreen</c> is what actually answers "has the
    /// PLAYER looked at it".
    /// </summary>
    public static class AmbushWake
    {
        public static bool ShouldWake(bool onScreen, bool sightClear) => onScreen && sightClear;
    }
}
