namespace MaxWorlds.Combat
{
    /// <summary>
    /// Everything about how long the Water Blaster keeps firing (YT-80), in one place.
    ///
    /// These are deliberately NOT <c>[SerializeField]</c>s on <see cref="WaterBlaster"/>. They used
    /// to be, and the scene silently won: <c>Backyard_Slice.unity</c> carried an old 2.5/tick drain
    /// and a 25/s regen, so the tank the code described (6.7 s) was not the tank anyone played
    /// (4.0 s) — and after the first magazine the hysteresis handed back a 1.4 s squirt for every
    /// 2.0 s of waiting. That is the "gun runs out too quickly" everyone felt, and no amount of
    /// editing the C# default would have fixed it. Authored here, in code, the scene cannot shadow
    /// it. Same reasoning as <see cref="MaxWorlds.Enemies.EnemyArchetype"/> and
    /// <see cref="MaxWorlds.Rendering.BackyardLook"/>.
    ///
    /// The unit that matters is ENERGY PER SECOND, not energy per tick: per-tick cost is meaningless
    /// on its own because the power ramp (YT-67) speeds the fire rate up, and it is per-second cost
    /// that the ramp deliberately holds constant. Author the number the player actually feels and
    /// let the tick cost be derived from it.
    /// </summary>
    public static class BlasterTuning
    {
        /// <summary>Size of the tank.</summary>
        public const float MaxEnergy = 140f;

        /// <summary>What holding the trigger costs per second. The ramp keeps this constant, so this
        /// single number sets sustained-fire length: <see cref="SustainedFireSeconds"/>.</summary>
        public const float EnergyPerSecond = 10f;

        /// <summary>Refill rate once the delay has passed. Comfortably faster than the drain, so any
        /// natural pause in a fight — repositioning, a dash, picking the next target — puts real
        /// ammo back rather than a token trickle.</summary>
        public const float RegenPerSec = 55f;

        /// <summary>Idle time before refilling starts. Short enough that letting go between targets
        /// is rewarded, long enough that it isn't refilling mid-burst.</summary>
        public const float RegenDelay = 0.35f;

        /// <summary>Run the tank dry and fire stays locked until it refills to this fraction — the
        /// hysteresis that stops an empty blaster dribbling one puff per delay.
        ///
        /// This is the number that decides what "running out" costs you, and at the old 0.35 it was
        /// brutal: you got a third of a tank back and ran dry again almost immediately, which is why
        /// the downtime felt constant rather than occasional. At 0.6 a dry tank costs you one short,
        /// legible pause and then hands back a burst worth having.</summary>
        public const float RechargeFraction = 0.6f;

        /// <summary>Seconds of unbroken fire from full. The headline feel number.</summary>
        public static float SustainedFireSeconds =>
            EnergyPerSecond > 0f ? MaxEnergy / EnergyPerSecond : 0f;

        /// <summary>Seconds of unbroken fire you get back after running the tank completely dry —
        /// i.e. every burst after the first, if you never stop holding the trigger.</summary>
        public static float RechargedFireSeconds =>
            EnergyPerSecond > 0f ? MaxEnergy * RechargeFraction / EnergyPerSecond : 0f;

        /// <summary>Seconds you spend NOT firing after running dry: the delay, plus refilling to
        /// <see cref="RechargeFraction"/>.</summary>
        public static float RecoverySeconds =>
            RegenPerSec > 0f ? RegenDelay + MaxEnergy * RechargeFraction / RegenPerSec : 0f;

        /// <summary>Fraction of the time the trigger actually produces water, for a player who never
        /// lets go. Real play is better than this, because real play has pauses.</summary>
        public static float WorstCaseUptime =>
            RechargedFireSeconds + RecoverySeconds > 0f
                ? RechargedFireSeconds / (RechargedFireSeconds + RecoverySeconds)
                : 0f;
    }
}
