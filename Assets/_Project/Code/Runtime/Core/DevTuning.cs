using System;
using UnityEngine;

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
    /// A moved slider is still throwaway by default — it dies with the session unless the panel's
    /// "Save settings" button explicitly commits it via <see cref="Save"/> (YT-201). That's the
    /// difference from "Copy current values": Copy is for carrying a good set back into the authored
    /// consts as new defaults; Save is for making the current tuning stick, app-wide, without a
    /// rebuild, and it survives a full quit because <see cref="ApplyOnLaunch"/> reloads it before any
    /// scene's own Awake runs.
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

        // --- Invasion Level / escalation (YT-181 DifficultyDirector) ---

        /// <summary>Invasion Level at the run's start, before any time or shed kills have counted.</summary>
        public static float? EscalationStart { get; set; }

        /// <summary>How fast the Invasion Level climbs over time — units per second.</summary>
        public static float? EscalationRate { get; set; }

        /// <summary>The step-up the Invasion Level takes each time a factory shed is destroyed.</summary>
        public static float? EscalationPerShedBump { get; set; }

        /// <summary>The ceiling the Invasion Level climbs to.</summary>
        public static float? EscalationMax { get; set; }

        // --- Death-throes surge (YT-182) — the wreck's last wave on shed destruction ---

        /// <summary>Robots the death-throes surge spawns when a factory shed is destroyed. Overrides
        /// the Invasion-Level curve outright — a pinned value doesn't grow bigger over a run, same
        /// contract as <see cref="SpawnInterval"/> overriding the spawn-cadence ramp.</summary>
        public static float? DeathSurgeBurstSize { get; set; }

        /// <summary>Chance [0,1] the death-throes surge includes one "elite" (a Bruiser) crawling out
        /// of the wreck. Overrides the Invasion-Level curve outright, same contract as
        /// <see cref="DeathSurgeBurstSize"/>.</summary>
        public static float? DeathSurgeEliteChance { get; set; }

        // --- Swarm pacing (YT-194) — the front-of-curve fix: a couple of robots at run start, not a
        // swarm; a more intuitive production unit; and a real toughness knob now that the field-wide
        // cap (YT-186) means late-game danger has to come from durability rather than raw numbers. ---

        /// <summary>Robots this factory allows alive at RUN START — ramps up to its authored
        /// <c>maxLiveEnemies</c> as <see cref="MaxWorlds.Enemies.DifficultyDirector.Normalized"/>
        /// climbs, so the opening reads as a couple of robots rather than the full swarm on frame
        /// one.</summary>
        public static float? StartingRobots { get; set; }

        /// <summary>Steady-state robots produced per MINUTE — the same number
        /// <see cref="SpawnInterval"/> expresses as raw seconds, but in the unit a player actually
        /// thinks in. Converted to seconds (60 / value) at the point of use; only replaces the
        /// steady-state end of the ramp, so the opening ease-in and the Invasion Level's
        /// <see cref="MaxWorlds.Enemies.DifficultyDirector.SpawnIntervalMultiplier"/> both still
        /// apply on top — unlike <see cref="SpawnInterval"/>, which pins a flat number outright.</summary>
        public static float? RobotProductionPerMinute { get; set; }

        /// <summary>Multiplies every archetype's base health (before the Invasion Level's own
        /// toughening). 1 = the authored default; the panel's "Robot health" knob.</summary>
        public static float? RobotHealthMultiplier { get; set; }

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
            WideBoreConeMultiplier.HasValue || HarnessCapacity.HasValue || AccelSpeed.HasValue ||
            EscalationStart.HasValue || EscalationRate.HasValue || EscalationPerShedBump.HasValue ||
            EscalationMax.HasValue ||
            DeathSurgeBurstSize.HasValue || DeathSurgeEliteChance.HasValue ||
            StartingRobots.HasValue || RobotProductionPerMinute.HasValue || RobotHealthMultiplier.HasValue;

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
            EscalationStart = null;
            EscalationRate = null;
            EscalationPerShedBump = null;
            EscalationMax = null;
            DeathSurgeBurstSize = null;
            DeathSurgeEliteChance = null;
            StartingRobots = null;
            RobotProductionPerMinute = null;
            RobotHealthMultiplier = null;
        }

        // ------------------------------------------------------------------ persistence (YT-201)

        private const string PrefsPrefix = "DevTuning.";
        // Distinguishes "never saved" from "saved with every knob back at its authored default" —
        // the latter is a real, deliberate save (e.g. after Reset then Save) and must still load.
        private const string SavedMarkerKey = PrefsPrefix + "Saved";

        /// <summary>Every override paired with its PlayerPrefs key, so Save/Load/Clear share one
        /// loop instead of thirty-three near-identical lines apiece.</summary>
        private static readonly (string Key, Func<float?> Get, Action<float?> Set)[] Persisted =
        {
            (PrefsPrefix + nameof(CameraDistance), () => CameraDistance, v => CameraDistance = v),
            (PrefsPrefix + nameof(PlayerMoveSpeed), () => PlayerMoveSpeed, v => PlayerMoveSpeed = v),
            (PrefsPrefix + nameof(RobotMoveSpeed), () => RobotMoveSpeed, v => RobotMoveSpeed = v),
            (PrefsPrefix + nameof(SpawnInterval), () => SpawnInterval, v => SpawnInterval = v),
            (PrefsPrefix + nameof(BossMoveSpeed), () => BossMoveSpeed, v => BossMoveSpeed = v),
            (PrefsPrefix + nameof(PlayerMaxHealth), () => PlayerMaxHealth, v => PlayerMaxHealth = v),
            (PrefsPrefix + nameof(BlasterDrainPerSecond), () => BlasterDrainPerSecond, v => BlasterDrainPerSecond = v),
            (PrefsPrefix + nameof(BlasterRegenPerSecond), () => BlasterRegenPerSecond, v => BlasterRegenPerSecond = v),
            (PrefsPrefix + nameof(FactoryHealth), () => FactoryHealth, v => FactoryHealth = v),
            (PrefsPrefix + nameof(BossHealth), () => BossHealth, v => BossHealth = v),
            (PrefsPrefix + nameof(BossVolleyInterval), () => BossVolleyInterval, v => BossVolleyInterval = v),
            (PrefsPrefix + nameof(BossAddsPerVolley), () => BossAddsPerVolley, v => BossAddsPerVolley = v),
            (PrefsPrefix + nameof(BossMaxAdds), () => BossMaxAdds, v => BossMaxAdds = v),
            (PrefsPrefix + nameof(BossVolleyWindup), () => BossVolleyWindup, v => BossVolleyWindup = v),
            (PrefsPrefix + nameof(HoseTetherLength), () => HoseTetherLength, v => HoseTetherLength = v),
            (PrefsPrefix + nameof(PartDropInterval), () => PartDropInterval, v => PartDropInterval = v),
            (PrefsPrefix + nameof(HydroDrainRate), () => HydroDrainRate, v => HydroDrainRate = v),
            (PrefsPrefix + nameof(PowerCellCapacity), () => PowerCellCapacity, v => PowerCellCapacity = v),
            (PrefsPrefix + nameof(PowerCellDropChance), () => PowerCellDropChance, v => PowerCellDropChance = v),
            (PrefsPrefix + nameof(NozzleConeMultiplier), () => NozzleConeMultiplier, v => NozzleConeMultiplier = v),
            (PrefsPrefix + nameof(PowerNozzleRange), () => PowerNozzleRange, v => PowerNozzleRange = v),
            (PrefsPrefix + nameof(RangeExtenderBonus), () => RangeExtenderBonus, v => RangeExtenderBonus = v),
            (PrefsPrefix + nameof(WideBoreConeMultiplier), () => WideBoreConeMultiplier, v => WideBoreConeMultiplier = v),
            (PrefsPrefix + nameof(HarnessCapacity), () => HarnessCapacity, v => HarnessCapacity = v),
            (PrefsPrefix + nameof(AccelSpeed), () => AccelSpeed, v => AccelSpeed = v),
            (PrefsPrefix + nameof(EscalationStart), () => EscalationStart, v => EscalationStart = v),
            (PrefsPrefix + nameof(EscalationRate), () => EscalationRate, v => EscalationRate = v),
            (PrefsPrefix + nameof(EscalationPerShedBump), () => EscalationPerShedBump, v => EscalationPerShedBump = v),
            (PrefsPrefix + nameof(EscalationMax), () => EscalationMax, v => EscalationMax = v),
            (PrefsPrefix + nameof(DeathSurgeBurstSize), () => DeathSurgeBurstSize, v => DeathSurgeBurstSize = v),
            (PrefsPrefix + nameof(DeathSurgeEliteChance), () => DeathSurgeEliteChance, v => DeathSurgeEliteChance = v),
            (PrefsPrefix + nameof(StartingRobots), () => StartingRobots, v => StartingRobots = v),
            (PrefsPrefix + nameof(RobotProductionPerMinute), () => RobotProductionPerMinute, v => RobotProductionPerMinute = v),
            (PrefsPrefix + nameof(RobotHealthMultiplier), () => RobotHealthMultiplier, v => RobotHealthMultiplier = v),
        };

        /// <summary>True once a save has actually happened. Lets the panel and tests tell "never
        /// saved" apart from "saved, and every knob happened to be at its authored default".</summary>
        public static bool HasSaved => PlayerPrefs.HasKey(SavedMarkerKey);

        /// <summary>Writes every current override to PlayerPrefs, keyed app-wide rather than to any
        /// save slot — every game, existing saves and brand-new ones, picks it up from the next
        /// launch on.</summary>
        public static void Save()
        {
            foreach (var f in Persisted)
            {
                float? v = f.Get();
                if (v.HasValue) PlayerPrefs.SetFloat(f.Key, v.Value);
                else PlayerPrefs.DeleteKey(f.Key);
            }
            PlayerPrefs.SetInt(SavedMarkerKey, 1);
            PlayerPrefs.Save();
        }

        /// <summary>Applies whatever was last saved on top of the current overrides. Runs
        /// automatically before any scene loads (see <see cref="ApplyOnLaunch"/>); exposed publicly
        /// so a test can simulate a relaunch without actually restarting the process.</summary>
        public static void LoadSaved()
        {
            if (!HasSaved) return;
            foreach (var f in Persisted)
                if (PlayerPrefs.HasKey(f.Key)) f.Set(PlayerPrefs.GetFloat(f.Key));
        }

        /// <summary>Drops the persisted set entirely. The panel's "Reset to defaults" calls this
        /// too, so a relaunch after a reset doesn't quietly bring the old saved numbers back.</summary>
        public static void ClearSaved()
        {
            foreach (var f in Persisted) PlayerPrefs.DeleteKey(f.Key);
            PlayerPrefs.DeleteKey(SavedMarkerKey);
            PlayerPrefs.Save();
        }

        /// <summary>Loads the saved settings before any scene's own Awake runs, so the home screen
        /// and every game — new or resumed — inherit them from frame one, with nothing hard-coded
        /// into a scene to make that happen (YT-201).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ApplyOnLaunch() => LoadSaved();
    }
}
