namespace MaxWorlds.Player
{
    /// <summary>
    /// Max's out-of-combat health regen (YT-80).
    ///
    /// Authored here rather than as <c>[SerializeField]</c>s on <see cref="PlayerHealth"/> for the
    /// same reason the blaster's energy moved into <see cref="MaxWorlds.Combat.BlasterTuning"/>:
    /// anything serialized on a component that sits in <c>Backyard_Slice.unity</c> gets a copy baked
    /// into the scene, and from then on the scene quietly outranks the code. That is not a
    /// hypothetical — it is exactly how the blaster ended up draining at 25/s while the source said
    /// 15/s, and how two earlier fixes had to resort to renaming fields to make a number take
    /// effect. A const cannot be shadowed.
    /// </summary>
    public static class PlayerTuning
    {
        /// <summary>HP per second, once the delay has elapsed. Against a 100 HP bar that is a full
        /// heal in ~33 s of genuine peace — a reward for disengaging cleanly, and nowhere near
        /// enough to stand in a pack and out-heal it (a single rusher hits for 12).</summary>
        public const float RegenPerSec = 3f;

        /// <summary>Seconds without taking a hit before the trickle starts.
        ///
        /// This is the number doing the real work, not the rate: while anything is still landing
        /// hits on Max the timer keeps resetting and regen never begins at all. That is what keeps
        /// the pressure to dodge intact — the regen is something you earn by getting OUT, not
        /// something that happens while you're getting hit.</summary>
        public const float RegenDelay = 5f;

        /// <summary>Seconds of peace to undo one rusher's hit (12 HP). Sanity-checkable feel number.</summary>
        public static float SecondsToUndoARusherHit => RegenPerSec > 0f ? 12f / RegenPerSec : 0f;
    }
}
