namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Owns "which room does this robot count itself as routing from, right now" (MV-447 cause 3).
    /// Pulled out of <see cref="RobotEnemy"/> for the same reason <see cref="PursuitStall"/> and
    /// <see cref="WallLatch"/> were: pure, no clock of its own beyond the <c>dt</c> it's handed,
    /// testable without a GameObject.
    ///
    /// <see cref="MaxWorlds.Arena.MapRoutes.Waypoint"/> answers differently depending on which room it
    /// is told the robot is standing in. A robot whose position straddles a zone boundary — a doorway
    /// threshold, a corner two rooms share — can have <c>MapData.ZoneAt</c> report first one room then
    /// the other from one frame to the next on nothing more than sub-metre movement, and those two
    /// rooms can route to materially different waypoints. That is the zone-boundary flip: the robot's
    /// steering target swaps every time the raw answer swaps.
    ///
    /// This is the hysteresis that keeps <see cref="MaxWorlds.Arena.MapRoutes"/> itself pure (no
    /// clock): it holds on to the last room it committed to routing from and only adopts a newly
    /// reported room once that room has been reported continuously for <see cref="SwitchDelay"/>.
    /// </summary>
    public sealed class ZoneHysteresis
    {
        /// <summary>How long a newly-reported zone id must hold continuously before it replaces the
        /// one this robot is routing from.</summary>
        public const float SwitchDelay = 0.35f;

        private string _current;
        private string _pending;
        private float _pendingTimer;

        /// <summary>Call once per Chase tick with the zone id the robot's raw position is actually in
        /// right now (null if it's outside every room). Returns the zone id to route from.</summary>
        public string Resolve(string raw, float dt)
        {
            if (_current == null)
            {
                _current = raw;
                _pending = null;
                _pendingTimer = 0f;
                return _current;
            }

            if (raw == _current)
            {
                _pending = null;
                _pendingTimer = 0f;
                return _current;
            }

            if (raw != _pending)
            {
                _pending = raw;
                _pendingTimer = 0f;
            }
            else
            {
                _pendingTimer += dt;
            }

            if (_pendingTimer >= SwitchDelay)
            {
                _current = _pending;
                _pending = null;
                _pendingTimer = 0f;
            }

            return _current;
        }

        /// <summary>Drop everything this robot knew about which room it was routing from. A pooled
        /// robot doesn't inherit the last one's zone (same convention as
        /// <see cref="PursuitStall.NoteSightHeld"/>/<see cref="WallLatch.Reset"/>).</summary>
        public void Reset()
        {
            _current = null;
            _pending = null;
            _pendingTimer = 0f;
        }
    }
}
