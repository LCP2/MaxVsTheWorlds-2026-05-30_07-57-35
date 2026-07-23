namespace MaxWorlds.Core
{
    /// <summary>
    /// Session overrides for the combat-feel numbers, driven by the in-game Settings panel (YT-120,
    /// originally YT-105).
    ///
    /// Why this layer exists at all: the authored numbers live in <c>const</c> classes on purpose
    /// (see <see cref="MaxWorlds.Combat.BlasterTuning"/> — serialized fields got baked into
    /// Backyard_Slice.unity and silently outranked the code). Consts can't be written at runtime,
    /// and promoting them to mutable statics would throw away exactly the property that made them
    /// trustworthy. So the authored value stays a const and this sits in front of it: gameplay asks
    /// <see cref="Or"/> for the number, and gets the authored one unless the Settings panel has
    /// dialled an override.
    ///
    /// A fresh session starts with every override null, so <see cref="Or"/> returns the authored
    /// value until a slider is actually moved. No dev flag gates this any more (YT-120): the panel
    /// is always compiled in, so a moved slider always takes effect. That is the point of it.
    ///
    /// Deliberately NOT persisted to disk. These are throwaway numbers you sweep past to find the
    /// one you want; the panel's "Copy current values" button is the intended way for a good set to
    /// leave a session, by being pasted back into the authored consts as new defaults.
    /// </summary>
    public static class DevTuning
    {
        /// <summary>Camera pull-back in metres. Shares the knob with the [ / ] nudge keys (YT-82).</summary>
        public static float? CameraDistance { get; set; }

        /// <summary>Max's planar move speed, m/s. Dash speed is deliberately not tunable here.</summary>
        public static float? PlayerMoveSpeed { get; set; }

        /// <summary>Robot chase speed, m/s. Applies to live robots and to anything spawned after.</summary>
        public static float? RobotMoveSpeed { get; set; }

        /// <summary>Seconds between robot spawns, overriding the whole start→min ramp with one flat
        /// rate. Read live every frame by <see cref="MaxWorlds.Enemies.EnemySpawner.CurrentInterval"/>,
        /// so moving the slider changes emergence cadence for every factory immediately (YT-170).</summary>
        public static float? SpawnInterval { get; set; }

        /// <summary>Big Bermuda's reposition speed, m/s. Charge speed is left alone.</summary>
        public static float? BossMoveSpeed { get; set; }

        /// <summary>Max's maximum HP.</summary>
        public static float? PlayerMaxHealth { get; set; }

        /// <summary>What holding the trigger costs per second — the unit the ramp holds constant.</summary>
        public static float? BlasterDrainPerSecond { get; set; }

        /// <summary>Tank refill rate per second, once the regen delay has passed.</summary>
        public static float? BlasterRegenPerSecond { get; set; }

        /// <summary>Each Mower Hutch's max HP — how much spray it takes to destroy (YT-126).</summary>
        public static float? FactoryHealth { get; set; }

        /// <summary>Big Bermuda's max HP (YT-126).</summary>
        public static float? BossHealth { get; set; }

        /// <summary>Seconds between the boss's brood volleys — its side-hatch add-spawner (YT-157). Read
        /// live, so the Settings BOSS tab retimes the waves mid-fight.</summary>
        public static float? BossVolleyInterval { get; set; }

        /// <summary>Robots flung per brood volley (YT-157). Rounded to a whole robot at the point of use.</summary>
        public static float? BossAddsPerVolley { get; set; }

        /// <summary>The ceiling on brood adds alive at once (YT-157) — the kiteability knob for the boss
        /// fight, where nothing else caps the robot count.</summary>
        public static float? BossMaxAdds { get; set; }

        /// <summary>The spawn telegraph length — how long the hatches gape before the fling (YT-157). The
        /// player's window to read the volley and reposition.</summary>
        public static float? BossVolleyWindup { get; set; }

        /// <summary>Hard max length of the hose leash, metres — how far Max can range from his tap (YT-129).</summary>
        public static float? HoseTetherLength { get; set; }

        /// <summary>Tough-robot kills between upgrade-part drops (YT-143) — bigger spreads the five parts
        /// further across a level. Power cells drop on their own faster rate regardless.</summary>
        public static float? PartDropInterval { get; set; }

        /// <summary>Power cells the Hydro device burns per second while untethered (YT-137).</summary>
        public static float? HydroDrainRate { get; set; }

        /// <summary>Max power cells the reserve holds — the meter's full mark (YT-137).</summary>
        public static float? PowerCellCapacity { get; set; }

        /// <summary>Chance [0,1] that a rusher's death drops a single power cell (YT-171) — the common
        /// kill's replenish trickle, on top of the bruiser's guaranteed drop. Not every robot need drop
        /// one, so this is a roll rather than a guarantee.</summary>
        public static float? PowerCellDropChance { get; set; }

        // --- upgrade-part effect magnitudes (YT-138 Weapons tab) ---

        /// <summary>Each nozzle's cone multiplier — smaller narrows the beam more.</summary>
        public static float? NozzleConeMultiplier { get; set; }

        /// <summary>Extra reach in metres the Power nozzle adds.</summary>
        public static float? PowerNozzleRange { get; set; }

        /// <summary>Extra reach in metres the Range Extender adds on top of the Power nozzle (YT-164).</summary>
        public static float? RangeExtenderBonus { get; set; }

        /// <summary>The Wide-Bore's own cone multiplier, stacked on top of the nozzles (YT-164).</summary>
        public static float? WideBoreConeMultiplier { get; set; }

        /// <summary>Water-capacity bonus the Augmentation harness adds.</summary>
        public static float? HarnessCapacity { get; set; }

        /// <summary>Move-speed multiplier the Acceleration engine gives.</summary>
        public static float? AccelSpeed { get; set; }

        /// <summary>
        /// The number gameplay should actually use: the override if the Settings panel has set one,
        /// otherwise the authored value.
        ///
        /// No longer gated on any dev flag (YT-120). The Settings panel is now a real, always-present
        /// feature compiled into every build, so a slider the player moved must take effect — the
        /// whole point of it is to change the game live. Until a slider is touched the override is
        /// null and this returns the authored constant on the first comparison, so a fresh session
        /// still plays exactly as authored and the cost is one HasValue read.
        /// </summary>
        public static float Or(float? over, float authored) =>
            over.HasValue ? over.Value : authored;

        /// <summary>True if any knob has been moved this session. Used by the panel's readout.</summary>
        public static bool AnyOverride =>
            CameraDistance.HasValue || PlayerMoveSpeed.HasValue || RobotMoveSpeed.HasValue ||
            SpawnInterval.HasValue ||
            BossMoveSpeed.HasValue || PlayerMaxHealth.HasValue ||
            BlasterDrainPerSecond.HasValue || BlasterRegenPerSecond.HasValue ||
            FactoryHealth.HasValue || BossHealth.HasValue || HoseTetherLength.HasValue ||
            BossVolleyInterval.HasValue || BossAddsPerVolley.HasValue || BossMaxAdds.HasValue ||
            BossVolleyWindup.HasValue ||
            PartDropInterval.HasValue || HydroDrainRate.HasValue || PowerCellCapacity.HasValue ||
            PowerCellDropChance.HasValue ||
            NozzleConeMultiplier.HasValue || PowerNozzleRange.HasValue || RangeExtenderBonus.HasValue ||
            WideBoreConeMultiplier.HasValue || HarnessCapacity.HasValue || AccelSpeed.HasValue;

        /// <summary>Drop every override, back to the authored numbers.</summary>
        public static void Reset()
        {
            CameraDistance = null;
            PlayerMoveSpeed = null;
            RobotMoveSpeed = null;
            SpawnInterval = null;
            BossMoveSpeed = null;
            PlayerMaxHealth = null;
            BlasterDrainPerSecond = null;
            BlasterRegenPerSecond = null;
            FactoryHealth = null;
            BossHealth = null;
            BossVolleyInterval = null;
            BossAddsPerVolley = null;
            BossMaxAdds = null;
            BossVolleyWindup = null;
            HoseTetherLength = null;
            PartDropInterval = null;
            HydroDrainRate = null;
            PowerCellCapacity = null;
            PowerCellDropChance = null;
            NozzleConeMultiplier = null;
            PowerNozzleRange = null;
            RangeExtenderBonus = null;
            WideBoreConeMultiplier = null;
            HarnessCapacity = null;
            AccelSpeed = null;
        }
    }
}
