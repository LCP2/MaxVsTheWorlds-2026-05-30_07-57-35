using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;
using MaxWorlds.CameraRig;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Bosses;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Upgrades;

namespace MaxWorlds.UI
{
    /// <summary>
    /// The in-game Settings panel (YT-120) — a gear button that opens a panel of live tuning sliders.
    ///
    /// It began as a dev-only overlay (YT-105) gated behind a build-time scripting define. That
    /// define was injected by editing ProjectSettings.asset mid-CI, which dirtied the git tree and
    /// tripped the version guard, so the iOS build failed the moment the panel was turned on
    /// (YT-119). The fix Lee asked for is the honest one: make it a real Settings panel that is
    /// ALWAYS compiled into every build. No <c>#if</c>, no define, no build-time file edits — the
    /// gear is simply always there, and a slider a player moves takes effect live through
    /// <see cref="DevTuning"/>.
    ///
    /// The sliders are still the combat-feel numbers: every one is a feel call, and a feel call
    /// costs a guess → build → deploy → play round trip to evaluate. On a phone that round trip is
    /// minutes, so this lets a value be found by sweeping past it and coming back.
    ///
    /// Built in uGUI, NOT IMGUI: the project runs Active Input Handling = "Input System (New)" only,
    /// where IMGUI receiving touch on device is not something to bet an acceptance criterion on.
    /// uGUI rides the EventSystem + InputSystemUIInputModule path the on-screen sticks already prove
    /// works on iOS and WebGL (YT-98). Its canvas sits at sorting order 200, above the HUD's 100, so
    /// its raycasts beat the invisible OnScreenStick pads rather than being swallowed by them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsPanel : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<SettingsPanel>() != null) return;
            new GameObject("SettingsPanel").AddComponent<SettingsPanel>();
        }

        // --- layout, in canvas reference units (1920x1080, match 0.5) ---
        //
        // Sized against the phone, not the monitor: the Craft Bible's non-negotiable is a 6-inch
        // screen. On an iPhone Plus in landscape (932x430pt) the scale factor is
        // sqrt(932/1920)*sqrt(430/1080) = 0.44, so one reference unit is 0.44pt. That makes the
        // smallest font below ~10.5pt (iOS caption is 11-13pt) and the gear a 42pt target.
        private const float RefW = 1920f;
        private const float RefH = 1080f;
        private const float Scale6Inch = 0.44f;   // used by the layout test

        // Grew by SaveBtnW + SaveBtnGap (YT-201) to make room for the Save settings button without
        // touching the other three footer buttons' proven widths. Width has huge slack against the
        // 932pt phone-width ceiling (currently 552.6pt used), unlike height, which is nearly maxed —
        // see PanelH below.
        private const float PanelW = 980f + SaveBtnW + SaveBtnGap;
        private const float SaveBtnW = 260f;
        private const float SaveBtnGap = 16f;
        // Grew for the two durability sliders (YT-126), then again in YT-192 so the three-column
        // value dump has room below the footer without its box running past the panel's bottom
        // edge, then again at YT-210 to match DumpH's growth for the Gameplay tab's 18th knob
        // (Run length). Still inside the landscape-phone height (955 * 0.44 ≈ 420pt < 430), which
        // the layout test guards.
        private const float PanelH = 955f;
        private const float Pad = 20f;
        private const float ColGap = 20f;
        private const float RowH = 112f;
        private const float SliderH = 72f;
        private const float HandleW = 64f;
        private const float HeaderH = 56f;
        private const float ButtonH = 84f;
        // The copied-values dump is laid out in THREE columns (YT-126, widened from two in YT-192
        // once the Gameplay tab grew to 17 knobs) — a single 10+-line column would need a taller
        // DumpH than the phone-height ceiling has room for, so it's spread wider instead of taller.
        // Grew again at YT-210 for the Gameplay tab's 18th knob (Run length), which pushed the
        // header-plus-knobs line count in the tallest column from 6 to 7.
        private const float DumpH = 195f;
        private const float GearSize = 96f;

        private const int LabelFont = 30;
        private const int HeaderFont = 40;
        private const int DumpFont = 24;

        // Knob row label/value (YT-190). The label and value used to share one baseline with no
        // bound on the label's width, so on the 3-column GAMEPLAY/WEAPONS tabs a long label ran
        // straight under the value's raw number. Fixed by giving the value a constant-width zone
        // (wide enough for the on-row percent text at every knob's range) and letting the label
        // take the rest of the row, with a gap between so they can never touch even at the
        // narrowest (3-column) row width. KnobLabelFont is a notch down from LabelFont so the
        // longest names fit that zone without wrapping; it still clears the 10pt-on-a-6-inch-phone
        // floor (24 * 0.44 = 10.6pt), same as DumpFont.
        private const int KnobLabelFont = 24;
        private const float KnobValueW = 80f;
        // Trimmed from 8f (YT-192): the new four-column Gameplay tab (17 knobs, see cols below) has
        // less width per row than the three-column tabs, and "Robot move speed" needed those few
        // extra px of label zone to stay on one line. Still a real, visible gap between label and
        // value at every column count.
        private const float KnobGap = 2f;

        // Basement-biome dark panel + bright green accent, from mockups/13-settings.html.
        private static readonly Color PanelColor = new Color(0.055f, 0.075f, 0.062f, 0.96f);
        private static readonly Color Accent = new Color(0.298f, 0.851f, 0.392f);      // #4CD964
        private static readonly Color AccentDeep = new Color(0.165f, 0.616f, 0.204f);  // #2A9D34
        private static readonly Color TrackColor = new Color(1f, 1f, 1f, 0.14f);
        private static readonly Color TextColor = Color.white;
        private static readonly Color Scrim = new Color(0f, 0f, 0f, 0.55f);

        private Canvas _canvas;
        private RectTransform _safeRoot;
        private GameObject _panelRoot;
        private GameObject _scrim;
        private Text _dumpTextL, _dumpTextM, _dumpTextR;   // three-column value dump (YT-126, YT-192)
        private bool _open;

        private readonly List<Knob> _knobs = new List<Knob>();

        /// <summary>One tunable value: where its number comes from, where it goes, and what 100%
        /// means. <see cref="Apply"/> aside — most knobs read through <see cref="DevTuning"/> at the
        /// point of use and pick a new number up next frame; only the ones cached into an object at
        /// construction (camera offset, energy pool, health ceiling) have to be pushed.</summary>
        private sealed class Knob
        {
            public string Name;
            public string Unit;
            public float Min;
            public float Max;
            public float Default;
            public Func<float> Get;
            public Action<float> Set;
            public Slider Slider;
            public Text Value;
            public int Tab;   // 0 = Gameplay, 1 = Weapons (YT-138), 2 = Boss (YT-157)
        }

        // Tabs (YT-138): the panel is full at 10 gameplay knobs, so the upgrade tuning lives on a
        // second page, and the boss's own attack tuning (YT-157) on a third. One container per tab;
        // only the active one is shown.
        private static readonly string[] TabNames = { "GAMEPLAY", "WEAPONS", "BOSS" };
        private GameObject[] _pages;
        private Button[] _tabButtons;
        private int _tab;

        // Built once, a frame after the scene loads (the objects it reads defaults from wake in their
        // own Awake first). Always — there is no gate any more.
        private void Start() => Build();

        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            if (_canvas != null) return;
            EnsureEventSystem();

            var go = new GameObject("Settings Canvas", typeof(Canvas), typeof(CanvasScaler),
                                    typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;   // above the HUD's 100, so the sticks can't eat our taps

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // Everything hangs off a safe-area root so the gear clears the notch on the exact device
            // the ticket is about.
            _safeRoot = NewRect("Safe Area", _canvas.transform, Vector2.zero, Vector2.one);
            Stretch(_safeRoot);
            _safeRoot.gameObject.AddComponent<SafeArea>();

            BuildKnobs();
            BuildScrim();
            BuildGearButton();
            BuildPanel();
            SetOpen(false);
        }

        /// <summary>The HUD builds one too, but the panel must work in a scene with no HUD (a test
        /// fixture, a stripped scene) or nothing would be clickable.</summary>
        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>();
        }

        /// <summary>
        /// Declare the seven knobs, capturing the authored value of each as its 100% reference.
        /// Defaults come from live objects where serialized (camera, player) and from tuning classes
        /// where const (robot, boss, blaster). If an object isn't in this scene the knob still works —
        /// the override lives in global <see cref="DevTuning"/> — it just falls back to the constant.
        /// </summary>
        private void BuildKnobs()
        {
            _knobs.Clear();

            var rig = FindFirstObjectByType<FixedAngleCameraRig>();
            var player = FindFirstObjectByType<PlayerController>();
            var health = FindFirstObjectByType<PlayerHealth>();

            float camDefault = rig != null ? rig.Distance : 25.1f;
            float playerDefault = player != null ? player.AuthoredMoveSpeed : 6f;
            float healthDefault = health != null ? health.AuthoredMax : 100f;
            float robotDefault = EnemyArchetype.Rusher.MoveSpeed;
            float bossDefault = BossTuning.MoveSpeed;
            float drainDefault = BlasterTuning.EnergyPerSecond;
            float regenDefault = BlasterTuning.RegenPerSec;

            Add("Camera zoom", "m", FixedAngleCameraRig.MinDistance, FixedAngleCameraRig.MaxDistance,
                camDefault,
                () => DevTuning.Or(DevTuning.CameraDistance, camDefault),
                v =>
                {
                    DevTuning.CameraDistance = v;
                    var r = FindFirstObjectByType<FixedAngleCameraRig>();
                    if (r != null) r.SetDistance(v);
                });

            Add("Max move speed", "m/s", 1f, 15f, playerDefault,
                () => DevTuning.Or(DevTuning.PlayerMoveSpeed, playerDefault),
                v => DevTuning.PlayerMoveSpeed = v);

            Add("Robot move speed", "m/s", 0.5f, 12f, robotDefault,
                () => DevTuning.Or(DevTuning.RobotMoveSpeed, robotDefault),
                v => DevTuning.RobotMoveSpeed = v);

            Add("Boss move speed", "m/s", 0.5f, 12f, bossDefault,
                () => DevTuning.Or(DevTuning.BossMoveSpeed, bossDefault),
                v => DevTuning.BossMoveSpeed = v, tab: 2);

            Add("Max max-life", "hp", 25f, 500f, healthDefault,
                () => DevTuning.Or(DevTuning.PlayerMaxHealth, healthDefault),
                v =>
                {
                    DevTuning.PlayerMaxHealth = v;
                    var h = FindFirstObjectByType<PlayerHealth>();
                    if (h != null) h.RefreshMax();
                });

            Add("Water deplete rate", "/s", 0f, 60f, drainDefault,
                () => DevTuning.Or(DevTuning.BlasterDrainPerSecond, drainDefault),
                v => { DevTuning.BlasterDrainPerSecond = v; RefreshBlaster(); });

            Add("Water refill rate", "/s", 0f, 200f, regenDefault,
                () => DevTuning.Or(DevTuning.BlasterRegenPerSecond, regenDefault),
                v => { DevTuning.BlasterRegenPerSecond = v; RefreshBlaster(); });

            // Durability sliders (YT-126). Factory default is read off a live hutch so it tracks the
            // baked value; the boss default is its authored max. Both retune the live objects on the
            // frame the slider moves, exactly like the speed/life/water knobs.
            float factoryHpDefault = FactoryDefault();
            float bossHpDefault = BossTuning.Health;

            Add("Factory health", "hp", 50f, 1500f, factoryHpDefault,
                () => DevTuning.Or(DevTuning.FactoryHealth, factoryHpDefault),
                v =>
                {
                    DevTuning.FactoryHealth = v;
                    foreach (MowerHutch h in FactoryCensus.All) if (h != null) h.RefreshMax();
                });

            Add("Boss health", "hp", 500f, 8000f, bossHpDefault,
                () => DevTuning.Or(DevTuning.BossHealth, bossHpDefault),
                v =>
                {
                    DevTuning.BossHealth = v;
                    var b = FindFirstObjectByType<BigBermudaBoss>();
                    if (b != null) b.RefreshMax();
                }, tab: 2);

            // ---- Invasion Level / escalation (YT-181): the DifficultyDirector reads every one of
            // these live, so a moved slider retimes the escalation mid-run — no push needed, same
            // contract as the pacing knobs above.
            Add("Escalation start", "lvl", 0f, 5f, DifficultyDirector.AuthoredStart,
                () => DevTuning.Or(DevTuning.EscalationStart, DifficultyDirector.AuthoredStart),
                v => DevTuning.EscalationStart = v);

            Add("Escalation rate", "lvl/s", 0f, 0.5f, DifficultyDirector.AuthoredRatePerSecond,
                () => DevTuning.Or(DevTuning.EscalationRate, DifficultyDirector.DerivedRatePerSecond),
                v => DevTuning.EscalationRate = v);

            // YT-210: the run is now a bounded ~6-minute clock. Run length is the authored knob;
            // the rate above derives from it unless explicitly pinned.
            Add("Run length", "s", 30f, 900f, DifficultyDirector.AuthoredRunLengthSeconds,
                () => DevTuning.Or(DevTuning.RunLengthSeconds, DifficultyDirector.AuthoredRunLengthSeconds),
                v => DevTuning.RunLengthSeconds = v);

            Add("Shed clock skip", "s", 0f, 300f, DifficultyDirector.AuthoredPerShedBump,
                () => DevTuning.Or(DevTuning.EscalationPerShedBump, DifficultyDirector.AuthoredPerShedBump),
                v => DevTuning.EscalationPerShedBump = v);

            Add("Escalation max", "lvl", 1f, 30f, DifficultyDirector.AuthoredMax,
                () => DevTuning.Or(DevTuning.EscalationMax, DifficultyDirector.AuthoredMax),
                v => DevTuning.EscalationMax = v);

            // ---- Death-throes surge (YT-182): the wreck's last wave. Left alone, both numbers scale
            // with the Invasion Level above; a pinned value here replaces that curve outright with a
            // flat number, same contract as "Spawn interval" pinning the cadence ramp.
            Add("Surge burst", "bots", 0f, 10f, EnemySpawner.DeathSurgeBurstMax,
                () => DevTuning.Or(DevTuning.DeathSurgeBurstSize, EnemySpawner.DeathSurgeBurstMax),
                v => DevTuning.DeathSurgeBurstSize = v);

            Add("Surge elite chance", "x", 0f, 1f, EnemySpawner.DeathSurgeEliteChanceMax,
                () => DevTuning.Or(DevTuning.DeathSurgeEliteChance, EnemySpawner.DeathSurgeEliteChanceMax),
                v => DevTuning.DeathSurgeEliteChance = v);

            // ---- Swarm pacing (YT-194): the front-of-curve fix for "overrun at 0, dominant once
            // armed" — a couple of robots at run start (not a swarm), an intuitive production unit,
            // and a real toughness knob now that the field-wide cap (YT-186) means late-game danger
            // has to come from durability rather than raw numbers. All three read live: EnemySpawner
            // re-derives its cap/interval on every check, and a freshly spawned or reused robot picks
            // up the health multiplier the moment SpawnKind builds its archetype.
            float startingRobotsDefault = StartingRobotsDefault();
            float productionDefault = ProductionPerMinuteDefault();

            Add("Starting robots", "bots", 0f, 10f, startingRobotsDefault,
                () => DevTuning.Or(DevTuning.StartingRobots, startingRobotsDefault),
                v => DevTuning.StartingRobots = v);

            Add("Production/min", "bots/m", 5f, 120f, productionDefault,
                () => DevTuning.Or(DevTuning.RobotProductionPerMinute, productionDefault),
                v => DevTuning.RobotProductionPerMinute = v);

            Add("Robot health", "x", 0.5f, 3f, EnemySpawner.DefaultRobotHealthMultiplier,
                () => DevTuning.Or(DevTuning.RobotHealthMultiplier, EnemySpawner.DefaultRobotHealthMultiplier),
                v => DevTuning.RobotHealthMultiplier = v);

            // Spawn cadence (YT-170). Reads live: EnemySpawner pulls CurrentInterval fresh on every
            // check, so the slider retimes every factory's emergence with no push needed. Lives on
            // the Gameplay tab (YT-196 audit): it's a robots/arena knob, same group as the two above.
            Add("Spawn interval", "s", 0.3f, 4f, SpawnIntervalDefault(),
                () => DevTuning.Or(DevTuning.SpawnInterval, SpawnIntervalDefault()),
                v => DevTuning.SpawnInterval = v);

            // ---- Weapons tab (YT-138): the upgrade-part magnitudes + the pacing/Hydro tunables. ----
            // Nozzle/range/harness re-fit the live weapon (RefreshUpgrades); the rest read live.
            Add("Nozzle narrowing", "x", 0.3f, 1f, UpgradeCatalog.NozzleConeMultiplier,
                () => DevTuning.Or(DevTuning.NozzleConeMultiplier, UpgradeCatalog.NozzleConeMultiplier),
                v => { DevTuning.NozzleConeMultiplier = v; RefreshUpgrades(); }, tab: 1);

            Add("Power reach", "m", 0f, 8f, UpgradeCatalog.PowerRangeBonus,
                () => DevTuning.Or(DevTuning.PowerNozzleRange, UpgradeCatalog.PowerRangeBonus),
                v => { DevTuning.PowerNozzleRange = v; RefreshUpgrades(); }, tab: 1);

            Add("Extender reach", "m", 0f, 8f, UpgradeCatalog.RangeExtenderBonus,
                () => DevTuning.Or(DevTuning.RangeExtenderBonus, UpgradeCatalog.RangeExtenderBonus),
                v => { DevTuning.RangeExtenderBonus = v; RefreshUpgrades(); }, tab: 1);

            Add("Wide-bore widen", "x", 1f, 5f, UpgradeCatalog.WideBoreConeMultiplier,
                () => DevTuning.Or(DevTuning.WideBoreConeMultiplier, UpgradeCatalog.WideBoreConeMultiplier),
                v => { DevTuning.WideBoreConeMultiplier = v; RefreshUpgrades(); }, tab: 1);

            Add("Harness capacity", "wtr", 0f, 150f, UpgradeCatalog.HarnessCapacityBonus,
                () => DevTuning.Or(DevTuning.HarnessCapacity, UpgradeCatalog.HarnessCapacityBonus),
                v => { DevTuning.HarnessCapacity = v; RefreshUpgrades(); }, tab: 1);

            Add("Engine boost", "x", 1f, 2.5f, UpgradeCatalog.AccelSpeedMultiplier,
                () => DevTuning.Or(DevTuning.AccelSpeed, UpgradeCatalog.AccelSpeedMultiplier),
                v => DevTuning.AccelSpeed = v, tab: 1);

            Add("Primary drain", "/m", 0f, 30f, WaterBlaster.DefaultPrimaryCellsPerMin,
                () => DevTuning.Or(DevTuning.PrimaryCellsPerMin, WaterBlaster.DefaultPrimaryCellsPerMin),
                v => DevTuning.PrimaryCellsPerMin = v, tab: 1);

            // Spray knockback recut (WV-225): near-zero cosmetic stagger, not the old launch.
            Add("Spray knockback", "m/s", 0f, 5f, WaterBlaster.DefaultSprayKnockback,
                () => DevTuning.Or(DevTuning.SprayKnockback, WaterBlaster.DefaultSprayKnockback),
                v => DevTuning.SprayKnockback = v, tab: 1);

            // Secondary/special don't exist yet (WV-231 builds Water Balloon/Dash/Teleport) — these
            // are settings only for now, ready for that ticket to spend (WV-227's economy spec §5/§9).
            Add("Secondary cost", "cells", 0f, 10f, CellEconomyTuning.DefaultSecondaryCellsPerUse,
                () => DevTuning.Or(DevTuning.SecondaryCellsPerUse, CellEconomyTuning.DefaultSecondaryCellsPerUse),
                v => DevTuning.SecondaryCellsPerUse = v, tab: 1);

            Add("Special cost", "cells", 0f, 10f, CellEconomyTuning.DefaultSpecialAbilityCellsPerUse,
                () => DevTuning.Or(DevTuning.SpecialAbilityCellsPerUse, CellEconomyTuning.DefaultSpecialAbilityCellsPerUse),
                v => DevTuning.SpecialAbilityCellsPerUse = v, tab: 1);

            Add("Power efficiency", "x", 0f, 0.3f, CellEconomyTuning.DefaultPowerEfficiencyReductionPerLevel,
                () => DevTuning.Or(DevTuning.PowerEfficiencyReductionPerLevel, CellEconomyTuning.DefaultPowerEfficiencyReductionPerLevel),
                v => DevTuning.PowerEfficiencyReductionPerLevel = v, tab: 1);

            Add("Weakened damage", "x", 1f, 3f, PlayerHealth.DefaultWeakenedDamageMultiplier,
                () => DevTuning.Or(DevTuning.WeakenedDamageMultiplier, PlayerHealth.DefaultWeakenedDamageMultiplier),
                v => DevTuning.WeakenedDamageMultiplier = v, tab: 1);

            Add("Hydro burst", "s", 2f, 30f, HydroBurst.AuthoredSeconds,
                () => DevTuning.Or(DevTuning.HydroBurstSeconds, HydroBurst.AuthoredSeconds),
                v => DevTuning.HydroBurstSeconds = v, tab: 1);

            Add("Hydro cooldown", "s", 5f, 90f, HydroBurst.AuthoredCooldown,
                () => DevTuning.Or(DevTuning.HydroBurstCooldown, HydroBurst.AuthoredCooldown),
                v => DevTuning.HydroBurstCooldown = v, tab: 1);

            Add("Cell capacity", "cells", 5f, 60f, PickupWallet.DefaultCapacity,
                () => DevTuning.Or(DevTuning.PowerCellCapacity, PickupWallet.DefaultCapacity),
                v => DevTuning.PowerCellCapacity = v, tab: 1);

            Add("Part pacing", "kills", 1f, 8f, PickupDirector.DefaultPartInterval,
                () => DevTuning.Or(DevTuning.PartDropInterval, PickupDirector.DefaultPartInterval),
                v => DevTuning.PartDropInterval = v, tab: 1);

            // Rusher cell-drop chance (YT-171): the common kill's trickle toward the Hydro reserve,
            // separate from the bruiser's guaranteed drop.
            Add("Cell drop chance", "x", 0f, 1f, PickupDirector.DefaultCellDropChance,
                () => DevTuning.Or(DevTuning.PowerCellDropChance, PickupDirector.DefaultCellDropChance),
                v => DevTuning.PowerCellDropChance = v, tab: 1);

            // ---- Boss tab (YT-157): the brood volley — Big Bermuda's side-hatch add-spawner. ----
            // All read live: the volley pulls each number through DevTuning the next time it fires, so a
            // slider retimes or re-sizes the wave mid-fight with no push.
            Add("Add volley interval", "s", 2f, 20f, BossTuning.VolleyInterval,
                () => DevTuning.Or(DevTuning.BossVolleyInterval, BossTuning.VolleyInterval),
                v => DevTuning.BossVolleyInterval = v, tab: 2);

            Add("Adds per volley", "bots", 1f, 8f, BossTuning.RobotsPerVolley,
                () => DevTuning.Or(DevTuning.BossAddsPerVolley, BossTuning.RobotsPerVolley),
                v => DevTuning.BossAddsPerVolley = v, tab: 2);

            Add("Max adds alive", "bots", 1f, 20f, BossTuning.MaxConcurrentAdds,
                () => DevTuning.Or(DevTuning.BossMaxAdds, BossTuning.MaxConcurrentAdds),
                v => DevTuning.BossMaxAdds = v, tab: 2);

            Add("Volley windup", "s", 0.3f, 3f, BossTuning.VolleyWindup,
                () => DevTuning.Or(DevTuning.BossVolleyWindup, BossTuning.VolleyWindup),
                v => DevTuning.BossVolleyWindup = v, tab: 2);
        }

        /// <summary>The authored factory HP for the 100% reference: a live hutch's if the level has
        /// one, else the shipped default so the panel still works in a bare test scene.</summary>
        private static float FactoryDefault()
        {
            foreach (MowerHutch h in FactoryCensus.All) if (h != null) return h.AuthoredMax;
            return 1501.5f;
        }

        /// <summary>The pinned Spawn-interval override's 100% reference (YT-170, decoupled from
        /// Production/min's reference in YT-200 — see <see cref="EnemySpawner.DefaultSpawnIntervalPin"/>).
        /// A fixed authored constant, not read off the live scene: unlike Production/min this knob
        /// pins an exact flat number rather than tracking the level's spawner, so there's no live
        /// value to follow.</summary>
        private static float SpawnIntervalDefault() => EnemySpawner.DefaultSpawnIntervalPin;

        /// <summary>The authored starting-robot count for the 100% reference (YT-194): a live
        /// factory's if the level has one, else the shipped default. Same fallback shape as
        /// <see cref="SpawnIntervalDefault"/>.</summary>
        private static float StartingRobotsDefault()
        {
            foreach (MowerHutch h in FactoryCensus.All)
            {
                if (h == null) continue;
                var spawner = h.GetComponent<EnemySpawner>();
                if (spawner != null) return spawner.AuthoredStartingRobots;
            }
            return 0f;
        }

        /// <summary>The authored steady-state production rate in robots/minute for the 100%
        /// reference (YT-194): a live factory's if the level has one, else the shipped default so the
        /// panel still works in a bare test scene. Was the same underlying number as
        /// <see cref="SpawnIntervalDefault"/> before YT-200 decoupled the two.</summary>
        private static float ProductionPerMinuteDefault()
        {
            foreach (MowerHutch h in FactoryCensus.All)
            {
                if (h == null) continue;
                var spawner = h.GetComponent<EnemySpawner>();
                if (spawner != null) return spawner.AuthoredProductionPerMinute;
            }
            return 60f / 12f;
        }

        private void Add(string name, string unit, float min, float max, float def,
                         Func<float> get, Action<float> set, int tab = 0)
        {
            _knobs.Add(new Knob
            {
                Name = name, Unit = unit, Min = min, Max = max, Default = def, Get = get, Set = set, Tab = tab,
            });
        }

        private static void RefreshBlaster()
        {
            var b = FindFirstObjectByType<WaterBlaster>();
            if (b != null) b.RefreshDevTuning();
        }

        /// <summary>Re-fit the live weapon to a changed upgrade magnitude (YT-138) — so sliding the
        /// nozzle/reach/capacity re-shapes an already-installed part on the frame you move it.</summary>
        private static void RefreshUpgrades()
        {
            var b = FindFirstObjectByType<WaterBlaster>();
            if (b != null) b.RefreshUpgrades();
        }

        // ------------------------------------------------------------------ widgets

        /// <summary>A full-screen dark scrim behind the panel: it dims the game so a small settings
        /// panel reads as a modal, and it swallows every tap outside the panel so tuning can never
        /// drive Max around underneath. Only present while the panel is open.</summary>
        private void BuildScrim()
        {
            var rt = NewRect("Scrim", _safeRoot, Vector2.zero, Vector2.one);
            Stretch(rt);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = HudTextures.Solid();
            img.color = Scrim;
            img.raycastTarget = true;
            // Tap outside the panel to dismiss — the phone-native gesture.
            var btn = rt.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => SetOpen(false));
            _scrim = rt.gameObject;
        }

        private void BuildGearButton()
        {
            // Left edge, vertically centred: the one region nothing else claims. Top-left is the FPS
            // readout and utility icons, top-right the ability slots, both bottom corners the sticks.
            var rt = NewRect("Gear", _safeRoot, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(GearSize, GearSize);
            rt.anchoredPosition = new Vector2(Pad + GearSize * 0.5f, 0f);

            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = HudTextures.Disc();
            img.color = new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.78f);
            img.raycastTarget = true;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => SetOpen(!_open));

            // A concentric-ring dial for the icon rather than a ⚙ glyph: the HUD renders in
            // LegacyRuntime.ttf, which doesn't carry the gear codepoint, so a glyph would leave an
            // empty box on device. TechRings is the same icon language the joysticks use, and reads
            // clearly as an adjustable control.
            var icon = AddImage(rt, HudTextures.TechRings(96, 3), Accent, "Icon");
            Stretch(icon.rectTransform);
            icon.raycastTarget = false;
        }

        private void BuildPanel()
        {
            var rt = NewRect("Panel", _safeRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            rt.sizeDelta = new Vector2(PanelW, PanelH);
            // The pivot stays top-left because every child is placed by Place() in top-left space, so
            // centring is done by offsetting half the panel up and left rather than moving the pivot.
            rt.anchoredPosition = new Vector2(-PanelW * 0.5f, PanelH * 0.5f);
            _panelRoot = rt.gameObject;

            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = HudTextures.RoundedBox();
            bg.type = Image.Type.Sliced;
            bg.color = PanelColor;
            bg.raycastTarget = true;

            float y = -Pad;

            // Header: title on the left, the Gameplay/Weapons tabs on the right of the same row so the
            // paging costs no vertical space on a phone (YT-138).
            var header = AddText(rt, "SETTINGS", HeaderFont, Accent, TextAnchor.MiddleLeft);
            Place(header.rectTransform, Pad, y, PanelW * 0.34f, HeaderH);

            const float TabW = 168f, TabGap = 8f;
            _pages = new GameObject[TabNames.Length];
            _tabButtons = new Button[TabNames.Length];
            for (int t = 0; t < TabNames.Length; t++)
            {
                float tx = PanelW - Pad - (TabNames.Length - t) * (TabW + TabGap) + TabGap;
                int captured = t;
                _tabButtons[t] = BuildButton(rt, TabNames[t], tx, y, TabW, HeaderH);
                _tabButtons[t].onClick.AddListener(() => ShowTab(captured));
            }
            y -= HeaderH + 8f;

            // One container per tab; each holds its own grid. The Gameplay tab has the most knobs at
            // two columns (10 => five rows), which is the phone-height ceiling (YT-126) — the footer
            // sits below that height for every tab. A tab that outgrows 10 (YT-171's Weapons knob)
            // spills into a THIRD column instead of a sixth row, so it stays inside that same ceiling;
            // a tab that outgrows 15 (YT-194's Gameplay knobs, YT-192) spills into a FOURTH column
            // instead of a sixth row, for the same reason — it's what keeps the footer/dump below it
            // inside the panel. The untouched two- and three-column tabs render pixel-identical to
            // before.
            float dumpColW = (PanelW - Pad * 2f - ColGap * 2f) / 3f;
            int maxRows = 0;
            for (int t = 0; t < TabNames.Length; t++)
            {
                var page = NewRect($"Page {TabNames[t]}", rt, new Vector2(0f, 1f), new Vector2(0f, 1f));
                Place(page, 0f, y, PanelW, RowH * 5f);
                _pages[t] = page.gameObject;

                var rows = _knobs.FindAll(k => k.Tab == t);
                int cols = rows.Count > 15 ? 4 : rows.Count > 10 ? 3 : 2;
                float colW = (PanelW - Pad * 2f - ColGap * (cols - 1)) / cols;
                int rowsPerCol = Mathf.Max(1, Mathf.CeilToInt(rows.Count / (float)cols));
                maxRows = Mathf.Max(maxRows, rowsPerCol);
                for (int i = 0; i < rows.Count; i++)
                {
                    int col = i / rowsPerCol;
                    int row = i % rowsPerCol;
                    float x = Pad + col * (colW + ColGap);
                    BuildKnobRow(rows[i], page, x, -row * RowH, colW);
                }
            }

            float gridH = RowH * maxRows;
            float footerY = y - gridH - 12f;

            // Save settings (YT-201) leads the footer — it's the one action that outlives the
            // session — then the original three, untouched at their proven widths, just shifted
            // right by the new button's footprint.
            const float afterSave = Pad + SaveBtnW + SaveBtnGap;

            var save = BuildButton(rt, "Save settings", Pad, footerY, SaveBtnW, ButtonH, primary: true);
            save.onClick.AddListener(SaveSettings);

            var copy = BuildButton(rt, "Copy current values", afterSave, footerY, 380f, ButtonH, primary: true);
            copy.onClick.AddListener(CopyValues);

            var reset = BuildButton(rt, "Reset to defaults", afterSave + 380f + 16f, footerY, 300f, ButtonH);
            reset.onClick.AddListener(ResetValues);

            var close = BuildButton(rt, "Close", afterSave + 380f + 16f + 300f + 16f, footerY, 200f, ButtonH);
            close.onClick.AddListener(() => SetOpen(false));

            // Three-column dump (YT-126, YT-192): keeps every line on the panel without pushing it
            // off a phone. Left/middle/right thirds of the value list, side by side.
            float dumpY = footerY - ButtonH - 8f;
            _dumpTextL = AddText(rt, "", DumpFont, TextColor, TextAnchor.UpperLeft);
            Place(_dumpTextL.rectTransform, Pad, dumpY, dumpColW, DumpH);
            _dumpTextL.verticalOverflow = VerticalWrapMode.Truncate;

            _dumpTextM = AddText(rt, "", DumpFont, TextColor, TextAnchor.UpperLeft);
            Place(_dumpTextM.rectTransform, Pad + dumpColW + ColGap, dumpY, dumpColW, DumpH);
            _dumpTextM.verticalOverflow = VerticalWrapMode.Truncate;

            _dumpTextR = AddText(rt, "", DumpFont, TextColor, TextAnchor.UpperLeft);
            Place(_dumpTextR.rectTransform, Pad + (dumpColW + ColGap) * 2f, dumpY, dumpColW, DumpH);
            _dumpTextR.verticalOverflow = VerticalWrapMode.Truncate;

            ShowTab(0);   // start on the Gameplay tab
        }

        private void BuildKnobRow(Knob k, RectTransform parent, float x, float y, float w)
        {
            var row = NewRect(k.Name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f));
            Place(row, x, y, w, RowH);

            float labelW = w - KnobValueW - KnobGap;

            var name = AddText(row, k.Name, KnobLabelFont, TextColor, TextAnchor.MiddleLeft);
            Place(name.rectTransform, 0f, 0f, labelW, 34f);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;   // bounded — never bleeds into the value zone

            k.Value = AddText(row, "", KnobLabelFont, Accent, TextAnchor.MiddleRight);
            Place(k.Value.rectTransform, w - KnobValueW, 0f, KnobValueW, 34f);
            k.Value.horizontalOverflow = HorizontalWrapMode.Wrap;

            k.Slider = BuildSlider(row, 0f, -40f, w, SliderH, k);
            UpdateValueText(k);
        }

        private Slider BuildSlider(RectTransform parent, float x, float y, float w, float h, Knob k)
        {
            var rt = NewRect("Slider", parent, new Vector2(0f, 1f), new Vector2(0f, 1f));
            Place(rt, x, y, w, h);
            var slider = rt.gameObject.AddComponent<Slider>();

            var track = NewRect("Track", rt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
            track.sizeDelta = new Vector2(0f, 14f);
            track.anchoredPosition = Vector2.zero;
            var trackImg = track.gameObject.AddComponent<Image>();
            trackImg.sprite = HudTextures.RoundedBox(24, 0.5f);
            trackImg.type = Image.Type.Sliced;
            trackImg.color = TrackColor;

            var fillArea = NewRect("Fill Area", rt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
            fillArea.sizeDelta = new Vector2(-HandleW, 14f);
            fillArea.anchoredPosition = Vector2.zero;
            var fill = NewRect("Fill", fillArea, new Vector2(0f, 0f), new Vector2(0f, 1f));
            fill.sizeDelta = new Vector2(HandleW, 0f);
            var fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.sprite = HudTextures.RoundedBox(24, 0.5f);
            fillImg.type = Image.Type.Sliced;
            fillImg.color = Accent;

            var handleArea = NewRect("Handle Slide Area", rt, new Vector2(0f, 0f), new Vector2(1f, 1f));
            handleArea.sizeDelta = new Vector2(-HandleW, 0f);
            handleArea.anchoredPosition = Vector2.zero;
            var handle = NewRect("Handle", handleArea, new Vector2(0f, 0f), new Vector2(0f, 1f));
            handle.sizeDelta = new Vector2(HandleW, 0f);
            var handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.sprite = HudTextures.Disc();
            handleImg.color = Color.white;

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            // Piecewise-normalised (YT-205): the slider always runs 0..1 with the default pinned to
            // the visual middle, so every knob centres regardless of where Default sits in [Min,Max].
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.SetValueWithoutNotify(ValueToPos(k.Min, k.Max, k.Default, Mathf.Clamp(k.Get(), k.Min, k.Max)));
            slider.onValueChanged.AddListener(pos =>
                { k.Set(PosToValue(k.Min, k.Max, k.Default, pos)); UpdateValueText(k); });

            // The real Min/Max/Default the slider maps to isn't recoverable from the slider itself
            // any more (it only knows its own 0..1 position) — park it alongside for anything outside
            // this method, including tests, that needs to convert a real value to a position or back.
            var range = rt.gameObject.AddComponent<SliderRange>();
            range.Min = k.Min;
            range.Max = k.Max;
            range.Default = k.Default;

            return slider;
        }

        /// <summary>Real-value bounds for a normalised slider (YT-205), attached to the same
        /// GameObject as its <see cref="Slider"/>.</summary>
        public sealed class SliderRange : MonoBehaviour
        {
            public float Min;
            public float Max;
            public float Default;
        }

        /// <summary>Slider position (0..1, 0.5 = default) → the knob's real value. Piecewise-linear
        /// around <c>def</c> so dragging left of centre always lowers the value and right always
        /// raises it, symmetrically, no matter where the default sits in [min,max] (YT-205).</summary>
        public static float PosToValue(float min, float max, float def, float pos) => pos <= 0.5f
            ? Mathf.Lerp(min, def, pos / 0.5f)
            : Mathf.Lerp(def, max, (pos - 0.5f) / 0.5f);

        /// <summary>Inverse of <see cref="PosToValue"/>: a real value → its slider position. Guards the
        /// degenerate half where the default equals min or max (e.g. Starting robots, Default==Min==0)
        /// by pinning that side at 0.5 rather than dividing by zero.</summary>
        public static float ValueToPos(float min, float max, float def, float value) => value <= def
            ? (def > min ? 0.5f * (value - min) / (def - min) : 0.5f)
            : (max > def ? 0.5f + 0.5f * (value - def) / (max - def) : 0.5f);

        private Button BuildButton(RectTransform parent, string label, float x, float y,
                                   float w, float h, bool primary = false)
        {
            var rt = NewRect(label, parent, new Vector2(0f, 1f), new Vector2(0f, 1f));
            Place(rt, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = HudTextures.RoundedBox();
            img.type = Image.Type.Sliced;
            img.color = primary ? AccentDeep : new Color(1f, 1f, 1f, 0.14f);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var text = AddText(rt, label, LabelFont, TextColor, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            return btn;
        }

        // ------------------------------------------------------------------ behaviour

        private void SetOpen(bool open)
        {
            _open = open;
            if (_panelRoot != null) _panelRoot.SetActive(open);
            if (_scrim != null) _scrim.SetActive(open);
        }

        /// <summary>Show one tab's page and light its button (YT-138).</summary>
        private void ShowTab(int tab)
        {
            _tab = tab;
            for (int t = 0; t < _pages.Length; t++)
            {
                if (_pages[t] != null) _pages[t].SetActive(t == tab);
                if (_tabButtons[t] != null && _tabButtons[t].targetGraphic is Image img)
                    img.color = t == tab ? AccentDeep : new Color(1f, 1f, 1f, 0.10f);
            }
        }

        private void UpdateValueText(Knob k)
        {
            if (k.Value == null) return;
            // Percent of the authored default only (YT-190) — the raw number + unit doesn't fit the
            // value's fixed-width zone next to a long label without the two overlapping. The exact
            // raw value (what gets pasted back into the source) is still in "Copy current values".
            // Derived from the slider's own normalised position (YT-205), so it reads 100% at centre
            // for every knob — including ones like Starting robots where Default==Min==0, which used
            // to divide by zero and stick at 0% (YT-206).
            float pct = k.Slider != null ? k.Slider.value * 200f : 100f;
            k.Value.text = $"{pct:0}%";
        }

        /// <summary>
        /// Dump every value as <c>name: value</c> text. Goes three places on purpose: the clipboard
        /// (convenient, but <c>systemCopyBuffer</c> isn't dependable under a WebGL security prompt),
        /// the panel itself (always works — Lee can read or screenshot it), and the log.
        /// </summary>
        private void CopyValues()
        {
            // The block for the clipboard and the log — one line per knob on the CURRENT tab, header
            // first. Per-tab (YT-138) so seventeen knobs across two tabs never overrun the on-panel
            // dump: you copy the page you're tuning.
            var lines = new List<string> { $"# MAX tuning ({TabNames[_tab]}) — " + Application.version };
            foreach (var k in _knobs)
            {
                if (k.Tab != _tab) continue;
                float v = k.Get();
                float pct = k.Slider != null ? k.Slider.value * 200f : 100f;
                lines.Add($"{k.Name}: {v:0.##} {k.Unit}  (default {k.Default:0.##}, {pct:0}%)");
            }

            string dump = string.Join("\n", lines);
            GUIUtility.systemCopyBuffer = dump;
            Debug.Log("[Settings]\n" + dump);

            // Split across the three on-panel columns so all of it stays inside the panel on a phone
            // (YT-192: two columns ran the Gameplay tab's 17 knobs past DumpH).
            int third = Mathf.CeilToInt(lines.Count / 3f);
            int firstEnd = Mathf.Min(third, lines.Count);
            int secondEnd = Mathf.Min(third * 2, lines.Count);
            if (_dumpTextL != null) _dumpTextL.text = string.Join("\n", lines.GetRange(0, firstEnd));
            if (_dumpTextM != null)
                _dumpTextM.text = string.Join("\n", lines.GetRange(firstEnd, secondEnd - firstEnd));
            if (_dumpTextR != null)
                _dumpTextR.text = string.Join("\n", lines.GetRange(secondEnd, lines.Count - secondEnd));
        }

        /// <summary>Persist the current tuning app-wide (YT-201): every game — the home screen,
        /// existing saves, brand-new ones — inherits it from the next launch on, via
        /// <see cref="DevTuning.Save"/> and the auto-load that runs before any scene does.</summary>
        private void SaveSettings()
        {
            DevTuning.Save();
            if (_dumpTextL != null) _dumpTextL.text = "Settings saved.\nApplies app-wide from the\nnext launch on.";
            if (_dumpTextM != null) _dumpTextM.text = "";
            if (_dumpTextR != null) _dumpTextR.text = "";
        }

        private void ResetValues()
        {
            // Push the authored value back through each knob's own setter first, so whatever cached
            // it (camera offset, energy pool) is re-seeded by the same path a slider move uses. Only
            // then drop the overrides — after which Or() returns the authored constant and they agree.
            foreach (var k in _knobs) k.Set(k.Default);
            DevTuning.Reset();
            // Also drop the persisted save (YT-201 AC) — otherwise the next launch would quietly
            // reload the old numbers this button just backed away from.
            DevTuning.ClearSaved();

            foreach (var k in _knobs)
            {
                if (k.Slider != null) k.Slider.SetValueWithoutNotify(0.5f); // Default always centres (YT-205)
                UpdateValueText(k);
            }
            if (_dumpTextL != null) _dumpTextL.text = "";
            if (_dumpTextM != null) _dumpTextM.text = "";
            if (_dumpTextR != null) _dumpTextR.text = "";
        }

        // ------------------------------------------------------------------ uGUI helpers

        private static RectTransform NewRect(string name, Transform parent, Vector2 anchorMin,
                                             Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0f, 1f);
            return rt;
        }

        /// <summary>Place a top-left-pivoted rect at (x, y) in its parent's top-left space.</summary>
        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Image AddImage(Transform parent, Sprite sprite, Color color, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        private static Text AddText(Transform parent, string content, int size, Color color,
                                    TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = HudFont.Get();
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Reference-unit → point scale on the 6-inch target, for the layout test.</summary>
        public static float PhoneScale => Scale6Inch;

        /// <summary>Smallest font in the panel, in reference units. The test converts it.</summary>
        public static int SmallestFont => DumpFont;
    }
}
