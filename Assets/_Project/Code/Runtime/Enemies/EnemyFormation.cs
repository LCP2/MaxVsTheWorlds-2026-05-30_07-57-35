using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// How a pack arrives (YT-93).
    ///
    /// Routing gets every robot to the same place by the same shortest way, which is correct and reads
    /// as a queue: a single-file line of robots walking down one lane at you, so the fight only ever
    /// happens on one side and killing the front one buys you the whole pack's worth of time. The
    /// playtest asked for pressure from multiple angles, and pressure is a shape, not a number.
    ///
    /// So each robot approaches on its OWN bearing: a stable sideways bias that fans the pack out at
    /// range and closes back in as it arrives. The fan is what makes them come at you from several
    /// sides at once; the closing is what stops it becoming a permanent orbit that never touches you.
    ///
    /// Pure maths — no transforms, no clock — so what the pack does is a thing a test can assert.
    /// </summary>
    public static class EnemyFormation
    {
        /// <summary>How far off the direct line a robot may swing, at full spread. Wider than a robot
        /// (so they genuinely take different lanes), narrower than the smallest room's free channel (so
        /// a fan never pushes the pack into a wall it then has to grind along).</summary>
        public const float Spread = 2.6f;

        /// <summary>Distance at which the fan is fully open. Inside this it closes down, so the last
        /// couple of metres are a converging attack and not a ring-around-the-rosie.</summary>
        public const float FullSpreadAt = 7f;

        /// <summary>How far off the direct line a FLANKING robot (MV-322) may swing, at full spread —
        /// wider than the ordinary <see cref="Spread"/> so a flanker visibly commits to a side approach
        /// instead of just leaning on the beeline. Still inside the same room-clearance headroom
        /// <see cref="Spread"/> was tuned against.</summary>
        public const float FlankSpread = 5.2f;

        /// <summary>Distance at which a flanker's wide swing is fully open — MV-322. Bigger than
        /// <see cref="FullSpreadAt"/> so it starts curving out well before it would otherwise have
        /// committed to a straight run at Max, which is what reads as "coming around" rather than
        /// "leaning while walking at him".</summary>
        public const float FlankFullSpreadAt = 11f;

        /// <summary>
        /// Whether this robot is one of the pack's flankers (MV-322) — a stable minority (about a
        /// third) that takes the wide <see cref="FlankSpread"/> swing instead of the ordinary fan's
        /// gentle lean, so "large numbers of robots come straight at Max" stops being every robot's
        /// whole approach. Independent of <see cref="Bias"/>'s lane pick — a flanker still keeps
        /// whichever side its lane already gave it, just swung much wider.
        /// </summary>
        public static bool IsFlanker(int id) => Mathf.Abs(id % 3) == 0;

        /// <summary>
        /// Where this robot should aim, given where it is and where the pack is going. The offset is
        /// perpendicular to its approach, stable for the life of the robot, and shrinks to nothing as
        /// it closes — so it fans out across the yard and converges on contact. A flanker (MV-322) uses
        /// a wider spread that opens at a longer range, so its approach reads as a deliberate arc
        /// rather than the rest of the pack's straight-in lean.
        /// </summary>
        public static Vector3 ApproachPoint(Vector3 goal, Vector3 from, int id)
        {
            Vector3 to = goal - from;
            to.y = 0f;

            float distance = to.magnitude;
            if (distance < 0.01f) return goal;

            bool flank = IsFlanker(id);
            float spread = flank ? FlankSpread : Spread;
            float fullSpreadAt = flank ? FlankFullSpreadAt : FullSpreadAt;

            Vector3 side = Vector3.Cross(Vector3.up, to / distance);
            float amount = spread * Bias(id) * Mathf.Clamp01(distance / fullSpreadAt);

            return goal + side * amount;
        }

        /// <summary>
        /// This robot's lane, −1..1. Stable (the same robot always takes the same side, so nothing
        /// jitters) and spread across the pack rather than split down the middle: a straight odd/even
        /// would give a fan with a hole in it exactly where the player is standing.
        /// </summary>
        public static float Bias(int id)
        {
            // Five lanes off a hash of the id: hard left, left, centre, right, hard right.
            int lane = Mathf.Abs(id % 5);
            return (lane - 2) * 0.5f;
        }
    }
}
