namespace MaxWorlds.Core
{
    /// <summary>
    /// Session-only overrides for the combat-feel numbers, driven by the dev tuning panel (YT-105).
    ///
    /// Why this layer exists at all: the authored numbers live in <c>const</c> classes on purpose
    /// (see <see cref="MaxWorlds.Combat.BlasterTuning"/> — serialized fields got baked into
    /// Backyard_Slice.unity and silently outranked the code). Consts can't be written at runtime,
    /// and promoting them to mutable statics would throw away exactly the property that made them
    /// trustworthy. So the authored value stays a const and this sits in front of it: gameplay asks
    /// <see cref="Or"/> for the number, and gets the authored one unless a dev has dialled an
    /// override this session.
    ///
    /// Every override is gated behind <see cref="DevMode.Enabled"/>, so in a release session
    /// <see cref="Or"/> returns the authored value on the first comparison and nothing here can
    /// change how the game plays. That gate is the whole "not present in release" story — there is
    /// no scripting define to hide behind, because the project ships with none and the CI WebGL
    /// build is a non-development build.
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

        /// <summary>Big Bermuda's reposition speed, m/s. Charge speed is left alone.</summary>
        public static float? BossMoveSpeed { get; set; }

        /// <summary>Max's maximum HP.</summary>
        public static float? PlayerMaxHealth { get; set; }

        /// <summary>What holding the trigger costs per second — the unit the ramp holds constant.</summary>
        public static float? BlasterDrainPerSecond { get; set; }

        /// <summary>Tank refill rate per second, once the regen delay has passed.</summary>
        public static float? BlasterRegenPerSecond { get; set; }

        /// <summary>
        /// The number gameplay should actually use: the dev override if one is set AND the tuning
        /// tools are available, otherwise the authored value. The <see cref="DevMode.ToolsAvailable"/>
        /// check comes first so a release session costs one bool read.
        ///
        /// Gated on ToolsAvailable rather than <see cref="DevMode.Enabled"/> since YT-118: a beta
        /// build has the panel but not the cheats, and a slider that moves without changing the game
        /// would be worse than no slider at all. In a release build the define is absent and
        /// ToolsAvailable collapses to Enabled, which is false — so this is unchanged where it
        /// matters.
        /// </summary>
        public static float Or(float? over, float authored) =>
            DevMode.ToolsAvailable && over.HasValue ? over.Value : authored;

        /// <summary>True if any knob has been moved this session. Used by the panel's readout.</summary>
        public static bool AnyOverride =>
            CameraDistance.HasValue || PlayerMoveSpeed.HasValue || RobotMoveSpeed.HasValue ||
            BossMoveSpeed.HasValue || PlayerMaxHealth.HasValue ||
            BlasterDrainPerSecond.HasValue || BlasterRegenPerSecond.HasValue;

        /// <summary>Drop every override, back to the authored numbers.</summary>
        public static void Reset()
        {
            CameraDistance = null;
            PlayerMoveSpeed = null;
            RobotMoveSpeed = null;
            BossMoveSpeed = null;
            PlayerMaxHealth = null;
            BlasterDrainPerSecond = null;
            BlasterRegenPerSecond = null;
        }
    }
}
