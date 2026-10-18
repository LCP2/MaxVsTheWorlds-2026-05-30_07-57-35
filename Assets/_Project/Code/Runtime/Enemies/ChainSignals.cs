using System;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// MV-1228: the Breaker's chain-snap bus — it announces here the instant it actually wakes
    /// (<see cref="RobotEnemy.Activate"/>), never on a wake attempt refused by the area's own
    /// "at most 2 awake" cap. The look ticket (MV-1231, the steel collar and snapped chain) is the
    /// intended listener; this ticket only fires the event. Null-safe like <c>DropSignals</c>/
    /// <c>HudSignals</c> — emitting with nobody listening is a no-op.
    /// </summary>
    public static class ChainSignals
    {
        /// <summary>A Breaker woke. Arg: which one.</summary>
        public static event Action<RobotEnemy> ChainSnapped;

        public static void EmitChainSnapped(RobotEnemy breaker) => ChainSnapped?.Invoke(breaker);
    }
}
