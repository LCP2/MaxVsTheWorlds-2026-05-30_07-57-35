namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Decides when a robot that has lost sight of Max should give up on his last-known position and
    /// fall back to Search (MV-387). Pulled out of <see cref="RobotEnemy"/> for the same reason
    /// <see cref="Perception"/> was (YT-83): pure, no transform, no clock of its own — testable
    /// without a GameObject.
    ///
    /// Two ways to give up: walk right up to the last-known spot, or grind in place without getting
    /// any closer to it for <c>searchTime</c>. Both are gated by a minimum hunt duration: a robot
    /// that was already standing on top of the last-known spot the instant sight broke — which
    /// happens whenever cover is close, e.g. ducking behind a nearby shrub mid-chase — used to read
    /// "arrived" on frame one, flipping straight to Search's spin-in-place with no visible pursuit at
    /// all (MV-387). The gate forces at least that long of keeping-after-it, so cover still reads as
    /// cover instead of an instant off-switch, before either check is allowed to fire.
    /// </summary>
    public sealed class PursuitStall
    {
        /// <summary>How much closer counts as having got closer (YT-93's slack for a robot rounding a
        /// corner — briefly moving away from the spot to get to it — while still not letting one
        /// grinding on a fence read as progress).</summary>
        private const float Progress = 0.15f;

        private float _closest = float.MaxValue;
        private float _stallTimer;
        private float _huntTimer;

        /// <summary>Call every tick sight is held. Clears the hunt so the next loss starts its own
        /// clock, not one still running from the last time it lost him.</summary>
        public void NoteSightHeld()
        {
            _closest = float.MaxValue;
            _stallTimer = 0f;
            _huntTimer = 0f;
        }

        /// <summary>
        /// Call every tick sight is lost, with the live distance to the last-known spot it's walking
        /// toward. Returns true once it should give up: <paramref name="minHuntTime"/> has passed AND
        /// (it's within <paramref name="arriveRadius"/> of the spot, or it hasn't gotten meaningfully
        /// closer in <paramref name="searchTime"/>).
        /// </summary>
        public bool TickHunting(float distanceToGoal, float dt, float arriveRadius, float searchTime, float minHuntTime)
        {
            _huntTimer += dt;

            if (distanceToGoal < _closest - Progress) { _closest = distanceToGoal; _stallTimer = 0f; }
            else _stallTimer += dt;

            if (_huntTimer < minHuntTime) return false;
            return distanceToGoal <= arriveRadius || _stallTimer >= searchTime;
        }
    }
}
