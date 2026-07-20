using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// A real door on the factory that robots have to come out of (YT-108).
    ///
    /// Before this the mouth was notional: robots appeared against whichever wall faced Max and
    /// walked out of it. That reads as production only if you already know that is what you are
    /// looking at. With a door there is something to watch — it hauls up, robots come out, it drops —
    /// and the cadence is visible instead of inferred.
    ///
    /// The spawner asks; it does not command. A factory with no door behaves exactly as it always
    /// did, which is what keeps the door an art concern: <see cref="MaxWorlds.VFX.FactoryDoorway"/>
    /// implements this and nothing in gameplay knows the type.
    /// </summary>
    public interface IFactoryDoor
    {
        /// <summary>Is the door open far enough to walk a robot through it?</summary>
        bool CanEmit { get; }

        /// <summary>The way the door faces — the direction a robot leaves along.</summary>
        Vector3 OutwardDirection { get; }

        /// <summary>How wide the opening lets the stream fan, in degrees either side of
        /// <see cref="OutwardDirection"/>. Narrower than the notional mouth: robots squeezing through
        /// a 1.6 m doorway cannot spread across a 110° arc without walking through the wall.</summary>
        float FanHalfAngleDeg { get; }
    }
}
