using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Player;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Dev;
using MaxWorlds.Enemies;
using MaxWorlds.Upgrades;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.UI
{
    /// <summary>
    /// In-run HUD (YT-30). Builds the entire combat interface in code — status strip,
    /// utility icons, ability slots with cooldown radials, tech-ring joysticks, arena
    /// indicator, boss bar, and floating combat text — per the Art Direction &amp; UI HUD
    /// spec, with the Backyard biome's warm tint. No prefab or inspector wiring: it finds
    /// the live systems (<see cref="PlayerHealth"/>, <see cref="WaterBlaster"/>,
    /// <see cref="PlayerController"/>) by type, so it runs headlessly in CI and shows up on
    /// the WebGL play link. HP and Energy bind to the real components; XP, abilities, arena
    /// progress and the boss are driven off kills through <see cref="HudModel"/> (the real
    /// economy/factory/boss systems are later tickets).
    /// </summary>
    public sealed class HudController : MonoBehaviour
    {
        /// <summary>
        /// MV-259: test-only escape hatch. The on-screen touch controls back onto a shared virtual
        /// Gamepad device (Unity's <c>OnScreenControl</c>) that <c>InputTestFixture</c>-based tests
        /// cannot cleanly tear down across a scene reload — an engine-level device-lifecycle
        /// conflict unrelated to anything this flag's callers are testing. Touch input itself is
        /// covered by <c>TouchControlsPlayTests</c>, which does not derive from
        /// <c>InputTestFixture</c> and leaves this false. Defaults false so real play is unaffected.
        /// </summary>
        public static bool SkipTouchControlsForTests = false;

        // Backyard palette (Art Direction §Colour identity + HUD spec).
        private static readonly Color HpColor = new Color(0.90f, 0.22f, 0.20f);
        // Golden — used by kill-reward floating text (SPARKS, damage crits, "FACTORY DOWN").
        private static readonly Color XpColor = new Color(0.957f, 0.788f, 0.365f); // #F4C95D golden
        private static readonly Color TechRingColor = new Color(0.31f, 0.76f, 0.97f);
        private static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.09f, 0.55f);
        private static readonly Color ReadyGlow = new Color(1f, 0.85f, 0.35f);
        private static readonly Color BiomeTint = new Color(0.96f, 0.62f, 0.20f, 0.06f); // warm orange overlay
        private static readonly Color BossColor = new Color(0.85f, 0.12f, 0.12f);
        // MV-588: amber, distinct from the boss's own red — reads as a separate readout, not a second
        // health bar.
        private static readonly Color SpawnLevelColor = new Color(0.95f, 0.65f, 0.15f);
        private static readonly Color BoneWhite = new Color(0.96f, 0.94f, 0.86f);
        // Robot-drop colours (YT-131): cyan power cell — matched to the world pickup's cyan core.
        private static readonly Color CellColor = new Color(0.31f, 0.86f, 0.98f);
        /// <summary>The Hydro burst button's colour (YT-215): the same cyan as the power-cell
        /// counter/pickup — the burst spends the exact same resource, so button and meter read as
        /// one system rather than two unrelated cyans.</summary>
        private static readonly Color HydroColor = CellColor;
        /// <summary>The Force Field button's colour (MV-361) — the same ready-cyan the bubble itself
        /// glows, so the button and the shield read as one thing.</summary>
        private static readonly Color ForceFieldColor = new Color(0.31f, 0.76f, 0.97f);
        /// <summary>MV-543: test-only read of <see cref="ForceFieldColor"/> — a contrast-ratio test
        /// needs the real ring colour the button renders, not a hand-copied duplicate.</summary>
        public static Color ForceFieldColorForTest => ForceFieldColor;
        /// <summary>MV-543: the Force Field %-remaining label's own ink — deliberately dark rather than
        /// the "near-white" a first instinct reaches for, because ForceFieldColor's luminance is
        /// mid-range and only a dark fill clears WCAG 4.5:1 against it (see the fixture in
        /// <c>BuildForceFieldButton</c>).</summary>
        private static readonly Color ForceFieldLabelInk = new Color(0.05f, 0.05f, 0.06f);
        /// <summary>The Sentinel deploy button's colour (MV-362, MV-422: one sentinel only) — the
        /// primary's own blue, since the turret is meant to read as Max's own tech ("a hose pipe on a
        /// stick").</summary>
        private static readonly Color SentinelColor = new Color(0.45f, 0.65f, 0.85f);
        // The Supercell-ready chip shares the on-ground collectible aura's colour (YT-147): the HUD tell
        // and the pickup it points at read as ONE language. Sourced from the constant the aura uses, not
        // a matched copy, so an art retune moves both at once. It is the shared ORANGE, deliberately NOT
        // the old gold (0.98,0.72,0.22) that read as yellow — the ticket's whole point.
        private static readonly Color SupercellColor = MaxWorlds.VFX.PickupArtDirector.CollectibleGlow;
        /// <summary>The WEAPONS button's idle-state ring (MV-425) — "deliberately recessive... it
        /// should disappear mid-fight," a thin cool grey rather than any of the amber/cyan alert hues.</summary>
        private static readonly Color WeaponsButtonIdleRingColor = new Color(0.55f, 0.58f, 0.62f, 1f);
        private const float RefW = 1920f, RefH = 1080f;

        // ---------- MV-606 HUD layout ----------
        // Central block for the handful of elements this reshuffle repositions (RIG, Teleport, Force
        // Field), so the next move of any one of them is a one-line change here instead of touching
        // several builder methods. Deliberately NOT a migration of the rest of the HUD — everything
        // else keeps its own inline literals until it, too, is next touched.

        /// <summary>RIG tap root (<see cref="BuildWeaponsButton"/>): top-right corner inset, replacing
        /// the retired B/U ability slots that used to occupy this corner.</summary>
        private const float RigCornerInset = 24f;

        /// <summary>Force Field button (<see cref="BuildForceFieldButton"/>): bottom of the left
        /// play-area column (MV-645) — MAP, the Settings/Controls gear, Water Balloon and Force Field
        /// all share X=150, stacked bottom-to-top directly above the Move stick.
        /// MV-676: was 345 (40px clear of the Move stick's own 250-unit top edge) — widened to a
        /// 52px clearance as part of easing the whole column's crowding. Not the full 55-60 the
        /// ticket sketched: at CanvasScaler's matchWidthOrHeight=0.5 blend, an iPhone-standard
        /// landscape aspect (~852x393pt) compresses the visible canvas to ~978 reference units tall,
        /// and widening every gap in this column to the top of its suggested range would push MAP's
        /// top edge inside the 20-unit safety margin MV676HudPhoneAspectMarginTests enforces — so all
        /// four gaps (this one plus the three below) were grown by the same ~12px so the column reads
        /// less crowded everywhere without any one element clipping on a narrow phone.</summary>
        private const float ForceFieldX = 150f;
        private const float ForceFieldRise = 357f;

        /// <summary>Teleport joystick (<see cref="RebuildTeleportJoystick"/>): tracks the right edge,
        /// horizontally centred on the aim stick's own centre line (same -150 offset — see
        /// <see cref="BuildJoysticks"/>), risen clear of the aim stick's full touch pad (half-size 100
        /// + 30px fat-finger margin = top edge 280) plus a visible gap, with margin to spare across the
        /// joystick's own level-driven size range.</summary>
        private const float TeleportX = -150f;
        private const float TeleportRise = 430f;

        private HudModel _model;
        private PlayerHealth _health;
        private PlayerController _player;
        private Camera _worldCamera;

        private Canvas _canvas;
        private RectTransform _safeRoot;
        private FloatingTextLayer _floating;

        // YT-54 presentation state. None of this feeds the model — it only animates what the model
        // already says.
        private readonly DamageNumberAggregator _damageNumbers = new DamageNumberAggregator();
        private readonly System.Collections.Generic.List<DamageNumberAggregator.Entry> _damageBuffer =
            new System.Collections.Generic.List<DamageNumberAggregator.Entry>(16);
        // The Hydro burst button (YT-215): hidden until UpgradeState.HydroAssembled, same TechRings
        // visual language the other ability controls use.
        private RectTransform _hydroButtonRoot;
        private Image _hydroGlow, _hydroRadial;
        private Text _hydroLabel;
        private bool _hydroWasReady;
        private bool _hydroWasActive;
        private float _hydroReadyFlash;
        private float _hydroSnapFlash;

        // The Force Field button (MV-361): hidden until AbilityKind.ForceField is acquired, same
        // round-button/radial-cooldown shape as the Hydro burst button above.
        private RectTransform _forceFieldButtonRoot;
        private Image _forceFieldGlow, _forceFieldRadial;
        private Text _forceFieldLabel;
        private bool _forceFieldWasReady;
        private bool _forceFieldWasActive;
        private float _forceFieldReadyFlash;

        // The Sentinel deploy joystick (MV-362, aimed-placement MV-399, one sentinel only MV-422):
        // hidden until AbilityKind.Sentinels is acquired, same tech-ring joystick shape Water
        // Balloon/Teleport use below — a sentinel isn't cooldown-gated, so the radial covers/uncovers
        // on cell cost + the deployment-slot cap instead of a cooldown sweep (same "empty bank reads
        // as covered" idiom Water Balloon's own radial already uses for its cell gate).
        private RectTransform _sentinelRoot;
        private AbilityControlArt.JoystickVisual _sentinelVisual;
        private Image _sentinelRadial;
        private Image _sentinelDeniedIcon;
        private int _sentinelBuiltLevel = -1;

        private RectTransform _attackModeToggleRoot;
        private Image _attackModeToggleBg;
        private Text _attackModeToggleLabel;
        private float _forceFieldSnapFlash;

        // Joysticks
        private Image _moveRings, _moveArrow;
        private RectTransform _moveKnob, _moveArrowRect;
        private Image _aimRings, _aimCross;
        private RectTransform _aimKnob;

        // Touch controls (YT-98): the joystick roots the on-screen sticks attach to.
        private RectTransform _moveJoystickRoot, _aimJoystickRoot;

        // Active-ability on-screen controls (WV-240, spec §6a): Water Balloon's joystick and a matching
        // Teleport joystick (MV-338). AbilityControlArt (WV-241) bakes size/brightness into
        // construction, so a level change rebuilds the control rather than tweening a property.
        private PlayerAbilities _abilities;
        private RectTransform _waterBalloonRoot;
        private AbilityControlArt.JoystickVisual _waterBalloonVisual;
        private Image _waterBalloonRadial;
        private int _waterBalloonBuiltLevel = -1;

        // The Auto-fire on/off toggle (MV-380 AC3): a small pill above the Water Balloon joystick,
        // shown only once AbilityKind.WaterBalloonAutoFire is acquired — the same "only alert when
        // actionable" idiom the rest of the HUD's conditional chrome follows.
        private RectTransform _waterBalloonAutoFireToggleRoot;
        private Image _waterBalloonAutoFireToggleBg;
        private Text _waterBalloonAutoFireToggleLabel;

        private RectTransform _teleportRoot;
        private AbilityControlArt.JoystickVisual _teleportVisual;
        private Image _teleportRadial;
        private int _teleportBuiltLevel = -1;

        // Arena indicator
        private Text _arenaLabel;
        private float _arenaProminence; // 1 = full, fades toward a faint idle

        // The MAP button (MV-563), replacing the always-on minimap this ticket removes outright — see
        // BuildMapButton.

        // The Invasion Dial (YT-197): a fill meter across the three escalation bands, so the whole
        // DifficultyDirector curve reads as a shape at a glance instead of a clock the player has
        // to interpret.
        private Image _dialFill;
        private Text _dialStageLabel;
        private Text _dialCaption;
        private DifficultyDirector.Stage? _shownStage;
        private float _dialStageFlash;

        // Boss
        private RectTransform _bossRoot;
        private Image _bossFill;
        private RectTransform _bossSegments;
        private Text _bossName;

        // Spawn-level bar (MV-588) — a second, thinner bar directly above the boss health bar
        private RectTransform _spawnLevelRoot;
        private Image _spawnLevelFill;

        // Warnings
        private Text _warning;
        private float _warningTimer;
        private float _bossIncomingTimer;

        // Robot drops (YT-131): banked power-cell counter. MV-510 moved this readout under THE
        // WEAPONS button's mark (see BuildPowerCellCounter) and gave it the MV-471 affordability
        // flash the old bare chip it replaced used to carry — hence _cellCounterRoot/Glow/Bg below,
        // the same shape UpdateRigCounters already drove for the part counter.
        private Text _cellCount;
        private Image _cellIcon;
        private float _cellPop;              // one-shot scale pop when a cell is banked
        private RectTransform _cellCounterRoot;
        private Image _cellCounterGlow, _cellCounterBg;

        // The always-available WEAPONS access button (YT-178), redrawn as THE RIG's own hexagonal
        // mark (MV-425, retiring both the old ABILITIES pill and the single-chip corner badge YT-131/
        // YT-178/MV-358 built up on it). _weaponsButtonRing is the state-coloured stroke (grey/amber/
        // module-cyan); _weaponsModuleHalo* are the module-captured state's double halo
        // (GlowRadiusMultiplier-style, only ever active for ModuleCaptured/Both); the two corner
        // badges are built and animated separately below. MV-581: the single tap target (and Button)
        // is now _weaponsTapRoot, an invisible parent sized to enclose both the hex mark AND the cell
        // readout beneath it (see BuildWeaponsButton) — _weaponsButtonRoot nests inside it, unchanged
        // in size, carrying only the visible sprites.
        private RectTransform _weaponsTapRoot;
        private RectTransform _weaponsButtonRoot;
        private Image _weaponsButtonRing;
        private Image _weaponsButtonMark;
        private RectTransform _weaponsModuleHaloRoot;
        private Image _weaponsModuleHaloOuter, _weaponsModuleHaloInner;

        private RectTransform _moduleBadgeRoot;
        private Image _moduleBadgeGlow, _moduleBadgeBg;
        private Text _moduleBadgeMark;

        // MV-519: the Supercell "definite pickup event" — a burst at the pickup point plus a "+10"
        // that flies to the cell readout, self-terminating (see SupercellPickupEffect). Replaces the
        // MV-471/MV-515 always-on "Rig Part Counter" chip, which is gone outright (no banked Supercell
        // tally left to show, no flashing edge icon left on screen between pickups).
        private RectTransform _supercellFxRoot;
        private Image _supercellFxBurst;
        private Text _supercellFxLabel;
        private bool _supercellFxActive;
        private float _supercellFxAge;
        private Vector2 _supercellFxStart, _supercellFxEnd;
        private int _supercellFxFromCells, _supercellFxToCells;

        private void Awake()
        {
            _health = FindFirstObjectByType<PlayerHealth>();
            _player = FindFirstObjectByType<PlayerController>();
            _abilities = FindFirstObjectByType<PlayerAbilities>();
            _worldCamera = Camera.main;
            _model = new HudModel();

            BuildCanvas();
            BuildBiomeTint();
            BuildUtilityIcons();
            BuildHomeButton();
            BuildHydroButton();
            BuildForceFieldButton();
            BuildSentinelJoystick();
            BuildAttackModeToggle();
            BuildWaterBalloonJoystick();
            BuildWaterBalloonAutoFireToggle();
            BuildTeleportJoystick();
            BuildJoysticks();
            BuildArenaIndicator();
            BuildInvasionDial();
            BuildBossBar();
            BuildSpawnLevelBar();
            BuildWarning();
            BuildWeaponsButton();
            BuildMapButton();
            BuildPowerCellCounter(); // parents onto _weaponsButtonRoot — must follow BuildWeaponsButton
            BuildWeaponsButtonBadges();
            BuildFloatingLayer();
            BuildSupercellFx(); // must follow BuildPowerCellCounter — travels toward _cellCounterRoot
            if (!SkipTouchControlsForTests) BuildTouchControls();

            _model.Boss.ActiveChanged += OnBossActiveChanged;
        }

        private void OnEnable()
        {
            HudSignals.DamageDealt += OnDamage;
            HudSignals.Pickup += OnPickup;
            HudSignals.SupercellCollected += OnSupercellCollected;
            HudSignals.EnemyKilled += OnEnemyKilled;
            HudSignals.FactoryRegistered += OnFactoryRegistered;
            HudSignals.FactoryDestroyed += OnFactoryDestroyed;
            HudSignals.BossRegistered += OnBossRegistered;
            HudSignals.BossEngaged += OnBossEngaged;
            HudSignals.BossHealthChanged += OnBossHealth;
            HudSignals.BossSpawnLevelChanged += OnBossSpawnLevel;
            HudSignals.BossDefeated += OnBossDefeated;
            HudSignals.SentinelRecalled += OnSentinelRecalled;
            MaxWorlds.Pickups.PickupWallet.PowerCellsChanged += OnPowerCells;
            MaxWorlds.Pickups.PickupWallet.CapacityChanged += OnCellCapacity;
            UpgradeState.Changed += OnUpgradesChanged;
            WeaponSystemState.Changed += OnAbilitiesChanged;
            AbilityCreditBank.Changed += OnAbilityCreditsChanged;
            PendingMorphingModule.Changed += OnPendingModuleChanged;
        }

        private void OnDisable()
        {
            HudSignals.DamageDealt -= OnDamage;
            HudSignals.Pickup -= OnPickup;
            HudSignals.SupercellCollected -= OnSupercellCollected;
            HudSignals.EnemyKilled -= OnEnemyKilled;
            HudSignals.FactoryRegistered -= OnFactoryRegistered;
            HudSignals.FactoryDestroyed -= OnFactoryDestroyed;
            HudSignals.BossRegistered -= OnBossRegistered;
            HudSignals.BossEngaged -= OnBossEngaged;
            HudSignals.BossHealthChanged -= OnBossHealth;
            HudSignals.BossSpawnLevelChanged -= OnBossSpawnLevel;
            HudSignals.BossDefeated -= OnBossDefeated;
            HudSignals.SentinelRecalled -= OnSentinelRecalled;
            MaxWorlds.Pickups.PickupWallet.PowerCellsChanged -= OnPowerCells;
            MaxWorlds.Pickups.PickupWallet.CapacityChanged -= OnCellCapacity;
            UpgradeState.Changed -= OnUpgradesChanged;
            WeaponSystemState.Changed -= OnAbilitiesChanged;
            AbilityCreditBank.Changed -= OnAbilityCreditsChanged;
            PendingMorphingModule.Changed -= OnPendingModuleChanged;
        }

        /// <summary>The Hydro burst button appears the moment the harness + condenser are both
        /// installed (YT-215 acceptance: "before assembly, no button; after assembly, button appears").</summary>
        private void OnUpgradesChanged()
        {
            if (_hydroButtonRoot != null) _hydroButtonRoot.gameObject.SetActive(UpgradeState.HydroAssembled);
        }

        /// <summary>Water Balloon and Teleport each appear the moment their own ability is acquired
        /// (MV-380 restored Water Balloon's gate after MV-370 briefly dropped it). Both grow more
        /// prominent as they level (WV-240, spec §6a) — rebuilt through <see cref="AbilityControlArt"/>
        /// whenever their level actually changed. Also refreshes the Auto-fire toggle (MV-380 AC3),
        /// which appears/disappears on the same acquisition signal.</summary>
        private void OnAbilitiesChanged()
        {
            RebuildWaterBalloonJoystickIfNeeded();
            RebuildTeleportJoystickIfNeeded();
            RebuildSentinelJoystickIfNeeded();
            RefreshWaterBalloonAutoFireToggle();
            RefreshAttackModeToggle();
            if (_forceFieldButtonRoot != null)
                _forceFieldButtonRoot.gameObject.SetActive(WeaponSystemState.IsAcquired(AbilityKind.ForceField));
        }

        private void OnPowerCells(int total)
        {
            SetCellCountDisplay(total, MaxWorlds.Pickups.PickupWallet.Capacity);
            _cellPop = 1f;   // a brief scale pop so a banked cell registers
        }

        /// <summary>MV-374: the reserve's cap itself moved (a Cell Capacity level-up) — the count
        /// didn't change, but the "current/max" text still needs to redraw for the new max.</summary>
        private void OnCellCapacity(int capacity)
        {
            SetCellCountDisplay(MaxWorlds.Pickups.PickupWallet.PowerCells, capacity);
        }

        /// <summary>MV-519 Change item 5: an over-cap balance (a Supercell pushed <paramref name="count"/>
        /// past <paramref name="capacity"/>) must read as a deliberate bonus, not a bug — tinted the same
        /// amber a Supercell glows, instead of the readout's usual bone-white. Every place that draws the
        /// cell readout's text (the ordinary bank/capacity events AND <see cref="UpdateSupercellFx"/>'s
        /// own count-up/settle) goes through here so the colour can never drift out of sync with the
        /// number it's tinting.</summary>
        private void SetCellCountDisplay(int count, int capacity)
        {
            if (_cellCount == null) return;
            _cellCount.text = $"{count}/{capacity}";
            _cellCount.color = count > capacity ? SupercellColor : BoneWhite;
        }

        /// <summary>MV-519: a Supercell now grants its cells instantly — this drives the burst/flyup/
        /// count-up event, not a banked-count tally (that chip is gone; see the FX fields' own doc
        /// comment).</summary>
        private void OnSupercellCollected(Vector3 worldPos, int cellsBefore, int cellsAfter) =>
            StartSupercellFx(worldPos, cellsBefore, cellsAfter);

        /// <summary>MV-358: a banked shed credit flashes the WEAPONS-button badge — "something is
        /// waiting in the Abilities screen" — the same way a captured module does.</summary>
        private void OnAbilityCreditsChanged(int banked) => RefreshWeaponsButtonAlert();

        /// <summary>MV-425: a Morphing Module draft banked/taken (<see cref="PendingMorphingModule"/>)
        /// flips the button's cyan "module captured" state.</summary>
        private void OnPendingModuleChanged() => RefreshWeaponsButtonAlert();

        /// <summary>The four-state alert this button carries (MV-425): idle, parts-to-fit (amber),
        /// module-captured (cyan) or both. Immediate toggle of what's shown; the pulse/flash animation
        /// itself runs every frame in <see cref="UpdateWeaponsButton"/>.</summary>
        private void RefreshWeaponsButtonAlert()
        {
            var alert = CurrentWeaponsButtonAlert();
            if (_moduleBadgeRoot != null) _moduleBadgeRoot.gameObject.SetActive(ShowsModuleBadge(alert));
            if (_weaponsModuleHaloRoot != null) _weaponsModuleHaloRoot.gameObject.SetActive(ShowsModuleRing(alert));
        }

        /// <summary>MV-519: the ring's amber "parts to fit" state now tracks banked ability credits
        /// alone — a Supercell is never banked anymore (<see cref="MaxWorlds.Pickups.PickupWallet.AddSupercell"/>
        /// grants instantly, nothing left to flag an alert over).</summary>
        private static WeaponsButtonAlert CurrentWeaponsButtonAlert() => ComputeWeaponsButtonAlert(
            AbilityCreditBank.Banked > 0,
            PendingMorphingModule.HasPending);

        /// <summary>Pure predicate (MV-358, dropped its Supercell half MV-519 — a Supercell is never
        /// banked anymore) — pinned by an EditMode test without building a canvas: a spend is waiting
        /// if a shed ability credit is banked.</summary>
        public static bool ShouldShowSupercellAlert(int abilityCreditsBanked) =>
            abilityCreditsBanked > 0;

        /// <summary>The WEAPONS button's four alert states (MV-425). Amber ("supercell to cash") means
        /// something is spendable and the player chooses when; cyan ("module captured") means the game
        /// is waiting on a decision the player hasn't made — they never share a colour, and cyan always
        /// wins the ring.</summary>
        public enum WeaponsButtonAlert { Idle, PartsToFit, ModuleCaptured, Both }

        /// <summary>Pure — pinned directly by an EditMode test, no canvas needed.</summary>
        public static WeaponsButtonAlert ComputeWeaponsButtonAlert(bool partsToFit, bool moduleCaptured) =>
            partsToFit && moduleCaptured ? WeaponsButtonAlert.Both
            : moduleCaptured ? WeaponsButtonAlert.ModuleCaptured
            : partsToFit ? WeaponsButtonAlert.PartsToFit
            : WeaponsButtonAlert.Idle;

        public static bool ShowsPartsBadge(WeaponsButtonAlert alert) =>
            alert == WeaponsButtonAlert.PartsToFit || alert == WeaponsButtonAlert.Both;

        public static bool ShowsModuleBadge(WeaponsButtonAlert alert) =>
            alert == WeaponsButtonAlert.ModuleCaptured || alert == WeaponsButtonAlert.Both;

        /// <summary>Cyan always wins the ring — "Both" shows the module halo, not the amber ring; the
        /// amber count keeps its own corner badge regardless.</summary>
        public static bool ShowsModuleRing(WeaponsButtonAlert alert) =>
            alert == WeaponsButtonAlert.ModuleCaptured || alert == WeaponsButtonAlert.Both;

        /// <summary>The module-cyan colour (<c>rig_board.json</c> "module", #7FE3FF) — the single named
        /// constant that data file's own colours block has asked for since MV-423
        /// (<c>"constant": "NEW - add HudController.ModuleColor"</c>). Read live off
        /// <see cref="RigBoardLayout"/> rather than a second hand-copied hex, the same "source, not a
        /// matched copy" idiom <see cref="SupercellColor"/> already follows.</summary>
        public static Color ModuleColor => RigBoardLayout.Colour("module");

        /// <summary>The ring/mark stroke colour for each state — module cyan sourced from
        /// <see cref="ModuleColor"/> so the HUD tell and the board it points at (MV-433's node glow)
        /// never drift apart.</summary>
        public static Color WeaponsButtonRingColor(WeaponsButtonAlert alert) =>
            ShowsModuleRing(alert) ? ModuleColor
            : alert == WeaponsButtonAlert.PartsToFit ? SupercellColor
            : WeaponsButtonIdleRingColor;

        /// <summary>Tapping the WEAPONS button (YT-178) opens the weapons area to show Max's current
        /// loadout on demand — the button is always-available access, not gated on a part being banked.
        /// Parts are universal upgrade tokens now (WV-228): there is no more draft-pick reveal
        /// (YT-207) to choose from on pickup — spending a banked part happens inside the weapons area.
        /// Opens <see cref="WeaponsScreen"/> (WV-232), the RCDA-tracks-and-abilities screen that
        /// supersedes the legacy <see cref="UpgradeScreen"/> as this button's destination.</summary>
        private void OnWeaponsButtonTapped()
        {
            var screen = FindFirstObjectByType<WeaponsScreen>();
            if (screen == null) return;

            screen.Open();
        }

        /// <summary>Tapping the MAP button (MV-563) opens the full-screen map, replacing the old
        /// always-on minimap — same "find it, open it" idiom as <see cref="OnWeaponsButtonTapped"/>.</summary>
        private void OnMapButtonTapped()
        {
            var screen = FindFirstObjectByType<MapScreen>();
            if (screen == null) return;

            screen.Open();
        }

        private void OnBossRegistered() => _model.UseExternalBoss();
        private void OnBossEngaged(string name, int phases) => _model.EngageBossExternal(name, phases);
        private void OnBossHealth(float normalized) => _model.SetBossHealth(normalized);
        private void OnBossSpawnLevel(int level, float progress01) => _model.SetBossSpawnLevel(level, progress01);
        private void OnBossDefeated() => _model.DefeatBossExternal();

        private void OnFactoryRegistered() => _model.RegisterFactory();

        private void OnFactoryDestroyed(Vector3 pos)
        {
            _model.RegisterFactoryDestroyed();
            _floating?.Spawn(pos + Vector3.up * 2.2f, "FACTORY DOWN", XpColor, false, 1.4f, 34f);
        }

        // ---------- signal handlers ----------

        private void OnDamage(Vector3 pos, float amount, bool crit)
        {
            // Accumulated, not spawned (YT-54). A sustained stream lands a tick every 0.1s on every
            // enemy it touches, so spawning a number per event buries the screen at 20-30 enemies.
            // The aggregator merges them into one number per enemy per window; see FlushDamageNumbers.
            _damageNumbers.Add(pos, amount, crit, Time.time);
        }

        private void FlushDamageNumbers()
        {
            if (_floating == null) return;

            _damageBuffer.Clear();
            _damageNumbers.Flush(Time.time, _damageBuffer);

            foreach (var e in _damageBuffer)
            {
                Color c = e.Crit ? XpColor : Color.white;
                // Bigger accumulated hits get a bigger number — the size carries the weight.
                float size = Mathf.Lerp(24f, 38f, Mathf.InverseLerp(4f, 60f, e.Amount));
                _floating.Spawn(e.Position + Vector3.up * 1.4f,
                    Mathf.RoundToInt(e.Amount).ToString(), c, e.Crit, 0.55f, size);
            }
        }

        private void OnPickup(Vector3 pos, string label, Color color)
            => _floating?.Spawn(pos + Vector3.up * 1.6f, label, color, false, 1.0f, 30f);

        /// <summary>MV-604 item 2: a sentinel vanishing several areas away, off-screen, would read as
        /// a bug with no HUD acknowledgement at all — this is the "a player who never sees the despawn
        /// still understands the slot was reclaimed" half; <see cref="MaxWorlds.VFX.CombatVfx"/>'s own
        /// recall burst is the other half, for whoever IS looking at it.</summary>
        private void OnSentinelRecalled(Vector3 pos)
            => _floating?.Spawn(pos + Vector3.up * 1.8f, "SENTINEL RECALLED", SentinelColor, false, 1.1f, 22f);

        private void OnEnemyKilled(Vector3 pos)
        {
            _model.RegisterKill();
            _floating?.Spawn(pos + Vector3.up * 1.8f, $"+{_model.SparksPerKill} SPARKS", XpColor, false, 1.0f, 30f);
        }

        private void OnBossActiveChanged(bool active)
        {
            _bossRoot.gameObject.SetActive(active);
            _spawnLevelRoot.gameObject.SetActive(active);
            if (active)
            {
                _bossName.text = _model.Boss.Name;
                _bossIncomingTimer = 1.5f; // "BOSS INCOMING" name-card flash
            }
        }

        // ---------- per-frame update ----------

        private void Update()
        {
            float dt = Time.deltaTime;

            // Slice ability demos: Bomb auto-cycles its cooldown so the radial wipe reads;
            // the Ultimate charges from kills (handled in the model).
            _model.Bomb.Tick(dt);
            if (_model.Bomb.Ready) _model.Bomb.Trigger();

            UpdateHydroButton(dt);
            UpdateForceFieldButton(dt);
            UpdateSentinelJoystick();
            UpdateAbilityControls();
            UpdateJoysticks();
            UpdateArena(dt);
            UpdateInvasionDial(dt);
            UpdateBoss();
            UpdateWarnings(dt);
            UpdateDrops(dt);
            UpdateWeaponsButton();
            UpdateSupercellFx(Time.unscaledDeltaTime);
            FlushDamageNumbers();
        }

        private void UpdateDrops(float dt)
        {
            // Cell icon pops on a bank and settles back.
            _cellPop = Mathf.Max(0f, _cellPop - dt * 3f);
            if (_cellIcon != null)
            {
                float s = 1f + CellIconPopScaleDelta * _cellPop;
                _cellIcon.rectTransform.localScale = new Vector3(s, s, 1f);
            }
        }

        /// <summary>MV-519: kicks off the Supercell "definite pickup event" — a burst at the pickup
        /// point plus a "+10" that flies to the cell readout. Projects the pickup's world position into
        /// <see cref="_supercellFxRoot"/>'s local space via the gameplay camera (same technique
        /// <see cref="FloatingTextLayer"/> uses for pickup toasts); silently does nothing if that
        /// projection fails (point behind the camera, no camera at all) rather than start an effect that
        /// can never reach its destination.</summary>
        private void StartSupercellFx(Vector3 worldPos, int cellsBefore, int cellsAfter)
        {
            if (_supercellFxRoot == null || _cellCounterRoot == null) return;
            Camera cam = _worldCamera != null ? _worldCamera : Camera.main;
            if (cam == null) return;

            Vector3 sp = cam.WorldToScreenPoint(worldPos + Vector3.up * 1.6f);
            if (sp.z < 0f) return; // behind the camera
            Camera uiCam = _canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cam;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_supercellFxRoot, sp, uiCam, out _supercellFxStart))
                return;

            _supercellFxEnd = _supercellFxRoot.InverseTransformPoint(_cellCounterRoot.position);
            _supercellFxFromCells = cellsBefore;
            _supercellFxToCells = cellsAfter;
            _supercellFxAge = 0f;
            _supercellFxActive = true;
            _cellPop = 1f;

            if (_supercellFxBurst != null)
            {
                _supercellFxBurst.gameObject.SetActive(true);
                _supercellFxBurst.rectTransform.anchoredPosition = _supercellFxStart;
            }
            if (_supercellFxLabel != null)
            {
                _supercellFxLabel.gameObject.SetActive(true);
                _supercellFxLabel.text = $"+{MaxWorlds.Pickups.PickupWallet.SupercellCellValue}";
                _supercellFxLabel.rectTransform.anchoredPosition = _supercellFxStart;
            }
        }

        /// <summary>Drives the active Supercell pickup event, if any, off the pure curves in
        /// <see cref="SupercellPickupEffect"/> — self-terminating: once <paramref name="unscaledDt"/>
        /// has carried <see cref="_supercellFxAge"/> past <see cref="SupercellPickupEffect.Duration"/>,
        /// the burst and the flyup label both deactivate and the readout settles on the real, final
        /// <see cref="MaxWorlds.Pickups.PickupWallet.PowerCells"/> total — nothing is left on screen.</summary>
        private void UpdateSupercellFx(float unscaledDt)
        {
            if (!_supercellFxActive) return;

            _supercellFxAge += unscaledDt;
            if (!SupercellPickupEffect.IsActive(_supercellFxAge))
            {
                _supercellFxActive = false;
                if (_supercellFxBurst != null) _supercellFxBurst.gameObject.SetActive(false);
                if (_supercellFxLabel != null) _supercellFxLabel.gameObject.SetActive(false);
                SetCellCountDisplay(MaxWorlds.Pickups.PickupWallet.PowerCells, MaxWorlds.Pickups.PickupWallet.Capacity);
                return;
            }

            float age = _supercellFxAge;
            if (_supercellFxBurst != null)
            {
                float s = SupercellPickupEffect.BurstScale(age);
                _supercellFxBurst.rectTransform.localScale = new Vector3(s, s, 1f);
                var bc = SupercellColor;
                bc.a = SupercellPickupEffect.BurstAlpha(age);
                _supercellFxBurst.color = bc;
            }
            if (_supercellFxLabel != null)
            {
                _supercellFxLabel.rectTransform.anchoredPosition =
                    Vector2.Lerp(_supercellFxStart, _supercellFxEnd, SupercellPickupEffect.TravelT(age));
                var lc = SupercellColor;
                lc.a = SupercellPickupEffect.LabelAlpha(age);
                _supercellFxLabel.color = lc;
            }
            int liveCount = SupercellPickupEffect.CountAt(_supercellFxFromCells, _supercellFxToCells, age);
            SetCellCountDisplay(liveCount, MaxWorlds.Pickups.PickupWallet.Capacity);
        }

        /// <summary>
        /// The parts-badge flash, 0..1. Pure and driven by unscaled time so it keeps flashing while a
        /// paused screen still shows the badge waiting (YT-147). ~1 Hz — a touch quicker than the
        /// on-ground aura's ambient breath, because this is an alert. Deliberately slower than
        /// <see cref="ModuleAlertFlash"/> (MV-425 design: amber reads as patient, cyan as urgent).
        /// </summary>
        public static float PartAlertFlash(float unscaledTime)
            => 0.5f + 0.5f * Mathf.Sin(unscaledTime * 6f);

        /// <summary>
        /// The parts badge's colour at flash amount <paramref name="t"/>: the shared collectible orange
        /// swung dim-&gt;full so it reads as an active beacon, not a static badge (YT-147). The hue is
        /// <see cref="SupercellColor"/> — the same orange the on-ground pickup glows — so the two never drift
        /// and neither is the forbidden yellow.
        /// </summary>
        public static Color PartAlertColor(float t)
        {
            t = Mathf.Clamp01(t);
            // MV-300: a deeper trough so the beat reads as a strong pulse, not a gentle wobble.
            Color c = SupercellColor * (0.32f + 0.68f * t);   // dim -> full orange
            c.a = 0.55f + 0.45f * t;
            return c;
        }

        /// <summary>The module badge/halo's flash, 0..1 (MV-425) — roughly twice <see cref="PartAlertFlash"/>'s
        /// rate: "cyan should read as roughly twice as urgent" as amber.</summary>
        public static float ModuleAlertFlash(float unscaledTime)
            => 0.5f + 0.5f * Mathf.Sin(unscaledTime * 12f);

        /// <summary>The module badge/ring/halo's colour at flash amount <paramref name="t"/> — the same
        /// dim-&gt;full swing <see cref="PartAlertColor"/> uses, in module cyan instead of parts amber.</summary>
        public static Color ModuleAlertColor(float t)
        {
            t = Mathf.Clamp01(t);
            Color c = ModuleColor * (0.32f + 0.68f * t);
            c.a = 0.55f + 0.45f * t;
            return c;
        }

        /// <summary>Drives the WEAPONS button's ring/halo/mark and both corner badges every frame
        /// (MV-425) off the four-state alert (<see cref="RefreshWeaponsButtonAlert"/> already toggled
        /// which pieces are active; this only animates them). Unscaled time throughout — same reason
        /// <see cref="PartAlertFlash"/> always was: a badge must keep flashing while a paused screen
        /// still shows something waiting.</summary>
        private void UpdateWeaponsButton()
        {
            if (_weaponsButtonRoot == null) return;
            var alert = CurrentWeaponsButtonAlert();

            if (_weaponsButtonMark != null)
            {
                var mc = BoneWhite;
                mc.a = alert == WeaponsButtonAlert.Idle ? 0.6f : 1f;
                _weaponsButtonMark.color = mc;
            }

            if (_weaponsButtonRing != null)
            {
                var rc = WeaponsButtonRingColor(alert);
                if (alert == WeaponsButtonAlert.Idle)
                {
                    rc.a = 0.7f; // thin, steady — deliberately recessive
                }
                else
                {
                    float flash = ShowsModuleRing(alert) ? ModuleAlertFlash(Time.unscaledTime) : PartAlertFlash(Time.unscaledTime);
                    rc.a = 0.6f + 0.4f * flash;
                }
                _weaponsButtonRing.color = rc;
            }

            if (_weaponsModuleHaloRoot != null && _weaponsModuleHaloRoot.gameObject.activeSelf)
            {
                float t = ModuleAlertFlash(Time.unscaledTime);
                var hc = ModuleColor; hc.a = 0.30f * (0.4f + 0.6f * t);
                if (_weaponsModuleHaloOuter != null) _weaponsModuleHaloOuter.color = hc;
                if (_weaponsModuleHaloInner != null) _weaponsModuleHaloInner.color = hc;
            }

            if (_moduleBadgeRoot != null && _moduleBadgeRoot.gameObject.activeSelf)
            {
                AnimateBadge(_moduleBadgeRoot, _moduleBadgeBg, _moduleBadgeGlow, ModuleColor, ModuleAlertFlash(Time.unscaledTime));
            }

            UpdateRigCounters();
        }

        /// <summary>MV-471 (MV-519 dropped its part-counter half — that chip is gone): redraws the
        /// always-on cell readout's flash every frame, which engages only when <see cref="RigActions"/>
        /// says cells actually buy something right now.</summary>
        private void UpdateRigCounters()
        {
            if (_cellCounterRoot != null)
            {
                // Text itself is kept current by OnPowerCells/OnCellCapacity — this only drives the
                // MV-471 flash the moved readout inherited from the bare chip it replaced.
                bool actionable = RigActions.AnyCellActionAffordable(MaxWorlds.Pickups.PickupWallet.PowerCells);
                SetRigCounterFlash(_cellCounterRoot, _cellCounterBg, _cellCounterGlow, CellColor, actionable, swapBackground: false);
            }
        }

        /// <summary>Idle: a flat neutral chip (the live count still shows). Actionable: the same
        /// dim-&gt;full pulse <see cref="AnimateBadge"/> already uses for the module badge, in the
        /// given currency's own colour — so "the RIG mark is worth a look" reads the same language
        /// everywhere on this button.</summary>
        /// <summary>MV-510 review round 1 (AC A2): <paramref name="swapBackground"/> defaults true for
        /// the parts chip (unchanged behaviour). The cell pill passes false — its background must stay
        /// PanelColor in every state (idle AND actionable); the cyan "worth a look" cue lives in the
        /// glow ring and the icon's own baked colour instead, so a saturated slab cannot come back.</summary>
        private static void SetRigCounterFlash(RectTransform root, Image bg, Image glow, Color hue, bool actionable, bool swapBackground = true)
        {
            if (actionable)
            {
                AnimateBadge(root, bg, glow, hue, PartAlertFlash(Time.unscaledTime), swapBackground);
                return;
            }

            if (swapBackground && bg != null) bg.color = PanelColor;
            if (glow != null) glow.color = Color.clear;
            root.localScale = Vector3.one;
        }

        /// <summary>One corner badge's beat: chip colour swings dim-&gt;full, a scale pop on the beat,
        /// and an outer glow ring swelling/brightening in step — same shape MV-300 built for the single
        /// chip this replaces, now shared by both. <paramref name="swapBackground"/> false (MV-510)
        /// skips the chip-colour swing so the background stays PanelColor throughout.</summary>
        private static void AnimateBadge(RectTransform root, Image bg, Image glow, Color hue, float t, bool swapBackground = true)
        {
            if (swapBackground && bg != null)
            {
                Color c = hue * (0.32f + 0.68f * t);
                c.a = 0.55f + 0.45f * t;
                bg.color = c;
            }

            float s = 1f + 0.22f * t;
            root.localScale = new Vector3(s, s, 1f);

            if (glow != null)
            {
                var gc = hue; gc.a = 0.55f * t;
                glow.color = gc;
                float gs = 1f + 0.18f * t;
                glow.rectTransform.localScale = new Vector3(gs, gs, 1f);
            }
        }

        /// <summary>
        /// Drives the Hydro burst button (YT-215) — a no-op while it's hidden (not yet assembled).
        /// While bursting, the glow runs hot and the label counts the freedom down (acceptance: "a kid
        /// knows the freedom is ticking"); once it ends, the radial darkens through the cooldown and a
        /// bright one-shot flash sells the snap-back moment before settling into the ready-glow pulse
        /// the ability slots already use (<see cref="SetSlot"/>).
        /// </summary>
        private void UpdateHydroButton(float dt)
        {
            if (_hydroButtonRoot == null || !_hydroButtonRoot.gameObject.activeSelf) return;

            bool active = HydroBurst.Active;
            bool ready = HydroBurst.Ready;

            if (ready && !_hydroWasReady) _hydroReadyFlash = 1f;
            _hydroWasReady = ready;
            _hydroReadyFlash = Mathf.Max(0f, _hydroReadyFlash - dt * 3.2f);

            // The snap-back beat: a bright flash the instant the burst ends, decaying independently
            // of the ready pulse above.
            if (_hydroWasActive && !active) _hydroSnapFlash = 1f;
            _hydroWasActive = active;
            _hydroSnapFlash = Mathf.Max(0f, _hydroSnapFlash - dt * 2f);

            if (active)
            {
                float pulse = 0.7f + 0.3f * Mathf.Abs(Mathf.Sin(Time.time * 8f));
                _hydroGlow.color = new Color(HydroColor.r, HydroColor.g, HydroColor.b, pulse);
                _hydroRadial.fillAmount = 0f;
                _hydroLabel.text = Mathf.CeilToInt(HydroBurst.RemainingSeconds) + "s";
            }
            else
            {
                _hydroLabel.text = "HYDRO";
                _hydroRadial.fillAmount = HydroBurst.CooldownNormalized;

                float readyPulse = ready ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 4f)) : 0f;
                Color glow = Color.Lerp(ReadyGlow, HydroColor, 0.5f);
                glow = Color.Lerp(glow, Color.white, _hydroSnapFlash);
                glow.a = ready ? Mathf.Clamp01(readyPulse + _hydroReadyFlash) : Mathf.Max(0f, _hydroSnapFlash * 0.8f);
                _hydroGlow.color = glow;
            }
        }

        /// <summary>
        /// Drives the Force Field button (MV-361) — a no-op while it's hidden (not yet acquired). While
        /// the bubble is up, the glow runs hot and the label counts the absorb budget down as a percent
        /// (MV-361: "obvious from peripheral vision... obvious when it drops"); once it pops, the radial
        /// darkens through the cooldown and a bright one-shot flash sells the burst before settling into
        /// the ready-glow pulse the ability slots already use — same shape as <see cref="UpdateHydroButton"/>.
        /// </summary>
        private void UpdateForceFieldButton(float dt)
        {
            if (_forceFieldButtonRoot == null || !_forceFieldButtonRoot.gameObject.activeSelf) return;
            if (_abilities == null) return;

            bool active = _abilities.ForceFieldActive;
            bool ready = _abilities.ForceFieldReady;

            if (ready && !_forceFieldWasReady) _forceFieldReadyFlash = 1f;
            _forceFieldWasReady = ready;
            _forceFieldReadyFlash = Mathf.Max(0f, _forceFieldReadyFlash - dt * 3.2f);

            if (_forceFieldWasActive && !active) _forceFieldSnapFlash = 1f;
            _forceFieldWasActive = active;
            _forceFieldSnapFlash = Mathf.Max(0f, _forceFieldSnapFlash - dt * 2f);

            if (active)
            {
                float pulse = 0.7f + 0.3f * Mathf.Abs(Mathf.Sin(Time.time * 8f));
                _forceFieldGlow.color = new Color(ForceFieldColor.r, ForceFieldColor.g, ForceFieldColor.b, pulse);
                _forceFieldRadial.fillAmount = 0f;
                _forceFieldLabel.text = Mathf.CeilToInt(_abilities.ForceFieldAbsorbFraction * 100f) + "%";
            }
            else
            {
                _forceFieldLabel.text = "FIELD";
                _forceFieldRadial.fillAmount = _abilities.ForceFieldCooldownRemaining > 0f
                    ? Mathf.Clamp01(_abilities.ForceFieldCooldownRemaining / WeaponSystemState.EffectiveCooldownSeconds(AbilityKind.ForceField))
                    : 0f;

                float readyPulse = ready ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 4f)) : 0f;
                Color glow = Color.Lerp(ReadyGlow, ForceFieldColor, 0.5f);
                glow = Color.Lerp(glow, Color.white, _forceFieldSnapFlash);
                glow.a = ready ? Mathf.Clamp01(readyPulse + _forceFieldReadyFlash) : Mathf.Max(0f, _forceFieldSnapFlash * 0.8f);
                _forceFieldGlow.color = glow;
            }
        }

        /// <summary>Drives the Sentinel deploy joystick (MV-362, aimed MV-399, one sentinel only
        /// MV-422) — no-op while hidden (not yet acquired). No cooldown: a sentinel is gated purely on
        /// cell cost and the deployment-slot cap, so the radial simply covers/uncovers on that gate —
        /// the same "empty bank reads as covered" idiom <see cref="UpdateAbilityControls"/> already
        /// uses for Water Balloon's own cell gate — and the label keeps the live slot-count readout
        /// the old buttons showed.</summary>
        private void UpdateSentinelJoystick()
        {
            if (_abilities == null) return;
            if (_sentinelRadial == null || _sentinelRoot == null || !_sentinelRoot.gameObject.activeSelf) return;

            _sentinelRadial.fillAmount = _abilities.SentinelReady ? 0f : 1f;
            if (_sentinelDeniedIcon != null)
                _sentinelDeniedIcon.gameObject.SetActive(
                    MaxWorlds.Pickups.PickupWallet.PowerCells < PlayerAbilities.SentinelCost);
            if (_sentinelVisual.Label != null)
                _sentinelVisual.Label.text = $"SEN\n{PlayerAbilities.SentinelDeployedCount}/{PlayerAbilities.SentinelDeploymentCap}";
        }

        /// <summary>Drives the Water Balloon/Teleport cooldown sweeps (WV-240, spec §6a: "every
        /// control shows a cooldown sweep and is disabled during cooldown"). MV-370: an empty cell bank
        /// reads the same as "on cooldown" — a full radial cover — since either way the control can't
        /// fire right now (AC6: "communicated clearly").</summary>
        private void UpdateAbilityControls()
        {
            if (_waterBalloonRadial != null && _waterBalloonRoot != null && _waterBalloonRoot.gameObject.activeSelf)
            {
                if (MaxWorlds.Pickups.PickupWallet.PowerCells <= 0)
                {
                    _waterBalloonRadial.fillAmount = 1f;
                }
                else
                {
                    float cd = WeaponSystemState.WaterBalloonEffectiveCooldownSeconds();
                    float remaining = _abilities != null ? _abilities.WaterBalloonCooldownRemaining : 0f;
                    _waterBalloonRadial.fillAmount = cd > 0f ? Mathf.Clamp01(remaining / cd) : 0f;
                }
            }

            if (_teleportRadial != null && _teleportRoot != null && _teleportRoot.gameObject.activeSelf)
            {
                float cd = WeaponSystemState.EffectiveCooldownSeconds(AbilityKind.Teleport);
                float remaining = _abilities != null ? _abilities.TeleportCooldownRemaining : 0f;
                _teleportRadial.fillAmount = cd > 0f ? Mathf.Clamp01(remaining / cd) : 0f;
            }
        }

        private void UpdateJoysticks()
        {
            // Movement joystick: dim when idle, bright + direction arrow when pushed.
            Vector2 move = _player != null ? _player.MoveInput : Vector2.zero;
            bool moving = move.sqrMagnitude > 0.02f;
            SetRingBrightness(_moveRings, moving);
            _moveArrowRect.gameObject.SetActive(moving);
            if (moving)
            {
                float ang = Mathf.Atan2(move.y, move.x) * Mathf.Rad2Deg - 90f; // arrow art points up
                _moveArrowRect.localRotation = Quaternion.Euler(0, 0, ang);
                _moveKnob.anchoredPosition = move.normalized * 26f;
            }
            else _moveKnob.anchoredPosition = Vector2.zero;

            // Aim joystick: bright while aiming; knob leans toward facing.
            bool aiming = _player != null && _player.IsAiming;
            SetRingBrightness(_aimRings, aiming);
            _aimCross.color = new Color(TechRingColor.r, TechRingColor.g, TechRingColor.b, aiming ? 1f : 0.45f);
            if (aiming)
            {
                Vector3 f = _player.Facing;
                _aimKnob.anchoredPosition = new Vector2(f.x, f.z).normalized * 26f;
            }
            else _aimKnob.anchoredPosition = Vector2.zero;
        }

        private static void SetRingBrightness(Image rings, bool active)
        {
            float a = active ? 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 6f)) : 0.35f;
            rings.color = new Color(TechRingColor.r, TechRingColor.g, TechRingColor.b, a);
        }

        private void UpdateArena(float dt)
        {
            _arenaProminence = Mathf.MoveTowards(_arenaProminence, 0.28f, dt * 1.4f); // settle to faint idle
            var a = _arenaLabel.color; a.a = _arenaProminence; _arenaLabel.color = a;
            float scale = Mathf.Lerp(1f, 1.18f, Mathf.InverseLerp(0.28f, 1f, _arenaProminence));
            _arenaLabel.rectTransform.localScale = Vector3.one * scale;
        }

        private void UpdateBoss()
        {
            if (!_model.Boss.Active) return;
            _bossFill.fillAmount = _model.Boss.HpNormalized;
            RebuildBossSegments(_model.Boss.Phases);

            float spawnFill = _model.Boss.SpawnLevel - 1 + _model.Boss.SpawnLevelProgress01;
            _spawnLevelFill.fillAmount = Mathf.Clamp01(spawnFill / BossState.MaxSpawnLevel);
        }

        private void UpdateWarnings(float dt)
        {
            string msg = null; Color col = Color.white;
            if (_bossIncomingTimer > 0f)
            {
                _bossIncomingTimer -= dt;
                msg = "BOSS INCOMING"; col = new Color(0.7f, 0.3f, 1f);
            }
            else if (_health != null && _health.IsAlive && _health.Normalized > 0f && _health.Normalized < 0.25f)
            {
                msg = "HEALTH LOW"; col = HpColor;
            }

            if (msg == null) { _warning.gameObject.SetActive(false); return; }
            _warning.gameObject.SetActive(true);
            _warning.text = msg;
            float pulse = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * 5f));
            _warning.color = new Color(col.r, col.g, col.b, pulse);
        }

        // ---------- construction ----------

        private void BuildCanvas()
        {
            var go = new GameObject("HUD Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // Safe-area root (YT-98): everything that anchors to a screen edge/corner is parented
            // here so the notch / Dynamic Island / home indicator never covers it. On desktop and
            // in CI the safe area is the full screen, so this rect fills the canvas and layout is
            // identical to before; the inset only appears on hardware that reports a notch.
            _safeRoot = NewRect("Safe Area", (RectTransform)_canvas.transform);
            Stretch(_safeRoot);
            _safeRoot.gameObject.AddComponent<SafeArea>();
        }

        /// <summary>Edge-anchored controls parent here — inset to the device safe area.</summary>
        private RectTransform Root => _safeRoot;

        /// <summary>Full-screen overlays (biome tint, floating text, big map) parent here — they
        /// intentionally cover the whole display, notch included.</summary>
        private RectTransform FullRoot => (RectTransform)_canvas.transform;

        private void BuildBiomeTint()
        {
            var img = AddImage(FullRoot, HudTextures.Solid(), BiomeTint, "Biome Tint");
            Stretch(img.rectTransform);
            img.raycastTarget = false;
        }

        private void BuildUtilityIcons()
        {
            // MV-645: "P" (Pack/Journal) and "S" (Settings) were dead placeholders with no click
            // handler; only "?" (the MV-503 diagnostic overlay toggle) ever did anything.
            string[] glyphs = { "?" };
            var col = NewRect("Utility Icons", Root);
            Anchor(col, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            col.anchoredPosition = new Vector2(24f, -24f);
            col.sizeDelta = new Vector2(56f, 56f);
            for (int i = 0; i < glyphs.Length; i++)
            {
                var slot = AddImage(col, HudTextures.RoundedBox(64, 0.28f), PanelColor, $"Icon {glyphs[i]}");
                Anchor(slot.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
                slot.rectTransform.sizeDelta = new Vector2(56f, 56f);
                slot.rectTransform.anchoredPosition = new Vector2(0f, -i * 64f);
                slot.type = Image.Type.Sliced;
                var t = AddText(slot.rectTransform, 26f, BoneWhite, TextAnchor.MiddleCenter);
                Stretch(t.rectTransform);
                t.text = glyphs[i];

                // MV-505: "?" (Help) has no other behaviour yet, and already sits next to the
                // FPS/build readout in the top-left — the existing touch affordance the ticket asks
                // the MV-503 overlay to hook into, rather than a new input path.
                if (glyphs[i] == "?")
                {
                    var button = slot.gameObject.AddComponent<Button>();
                    button.transition = Selectable.Transition.None;
                    button.onClick.AddListener(Mv503DiagnosticOverlay.ToggleVisible);
                }
            }
        }

        /// <summary>The HOME button (YT-191): one tap from a live run back to the Home/save-slot
        /// screen. Sits just right of the utility icon column — the one gap the top-left corner has
        /// left, clear of the status strip, which is centred.</summary>
        private void BuildHomeButton()
        {
            var root = NewRect("Home Button", Root);
            Anchor(root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            root.sizeDelta = new Vector2(64f, 64f);
            root.anchoredPosition = new Vector2(90f, -24f);

            var bg = AddImage(root, HudTextures.RoundedBox(64, 0.28f), PanelColor, "Home BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;

            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnHomeButtonTapped);

            var label = AddText(root, 15f, BoneWhite, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, -6f);
            label.text = "HOME";
            label.fontStyle = FontStyle.Bold;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 9;
            label.resizeTextMaxSize = 16;
            label.raycastTarget = false;
        }

        /// <summary>Tapping HOME (YT-191): abandon the live run and return to the Home/save-slot
        /// screen — now the shared <see cref="RunFlow.QuitToMenu"/> (MV-257), same effect this
        /// button always had, so Settings and Weapons can offer it too.</summary>
        private void OnHomeButtonTapped() => RunFlow.QuitToMenu();

        /// <summary>
        /// The Hydro burst button (YT-215) — a round action button up and to the left of the aim
        /// stick, where the right thumb already is (the Brawl-Stars placement Dash occupied before
        /// MV-359 removed it: the action button sits inside the arc the aiming thumb already sweeps).
        /// Hidden until <see cref="UpgradeState.HydroAssembled"/>.
        /// </summary>
        private void BuildHydroButton()
        {
            var root = NewRect("Hydro Burst Button", Root);
            Anchor(root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f));
            root.anchoredPosition = new Vector2(-HydroButtonInset, HydroButtonRise);
            root.sizeDelta = new Vector2(HydroButtonSize, HydroButtonSize);
            _hydroButtonRoot = root;

            var glow = AddImage(root, HudTextures.TechRings(160, 3), Color.clear, "Glow");
            Stretch(glow.rectTransform, 4f);
            glow.raycastTarget = false;
            _hydroGlow = glow;

            var ring = AddImage(root, HudTextures.TechRings(160, 3), HydroColor, "Ring");
            Stretch(ring.rectTransform);
            ring.raycastTarget = true;   // the tappable surface — the ring itself is the button
            var button = ring.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnHydroButtonTapped);

            _hydroLabel = AddText(root, 20f, HydroColor, TextAnchor.MiddleCenter);
            Stretch(_hydroLabel.rectTransform);
            _hydroLabel.text = "HYDRO";
            _hydroLabel.fontStyle = FontStyle.Bold;
            _hydroLabel.raycastTarget = false;
            _hydroLabel.resizeTextForBestFit = true;
            _hydroLabel.resizeTextMinSize = 10;
            _hydroLabel.resizeTextMaxSize = 22;

            var radial = AddImage(root, HudTextures.Disc(160), new Color(0f, 0f, 0f, 0.5f), "Radial");
            Stretch(radial.rectTransform, -6f);
            radial.type = Image.Type.Filled;
            radial.fillMethod = Image.FillMethod.Radial360;
            radial.fillOrigin = (int)Image.Origin360.Top;
            radial.fillClockwise = true;
            radial.fillAmount = 0f;
            radial.raycastTarget = false;
            _hydroRadial = radial;

            root.gameObject.SetActive(UpgradeState.HydroAssembled);   // hidden until assembled
        }

        /// <summary>Tapping HYDRO (YT-215): start the burst. <see cref="HydroBurst.Trigger"/> is
        /// itself a no-op when not ready, so there is nothing to gate here beyond the button
        /// existing at all.</summary>
        private void OnHydroButtonTapped() => HydroBurst.Trigger();

        // Far enough from the corner to clear the aim stick's touch pad (the stick is 200 wide at
        // (-150, 150) and its pad adds 30 on each side, so it owns out to x = -310) — the same slot
        // Dash occupied before MV-359 removed it.
        private const float HydroButtonSize = 110f;
        private const float HydroButtonInset = 400f;
        private const float HydroButtonRise = 330f;

        /// <summary>
        /// The Force Field button (MV-361, moved to the far left MV-606, reordered into the left
        /// play-area column MV-645) — same round action-button shape as Hydro, sitting at the bottom
        /// of the shared left column via <see cref="ForceFieldX"/>/<see cref="ForceFieldRise"/>,
        /// closest to the Move stick. Hidden until <see cref="AbilityKind.ForceField"/> is acquired.
        /// </summary>
        private void BuildForceFieldButton()
        {
            var root = NewRect("Force Field Button", Root);
            Anchor(root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
            root.anchoredPosition = new Vector2(ForceFieldX, ForceFieldRise);
            root.sizeDelta = new Vector2(HydroButtonSize, HydroButtonSize);
            _forceFieldButtonRoot = root;

            var glow = AddImage(root, HudTextures.TechRings(160, 3), Color.clear, "Glow");
            Stretch(glow.rectTransform, 4f);
            glow.raycastTarget = false;
            _forceFieldGlow = glow;

            var ring = AddImage(root, HudTextures.TechRings(160, 3), ForceFieldColor, "Ring");
            Stretch(ring.rectTransform);
            ring.raycastTarget = true;
            var button = ring.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnForceFieldButtonTapped);

            // MV-543: ForceFieldColor's own luminance sits mid-range, so a light "bone" fill on the
            // label can't clear 4.5:1 against it (peaks near 2:1 even at pure white) — only a dark ink
            // does the maths. The BoneWhite outline supplies the "near-white" treatment instead, framing
            // the digits rather than filling them.
            // MV-585: AddText's base fontSize (its own field, distinct from resizeTextMaxSize) is itself
            // an upper bound on what best-fit will ever resolve to — the exact MV-489 trap
            // (WeaponsScreen's base fontSize silently outranking resizeTextMaxSize). Base size must track
            // the raised cap below or it re-imposes the old 20pt ceiling on its own.
            _forceFieldLabel = AddText(root, 32f, ForceFieldLabelInk, TextAnchor.MiddleCenter);
            Anchor(_forceFieldLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            // Was 84x40 with resizeTextMaxSize 22 — the max was the binding constraint (best-fit
            // never draws past it regardless of box size), so it read unreadably small on an iPhone. Both
            // move together: the box widened enough to let the raised cap actually resolve, sized to clear
            // 1%/100%/FIELD (the widest case) against the ring's inner radius (see MV585ForceFieldLabelFontSizeTests).
            _forceFieldLabel.rectTransform.sizeDelta = new Vector2(96f, 52f);
            _forceFieldLabel.rectTransform.anchoredPosition = Vector2.zero;
            _forceFieldLabel.text = "FIELD";
            _forceFieldLabel.fontStyle = FontStyle.Bold;
            _forceFieldLabel.raycastTarget = false;
            _forceFieldLabel.resizeTextForBestFit = true;
            _forceFieldLabel.resizeTextMinSize = 10;
            var forceFieldLabelOutline = _forceFieldLabel.gameObject.AddComponent<Outline>();
            forceFieldLabelOutline.effectColor = BoneWhite;
            forceFieldLabelOutline.effectDistance = new Vector2(1.2f, -1.2f);
            _forceFieldLabel.resizeTextMaxSize = 32;

            var radial = AddImage(root, HudTextures.Disc(160), new Color(0f, 0f, 0f, 0.5f), "Radial");
            Stretch(radial.rectTransform, -6f);
            radial.type = Image.Type.Filled;
            radial.fillMethod = Image.FillMethod.Radial360;
            radial.fillOrigin = (int)Image.Origin360.Top;
            radial.fillClockwise = true;
            radial.fillAmount = 0f;
            radial.raycastTarget = false;
            _forceFieldRadial = radial;

            root.gameObject.SetActive(WeaponSystemState.IsAcquired(AbilityKind.ForceField));
        }

        /// <summary>Tapping FIELD (MV-361): raise the bubble. <see cref="PlayerAbilities.TryActivateForceField"/>
        /// is itself a no-op when not ready (unowned, on cooldown, already up, or too few cells), so
        /// there is nothing to gate here beyond the button existing at all.</summary>
        private void OnForceFieldButtonTapped() => _abilities?.TryActivateForceField();

        // The Sentinel deploy joystick (MV-362, aimed placement MV-399, one sentinel only MV-422):
        // well clear of Hydro's own stack below (top edge 385, MV-606: Force Field moved off this
        // column onto its own left-edge spot) and the boss bar's y-band (rise 300, half 8) beneath
        // that — same "half-extent-plus-margin clearance" reasoning the Water Balloon/Teleport column
        // below uses for itself.
        private const float SentinelJoystickRise = 820f;
        private const float SentinelJoystickX = 360f;

        private void BuildSentinelJoystick() => RebuildSentinelJoystick();

        private void RebuildSentinelJoystick()
        {
            if (_sentinelRoot != null) Destroy(_sentinelRoot.gameObject);

            int level = RigState.Level("u_dmg");
            int maxLevel = RigBoard.MaxLevel("u_dmg");
            var anchoredPos = new Vector2(SentinelJoystickX, SentinelJoystickRise);
            _sentinelVisual = AbilityControlArt.BuildJoystick(
                Root, "Sentinel Joystick", anchoredPos, SentinelColor, "SEN", level, maxLevel);
            _sentinelRoot = _sentinelVisual.Root;

            // No cooldown — covers/uncovers on the cell-cost + deployment-cap gate instead, same
            // "empty bank reads as covered" idiom Water Balloon's own radial uses (UpdateAbilityControls).
            var radial = AddImage(_sentinelRoot, HudTextures.Disc(160), new Color(0f, 0f, 0f, 0.5f), "Radial");
            Stretch(radial.rectTransform, -6f);
            radial.type = Image.Type.Filled;
            radial.fillMethod = Image.FillMethod.Radial360;
            radial.fillOrigin = (int)Image.Origin360.Top;
            radial.fillClockwise = true;
            radial.fillAmount = 0f;
            radial.raycastTarget = false;
            _sentinelRadial = radial;

            // MV-407: a dedicated "can't afford this" read, distinct from the radial cover above —
            // the radial also covers on a full deployment cap, which isn't a cell-cost problem.
            var denied = AddImage(_sentinelRoot, WeaponHudIcons.PowerCellDenied(64), Color.white, "Insufficient Parts");
            denied.rectTransform.anchorMin = denied.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            denied.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            denied.rectTransform.sizeDelta = new Vector2(72f, 72f);
            denied.rectTransform.anchoredPosition = Vector2.zero;
            denied.raycastTarget = false;
            denied.gameObject.SetActive(false);
            _sentinelDeniedIcon = denied;

            var pad = new GameObject("Sentinel Touch", typeof(RectTransform), typeof(Image));
            var padRect = (RectTransform)pad.transform;
            padRect.SetParent(_sentinelRoot, false);
            padRect.anchorMin = Vector2.zero; padRect.anchorMax = Vector2.one;
            padRect.offsetMin = new Vector2(-30f, -30f); padRect.offsetMax = new Vector2(30f, 30f);
            var padImg = pad.GetComponent<Image>();
            padImg.color = new Color(0f, 0f, 0f, 0f);
            padImg.raycastTarget = true;

            var control = pad.AddComponent<SentinelJoystickControl>();
            control.Init(_sentinelVisual.Knob, _player != null ? _player.transform : null, _abilities,
                _sentinelVisual.Rings);

            _sentinelBuiltLevel = level;
            _sentinelRoot.gameObject.SetActive(WeaponSystemState.IsAcquired(AbilityKind.Sentinels));
        }

        private void RebuildSentinelJoystickIfNeeded()
        {
            int level = RigState.Level("u_dmg");
            if (level == _sentinelBuiltLevel)
            {
                if (_sentinelRoot != null)
                    _sentinelRoot.gameObject.SetActive(WeaponSystemState.IsAcquired(AbilityKind.Sentinels));
                return;
            }
            RebuildSentinelJoystick();
        }

        /// <summary>MV-636: a small pill sitting above the Sentinel joystick, reading "ATTACK ON"/
        /// "ATTACK OFF" — the player's own switch for whether deployed sentinels hold ahead of Max and
        /// prioritise clearing his path, or keep the existing standoff-follow/nearest-overall behaviour.
        /// Same shape as <see cref="BuildWaterBalloonAutoFireToggle"/>'s own pill, gated on Move/u_mov
        /// &gt;= 1 (per the ticket) rather than an <see cref="AbilityKind"/> acquisition. Built once and
        /// left inactive; <see cref="RefreshAttackModeToggle"/> (driven off <see cref="WeaponSystemState.Changed"/>,
        /// which already fires on a RIG level-up — see <see cref="OnAbilitiesChanged"/>) shows/hides and
        /// relabels it live.</summary>
        // MV-676: was SentinelJoystickRise + 120 (centre 940), only checked against the full 1080
        // reference — top edge 962 leaves almost no room once an iPhone-standard landscape aspect
        // (~852x393pt) compresses the CanvasScaler's matchWidthOrHeight=0.5 blend down to an effective
        // ~978-unit-tall canvas, and it clipped on real devices. +60 lands the top edge at ~902, the
        // same ~72-76-unit safety margin the MAP button (the column's own topmost element) already
        // carries — see MV676HudPhoneAspectMarginTests.
        private const float AttackModeToggleRise = 60f;

        private void BuildAttackModeToggle()
        {
            var root = NewRect("Sentinel Attack Mode Toggle", Root);
            Anchor(root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
            root.sizeDelta = new Vector2(140f, 44f);
            root.anchoredPosition = new Vector2(SentinelJoystickX, SentinelJoystickRise + AttackModeToggleRise);
            _attackModeToggleRoot = root;

            var bg = AddImage(root, HudTextures.RoundedBox(32, 0.5f), SentinelColor, "BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;
            _attackModeToggleBg = bg;

            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnAttackModeToggleTapped);

            _attackModeToggleLabel = AddText(root, 18f, BoneWhite, TextAnchor.MiddleCenter);
            Stretch(_attackModeToggleLabel.rectTransform);
            _attackModeToggleLabel.fontStyle = FontStyle.Bold;
            _attackModeToggleLabel.raycastTarget = false;

            root.gameObject.SetActive(false);   // RefreshAttackModeToggle turns it on once u_mov >= 1
            RefreshAttackModeToggle();
        }

        private void OnAttackModeToggleTapped()
        {
            Sentinel.AttackModeEnabled = !Sentinel.AttackModeEnabled;
            RefreshAttackModeToggle();
        }

        private void RefreshAttackModeToggle()
        {
            if (_attackModeToggleRoot == null) return;

            bool unlocked = AbilityTuning.SentinelCanMove(RigState.Level("u_mov"));
            _attackModeToggleRoot.gameObject.SetActive(unlocked);
            if (!unlocked) return;

            bool on = Sentinel.AttackModeEnabled;
            if (_attackModeToggleLabel != null) _attackModeToggleLabel.text = on ? "ATTACK ON" : "ATTACK OFF";
            if (_attackModeToggleBg != null)
            {
                var c = SentinelColor;
                c.a = on ? 1f : 0.4f;
                _attackModeToggleBg.color = c;
            }
        }

        // Water Balloon's joystick sits above the Move stick (WV-240, spec §6a), so aiming a throw
        // never costs the player their movement thumb. MV-606 moved Teleport off this column onto its
        // own right-edge spot above the aim stick (see TeleportX/TeleportRise) — Lee's brief put
        // teleport with the hand that aims, not the hand that moves.
        // MV-645: this X now shares the left play-area column with MAP/the Settings gear/Force
        // Field (all at X=150) — RebuildWaterBalloonJoystick re-anchors its root to the left edge to
        // apply it, same as RebuildTeleportJoystick already does on the right.
        private const float AbilityControlColumnX = 150f;
        // MV-676: was 530 (a 30px gap above Force Field's own top edge) — raised to a 42px gap, part
        // of the same column-wide crowding fix as ForceFieldRise above.
        private const float WaterBalloonJoystickRise = 554f;
        private const float WaterBalloonJoystickMaxHalfSize = 100f;   // half of BuildJoystick's 200 px cap

        private static readonly Color WaterBalloonColor = new Color(0.35f, 0.65f, 0.98f); // balloon blue
        private static readonly Color TeleportColor = new Color(0.75f, 0.45f, 0.95f);     // blink violet

        /// <summary>The Water Balloon joystick (WV-240, spec §6a; MV-370: a primary add-on now, visible
        /// from run start rather than gated on acquisition), grows more prominent with level
        /// (<see cref="AbilityControlArt"/>), and its own <see cref="WaterBalloonJoystickControl"/>
        /// drives the press/drag/release aim + throw.</summary>
        private void BuildWaterBalloonJoystick() => RebuildWaterBalloonJoystick();

        /// <summary>The joystick's visual "prominence" level (MV-370): the best-invested of the three
        /// Water Balloon tracks, out of their shared cap — since there's no longer one single ability
        /// level to read, this is the closest read of "how upgraded is this add-on overall".</summary>
        private static int WaterBalloonJoystickLevel() => Mathf.Max(
            WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.Range),
            Mathf.Max(
                WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.SplashArea),
                WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.RepeatFire)));

        private void RebuildWaterBalloonJoystick()
        {
            if (_waterBalloonRoot != null) Destroy(_waterBalloonRoot.gameObject);

            int level = WaterBalloonJoystickLevel();
            int maxLevel = WeaponCatalog.MaxLevel(WaterBalloonTrackKind.Range);
            _waterBalloonVisual = AbilityControlArt.BuildJoystick(
                Root, "Water Balloon Joystick", Vector2.zero, WaterBalloonColor, "Balloon", level, maxLevel);
            _waterBalloonRoot = _waterBalloonVisual.Root;
            // MV-645: re-anchored to the left edge, same idiom MV-606 already used to move Teleport's
            // BuildJoystick (always bottom-CENTRE by default) onto the right edge above the aim
            // stick. A bottom-CENTRE anchor's fixed offset drifts by half the gap between the actual
            // and reference canvas width, which the old (450) column X had enough margin to survive
            // at every tested aspect (MV606HudReshuffleTests) but the new, closer-to-the-edge column
            // X=150 does not — re-anchoring to the left edge (like Force Field/MAP/the Move stick
            // already do) makes the shared column X robust at any aspect instead of just 16:9.
            Anchor(_waterBalloonRoot, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
            _waterBalloonRoot.anchoredPosition = new Vector2(AbilityControlColumnX, WaterBalloonJoystickRise);

            // Cooldown wipe, identical treatment to the other controls so the three read as one
            // language (spec §6a: "every control shows a cooldown sweep").
            var radial = AddImage(_waterBalloonRoot, HudTextures.Disc(160), new Color(0f, 0f, 0f, 0.5f), "Radial");
            Stretch(radial.rectTransform, -6f);
            radial.type = Image.Type.Filled;
            radial.fillMethod = Image.FillMethod.Radial360;
            radial.fillOrigin = (int)Image.Origin360.Top;
            radial.fillClockwise = true;
            radial.fillAmount = 0f;
            radial.raycastTarget = false;
            _waterBalloonRadial = radial;

            // Transparent raycastable pad over the joystick, the same fat-finger margin as
            // AddOnScreenStick's own pads — the finger's touch surface; the rings/knob stay the
            // visible control.
            var pad = new GameObject("Water Balloon Touch", typeof(RectTransform), typeof(Image));
            var padRect = (RectTransform)pad.transform;
            padRect.SetParent(_waterBalloonRoot, false);
            padRect.anchorMin = Vector2.zero; padRect.anchorMax = Vector2.one;
            padRect.offsetMin = new Vector2(-30f, -30f); padRect.offsetMax = new Vector2(30f, 30f);
            var padImg = pad.GetComponent<Image>();
            padImg.color = new Color(0f, 0f, 0f, 0f);
            padImg.raycastTarget = true;

            var control = pad.AddComponent<WaterBalloonJoystickControl>();
            control.Init(_waterBalloonVisual.Knob, _player != null ? _player.transform : null, _abilities,
                _waterBalloonVisual.Rings);

            _waterBalloonBuiltLevel = level;
            _waterBalloonRoot.gameObject.SetActive(WeaponSystemState.IsAcquired(AbilityKind.WaterBalloon));
        }

        private void RebuildWaterBalloonJoystickIfNeeded()
        {
            int level = WaterBalloonJoystickLevel();
            if (level == _waterBalloonBuiltLevel)
            {
                if (_waterBalloonRoot != null)
                    _waterBalloonRoot.gameObject.SetActive(WeaponSystemState.IsAcquired(AbilityKind.WaterBalloon));
                return;
            }
            RebuildWaterBalloonJoystick();
        }

        /// <summary>MV-380 AC3: a small pill sitting just above the Water Balloon joystick, reading
        /// "AUTO ON"/"AUTO OFF" — the player's own switch for auto-fire once it's unlocked, so someone
        /// who'd rather aim by hand some of the time isn't stuck with it. Built once and left inactive;
        /// <see cref="RefreshWaterBalloonAutoFireToggle"/> (driven off <see cref="WeaponSystemState.Changed"/>)
        /// shows/hides and relabels it live.</summary>
        private void BuildWaterBalloonAutoFireToggle()
        {
            var root = NewRect("Water Balloon Auto-fire Toggle", Root);
            Anchor(root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
            root.sizeDelta = new Vector2(140f, 44f);
            // Sits just above the joystick's own rings — nothing stacked above it on this column since
            // MV-606 moved Teleport to the right-edge column above the aim stick.
            root.anchoredPosition = new Vector2(
                AbilityControlColumnX - RefW * 0.5f,
                WaterBalloonJoystickRise + WaterBalloonJoystickMaxHalfSize + 20f);
            _waterBalloonAutoFireToggleRoot = root;

            var bg = AddImage(root, HudTextures.RoundedBox(32, 0.5f), WaterBalloonColor, "BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;
            _waterBalloonAutoFireToggleBg = bg;

            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnWaterBalloonAutoFireToggleTapped);

            _waterBalloonAutoFireToggleLabel = AddText(root, 18f, BoneWhite, TextAnchor.MiddleCenter);
            Stretch(_waterBalloonAutoFireToggleLabel.rectTransform);
            _waterBalloonAutoFireToggleLabel.fontStyle = FontStyle.Bold;
            _waterBalloonAutoFireToggleLabel.raycastTarget = false;

            root.gameObject.SetActive(false);   // RefreshWaterBalloonAutoFireToggle turns it on once acquired
        }

        private void OnWaterBalloonAutoFireToggleTapped()
        {
            WeaponSystemState.WaterBalloonAutoFireEnabled = !WeaponSystemState.WaterBalloonAutoFireEnabled;
            RefreshWaterBalloonAutoFireToggle();
        }

        private void RefreshWaterBalloonAutoFireToggle()
        {
            if (_waterBalloonAutoFireToggleRoot == null) return;

            bool unlocked = WeaponSystemState.IsAcquired(AbilityKind.WaterBalloonAutoFire);
            _waterBalloonAutoFireToggleRoot.gameObject.SetActive(unlocked);
            if (!unlocked) return;

            bool on = WeaponSystemState.WaterBalloonAutoFireEnabled;
            if (_waterBalloonAutoFireToggleLabel != null) _waterBalloonAutoFireToggleLabel.text = on ? "AUTO ON" : "AUTO OFF";
            if (_waterBalloonAutoFireToggleBg != null)
            {
                var c = WaterBalloonColor;
                c.a = on ? 1f : 0.4f;
                _waterBalloonAutoFireToggleBg.color = c;
            }
        }

        /// <summary>The Teleport joystick (MV-338: "needs to work the same way as Water Balloon — a
        /// direction and distance joystick"), appearing once acquired and growing a detail pip at its
        /// aimed-blink second level. Its own <see cref="TeleportJoystickControl"/> drives the
        /// press/drag/release aim + blink, the same hand-off shape Water Balloon's joystick uses.
        /// MV-606: lives above the aim stick now (<see cref="TeleportX"/>/<see cref="TeleportRise"/>),
        /// re-anchored to the right edge after <see cref="AbilityControlArt.BuildJoystick"/> builds it
        /// (that helper always anchors bottom-centre).</summary>
        private void BuildTeleportJoystick() => RebuildTeleportJoystick();

        private void RebuildTeleportJoystick()
        {
            if (_teleportRoot != null) Destroy(_teleportRoot.gameObject);

            int level = Mathf.Max(1, WeaponSystemState.AbilityLevel(AbilityKind.Teleport));
            int maxLevel = WeaponCatalog.MaxLevel(AbilityKind.Teleport);
            _teleportVisual = AbilityControlArt.BuildJoystick(
                Root, "Teleport Joystick", Vector2.zero, TeleportColor, "Teleport", level, maxLevel);
            _teleportRoot = _teleportVisual.Root;
            Anchor(_teleportRoot, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f));
            _teleportRoot.anchoredPosition = new Vector2(TeleportX, TeleportRise);

            // Cooldown wipe, identical treatment to Water Balloon's own (spec §6a: "every control shows
            // a cooldown sweep").
            var radial = AddImage(_teleportRoot, HudTextures.Disc(160), new Color(0f, 0f, 0f, 0.5f), "Radial");
            Stretch(radial.rectTransform, -6f);
            radial.type = Image.Type.Filled;
            radial.fillMethod = Image.FillMethod.Radial360;
            radial.fillOrigin = (int)Image.Origin360.Top;
            radial.fillClockwise = true;
            radial.fillAmount = 0f;
            radial.raycastTarget = false;
            _teleportRadial = radial;

            // Transparent raycastable pad over the joystick, the same fat-finger margin Water Balloon's
            // own touch pad uses — the finger's touch surface; the rings/knob stay the visible control.
            var pad = new GameObject("Teleport Touch", typeof(RectTransform), typeof(Image));
            var padRect = (RectTransform)pad.transform;
            padRect.SetParent(_teleportRoot, false);
            padRect.anchorMin = Vector2.zero; padRect.anchorMax = Vector2.one;
            padRect.offsetMin = new Vector2(-30f, -30f); padRect.offsetMax = new Vector2(30f, 30f);
            var padImg = pad.GetComponent<Image>();
            padImg.color = new Color(0f, 0f, 0f, 0f);
            padImg.raycastTarget = true;

            var control = pad.AddComponent<TeleportJoystickControl>();
            control.Init(_teleportVisual.Knob, _player != null ? _player.transform : null, _abilities,
                _teleportVisual.Rings);

            _teleportBuiltLevel = level;
            _teleportRoot.gameObject.SetActive(WeaponSystemState.IsAcquired(AbilityKind.Teleport));
        }

        private void RebuildTeleportJoystickIfNeeded()
        {
            int level = Mathf.Max(1, WeaponSystemState.AbilityLevel(AbilityKind.Teleport));
            if (level == _teleportBuiltLevel)
            {
                if (_teleportRoot != null)
                    _teleportRoot.gameObject.SetActive(WeaponSystemState.IsAcquired(AbilityKind.Teleport));
                return;
            }
            RebuildTeleportJoystick();
        }

        private void BuildJoysticks()
        {
            // Bottom-left: movement.
            var moveRoot = NewRect("Move Joystick", Root);
            _moveJoystickRoot = moveRoot;
            Anchor(moveRoot, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
            moveRoot.anchoredPosition = new Vector2(150f, 150f);
            moveRoot.sizeDelta = new Vector2(200f, 200f);
            _moveRings = AddImage(moveRoot, HudTextures.TechRings(160, 3), TechRingColor, "Rings");
            Stretch(_moveRings.rectTransform); _moveRings.raycastTarget = false;
            _moveKnob = AddImage(moveRoot, HudTextures.Disc(96), new Color(TechRingColor.r, TechRingColor.g, TechRingColor.b, 0.9f), "Knob").rectTransform;
            Center(_moveKnob, 64f);
            _moveArrow = AddImage(moveRoot, HudTextures.Arrow(64), Color.white, "Arrow");
            _moveArrowRect = _moveArrow.rectTransform;
            Center(_moveArrowRect, 40f);
            _moveArrowRect.anchoredPosition = new Vector2(0f, 60f);
            _moveArrowRect.gameObject.SetActive(false);

            // Bottom-right: aim.
            var aimRoot = NewRect("Aim Joystick", Root);
            _aimJoystickRoot = aimRoot;
            Anchor(aimRoot, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f));
            aimRoot.anchoredPosition = new Vector2(-150f, 150f);
            aimRoot.sizeDelta = new Vector2(200f, 200f);
            _aimRings = AddImage(aimRoot, HudTextures.TechRings(160, 3), TechRingColor, "Rings");
            Stretch(_aimRings.rectTransform); _aimRings.raycastTarget = false;
            _aimKnob = AddImage(aimRoot, HudTextures.Disc(96), new Color(TechRingColor.r, TechRingColor.g, TechRingColor.b, 0.9f), "Knob").rectTransform;
            Center(_aimKnob, 64f);
            _aimCross = AddImage(aimRoot, HudTextures.Crosshair(96), TechRingColor, "Crosshair");
            Center(_aimCross.rectTransform, 72f);
        }

        /// <summary>
        /// Touch controls for the iOS/mobile input path (YT-98). The visible joysticks above are
        /// only visualisers; here we lay a transparent <see cref="OnScreenStick"/> pad over each,
        /// driving the SAME synthetic-gamepad controls <see cref="PlayerController"/> already binds
        /// (<c>&lt;Gamepad&gt;/leftStick</c>, <c>/rightStick</c>). So a finger feeds the exact input
        /// path a real controller would, with zero change to gameplay code, and — because each stick
        /// captures its own pointer — move and aim work as simultaneous multi-touch. On-device feel
        /// (drag range, tap vs drag) is tuned in Lee's device pass.
        /// </summary>
        private void BuildTouchControls()
        {
            EnsureEventSystem();

            if (_moveJoystickRoot != null)
                AddOnScreenStick(_moveJoystickRoot, "<Gamepad>/leftStick", "Move Touch");
            if (_aimJoystickRoot != null)
                AddOnScreenStick(_aimJoystickRoot, "<Gamepad>/rightStick", "Aim Touch");
        }

        private static void AddOnScreenStick(RectTransform joystickRoot, string controlPath, string name)
        {
            // Transparent, raycastable pad over the joystick (plus margin for fat fingers). The pad
            // is what the finger grabs; the rings/knob stay the visible stick, driven by MoveInput.
            var pad = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(OnScreenStick));
            var rect = (RectTransform)pad.transform;
            rect.SetParent(joystickRoot, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-30f, -30f); rect.offsetMax = new Vector2(30f, 30f);

            var img = pad.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f); // invisible touch surface
            img.raycastTarget = true;

            var stick = pad.GetComponent<OnScreenStick>();
            stick.controlPath = controlPath;
            stick.movementRange = 90f; // px drag for full deflection; tuned on device
            // MV-502: on a real touchscreen, InputSystemUIInputModule's own device-switch auto-cancel
            // fires OnPointerUp on this stick mid-drag (Touchscreen input alongside the stick's
            // synthetic Gamepad output looks like a device switch to the Input System), snapping it
            // back to centre while the finger is still down — the on-device symptom read as "turns,
            // barely moves, never fires". useIsolatedInputActions drives the stick off its own local
            // actions instead of the shared/cancellable ones, so a real device switch elsewhere can't
            // reset it. Explicit behaviour so a package upgrade can't silently change the serialized
            // default out from under this.
            stick.useIsolatedInputActions = true;
            stick.behaviour = OnScreenStick.Behaviour.RelativePositionWithStaticOrigin;
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>();
        }

        private void BuildArenaIndicator()
        {
            _arenaLabel = AddText(Root, 34f, new Color(BoneWhite.r, BoneWhite.g, BoneWhite.b, 0.28f),
                TextAnchor.MiddleCenter);
            Anchor(_arenaLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            _arenaLabel.rectTransform.sizeDelta = new Vector2(720f, 60f);
            _arenaLabel.rectTransform.anchoredPosition = new Vector2(0f, 40f);
            _arenaLabel.fontStyle = FontStyle.Bold;
            RefreshArenaText(prominent: false);
            _model.Arena.Changed += OnArenaChanged;
        }

        private void OnArenaChanged(bool prominent)
        {
            RefreshArenaText(prominent);
            if (prominent) _arenaProminence = 1f;
            else _arenaProminence = Mathf.Max(_arenaProminence, 0.7f);
        }

        private void RefreshArenaText(bool prominent)
        {
            _arenaLabel.text = ArenaLabelText(_model.Arena);
        }

        /// <summary>
        /// MV-353: "SUB-ZONE n/1" was a leftover from the pre-MV-242 single-arena slice — it is not
        /// the same concept as an "Area" in the 10-area gated chain (that already has its own readout,
        /// the minimap), <see cref="ArenaProgress.SubZonesTotal"/> is hard-pinned to 1 in production, and
        /// the flag it shows (every factory destroyed) is the exact instant <see cref="ArenaProgress.FactoriesDestroyed"/>
        /// reaches <see cref="ArenaProgress.FactoriesTotal"/> — a permanently-redundant "0/1" or "1/1"
        /// beside the count that already says the same thing. Dropped rather than relabelled: it has no
        /// meaning of its own left to give a correct label to. FACTORIES stays — it counts real,
        /// dynamically-discovered factories correctly (see <see cref="HudModel.RegisterFactory"/>).
        /// </summary>
        public static string ArenaLabelText(ArenaProgress a) => $"FACTORIES {a.FactoriesDestroyed}/{a.FactoriesTotal}";

        /// <summary>The Invasion Dial (YT-197): a small fill meter across the three escalation bands
        /// — INVASION / INFESTATION / DOMINATION — so the DifficultyDirector curve the swarm is
        /// racing is legible at a glance instead of a clock the player has to interpret. Sits
        /// centred just under the arena indicator — the other "how's the run going" readout.
        /// Replaces the old MM:SS level clock (YT-181).
        ///
        /// MV-355: the band names alone didn't say what the bar actually DOES — Lee, playing it
        /// blind, couldn't tell what filling it meant or changed. Added a small permanent caption
        /// under the fill (not tied to the stage-crossing flash, always on) stating the one real
        /// consequence in plain words: it drives <see cref="DifficultyDirector.SpawnIntervalMultiplier"/>
        /// and <see cref="DifficultyDirector.ToughnessMultiplier"/> — robots spawn faster and hit
        /// harder as it climbs. Kept the band names: DOMINATION already appears on the Result
        /// Screen's near-miss line, so the vocabulary is consistent, not invented here.</summary>
        private void BuildInvasionDial()
        {
            var root = NewRect("Invasion Dial", Root);
            Anchor(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            root.sizeDelta = new Vector2(220f, 18f);
            root.anchoredPosition = new Vector2(0f, 104f); // just above the arena indicator

            var bg = AddImage(root, HudTextures.RoundedBox(18, 0.5f), PanelColor, "BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced;

            _dialFill = AddImage(root, HudTextures.RoundedBox(18, 0.5f), BoneWhite, "Fill");
            Stretch(_dialFill.rectTransform, -3f);
            _dialFill.type = Image.Type.Filled;
            _dialFill.fillMethod = Image.FillMethod.Horizontal;
            _dialFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _dialFill.fillAmount = 0f;

            // Two ticks mark the band boundaries at 1/3 and 2/3 — same language as RebuildBossSegments.
            for (int i = 1; i < 3; i++)
            {
                var tick = AddImage(root, HudTextures.Solid(), new Color(0f, 0f, 0f, 0.75f), $"Band {i}");
                Anchor(tick.rectTransform, new Vector2(i / 3f, 0.5f), new Vector2(i / 3f, 0.5f), new Vector2(0.5f, 0.5f));
                tick.rectTransform.sizeDelta = new Vector2(2f, 18f);
                tick.raycastTarget = false;
            }

            _dialStageLabel = AddText(Root, 24f, BoneWhite, TextAnchor.MiddleCenter);
            Anchor(_dialStageLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            _dialStageLabel.rectTransform.sizeDelta = new Vector2(260f, 28f);
            _dialStageLabel.rectTransform.anchoredPosition = new Vector2(0f, 126f); // label rides above the fill
            _dialStageLabel.fontStyle = FontStyle.Bold;

            // Permanent — not part of the stage-crossing flash — so the consequence reads even if
            // the player never sees a band change.
            _dialCaption = AddText(Root, 13f, new Color(BoneWhite.r, BoneWhite.g, BoneWhite.b, 0.6f),
                TextAnchor.MiddleCenter);
            Anchor(_dialCaption.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            _dialCaption.rectTransform.sizeDelta = new Vector2(260f, 16f);
            _dialCaption.rectTransform.anchoredPosition = new Vector2(0f, 84f); // rides below the fill
            _dialCaption.text = "ROBOTS GET FASTER & TOUGHER";
        }

        private void UpdateInvasionDial(float dt)
        {
            if (_dialFill == null) return;

            float normalized = DifficultyDirector.Normalized;
            _dialFill.fillAmount = normalized;
            // Calm white climbing to urgent red as the Invasion Level nears its ceiling — the same
            // language HEALTH LOW/ENERGY OUT already speak, so a rising threat looks like one.
            _dialFill.color = Color.Lerp(BoneWhite, HpColor, normalized);

            var stage = DifficultyDirector.CurrentStage;
            if (stage != _shownStage)
            {
                _shownStage = stage;
                _dialStageLabel.text = StageLabel(stage);
                _dialStageFlash = 1f; // crossing into a new band is an escalation beat, not a silent tick
            }

            _dialStageFlash = Mathf.Max(0f, _dialStageFlash - dt / 0.4f);
            float pop = 1f + 0.25f * _dialStageFlash;
            _dialStageLabel.rectTransform.localScale = new Vector3(pop, pop, 1f);
            _dialStageLabel.color = Color.Lerp(BoneWhite, ReadyGlow, _dialStageFlash);
        }

        private static string StageLabel(DifficultyDirector.Stage stage) => stage switch
        {
            DifficultyDirector.Stage.Invasion => "INVASION",
            DifficultyDirector.Stage.Infestation => "INFESTATION",
            _ => "DOMINATION",
        };

        /// <summary>Slim boss bar + name card (YT-71). It was a 60%-wide, 34 px slab that read as a
        /// piece of furniture rather than a readout. A boss bar earns attention by being the only
        /// red thing on screen, not by being big.</summary>
        private const float BossBarWidth = RefW * 0.40f;
        private const float BossBarHeight = 16f;

        private void BuildBossBar()
        {
            _bossRoot = NewRect("Boss Bar", Root);
            Anchor(_bossRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            _bossRoot.sizeDelta = new Vector2(BossBarWidth, BossBarHeight);
            _bossRoot.anchoredPosition = new Vector2(0f, 300f);

            var bg = AddImage(_bossRoot, HudTextures.RoundedBox(24, 0.4f), PanelColor, "BG");
            Stretch(bg.rectTransform, -3f); bg.type = Image.Type.Sliced;

            _bossFill = AddImage(_bossRoot, HudTextures.RoundedBox(24, 0.4f), BossColor, "Fill");
            Stretch(_bossFill.rectTransform); _bossFill.type = Image.Type.Filled;
            _bossFill.fillMethod = Image.FillMethod.Horizontal;
            _bossFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _bossFill.fillAmount = 1f;

            _bossSegments = NewRect("Segments", _bossRoot);
            Stretch(_bossSegments);

            _bossName = AddText(_bossRoot, 22f, BoneWhite, TextAnchor.MiddleCenter);
            Anchor(_bossName.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f));
            _bossName.rectTransform.sizeDelta = new Vector2(BossBarWidth, 28f);
            _bossName.rectTransform.anchoredPosition = new Vector2(0f, 16f);
            _bossName.fontStyle = FontStyle.Bold;

            _bossRoot.gameObject.SetActive(false);
        }

        private int _bossSegmentCount = -1;
        private void RebuildBossSegments(int phases)
        {
            if (_bossSegmentCount == phases) return;
            _bossSegmentCount = phases;
            for (int i = _bossSegments.childCount - 1; i >= 0; i--)
                Destroy(_bossSegments.GetChild(i).gameObject);
            for (int i = 1; i < phases; i++)
            {
                var tick = AddImage(_bossSegments, HudTextures.Solid(), new Color(0, 0, 0, 0.75f), $"Seg {i}");
                Anchor(tick.rectTransform, new Vector2((float)i / phases, 0.5f), new Vector2((float)i / phases, 0.5f),
                    new Vector2(0.5f, 0.5f));
                tick.rectTransform.sizeDelta = new Vector2(3f, 34f);
                tick.raycastTarget = false;
            }
        }

        /// <summary>How far off the boss bar's own thickness the spawn-level bar sits, plus its own half
        /// height — so it reads as attached to the boss bar rather than floating above it.</summary>
        private const float SpawnBarHeight = 8f;
        private const float SpawnBarGap = 4f;

        /// <summary>The spawn-level bar (MV-588) — a second, thinner bar directly above the boss health
        /// bar, showing how far the brood volley's composition has escalated. <see cref="BossState.MaxSpawnLevel"/>
        /// fixed segments, same continuous-fill-plus-divider-ticks idiom <see cref="BuildBossBar"/> uses
        /// for phases, so the fill sweeps smoothly and still reads as discrete segments.</summary>
        private void BuildSpawnLevelBar()
        {
            _spawnLevelRoot = NewRect("Spawn Level Bar", Root);
            Anchor(_spawnLevelRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            _spawnLevelRoot.sizeDelta = new Vector2(BossBarWidth, SpawnBarHeight);
            _spawnLevelRoot.anchoredPosition =
                new Vector2(0f, 300f + BossBarHeight * 0.5f + SpawnBarGap + SpawnBarHeight * 0.5f);

            var bg = AddImage(_spawnLevelRoot, HudTextures.RoundedBox(24, 0.4f), PanelColor, "BG");
            Stretch(bg.rectTransform, -2f); bg.type = Image.Type.Sliced;

            _spawnLevelFill = AddImage(_spawnLevelRoot, HudTextures.RoundedBox(24, 0.4f), SpawnLevelColor, "Fill");
            Stretch(_spawnLevelFill.rectTransform); _spawnLevelFill.type = Image.Type.Filled;
            _spawnLevelFill.fillMethod = Image.FillMethod.Horizontal;
            _spawnLevelFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _spawnLevelFill.fillAmount = 0f;

            var segments = NewRect("Segments", _spawnLevelRoot);
            Stretch(segments);
            for (int i = 1; i < BossState.MaxSpawnLevel; i++)
            {
                var tick = AddImage(segments, HudTextures.Solid(), new Color(0, 0, 0, 0.75f), $"Seg {i}");
                float frac = (float)i / BossState.MaxSpawnLevel;
                Anchor(tick.rectTransform, new Vector2(frac, 0.5f), new Vector2(frac, 0.5f), new Vector2(0.5f, 0.5f));
                tick.rectTransform.sizeDelta = new Vector2(2f, SpawnBarHeight);
                tick.raycastTarget = false;
            }

            _spawnLevelRoot.gameObject.SetActive(false);
        }

        private void BuildWarning()
        {
            _warning = AddText(Root, 60f, Color.white, TextAnchor.MiddleCenter);
            Anchor(_warning.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _warning.rectTransform.sizeDelta = new Vector2(900f, 100f);
            _warning.rectTransform.anchoredPosition = new Vector2(0f, 160f);
            _warning.fontStyle = FontStyle.Bold;
            _warning.gameObject.SetActive(false);
        }

        /// <summary>Gap between THE WEAPONS button's hex mark and the cell readout below it (MV-510 —
        /// roomier than the old parts chip's gap so it doesn't crowd the doubled mark; MV-519 removed
        /// that parts chip outright, leaving this readout the mark's only counter).</summary>
        private const float CellReadoutGap = 14f;

        /// <summary>MV-510 review round 1 (Lee): the pill must never read louder than the hex mark it
        /// sits under, and must not exceed the mark's own width (<see cref="WeaponsButtonSize"/>).
        /// MV-510 round 2 shrank the mark 216 -&gt; 173 (x0.8); this keeps the same x0.8 ratio and
        /// margin (200 -&gt; 160, still a proportional margin under the mark, AC A3).</summary>
        private const float CellCounterWidth = 160f;
        private const float CellCounterHeight = 60f;
        private const float CellCounterIconSize = 44f;

        /// <summary>MV-510 round 2 - the cell icon's pop-scale amplitude (<see cref="UpdateDrops"/>
        /// scales it up to <c>1 + CellIconPopScaleDelta</c> on a bank). Named so the text inset below
        /// can derive its reserved width from the same number the animation actually uses, instead of
        /// two independently-authored values that can drift apart - that drift, combined with a pivot
        /// bug, is what caused the icon/digit overlap this round fixes.</summary>
        private const float CellIconPopScaleDelta = 0.35f;
        private const float CellIconMaxPopScale = 1f + CellIconPopScaleDelta;

        /// <summary>Gap from the pill's left edge to the icon, and from the icon to the count text.
        /// Also used to derive <see cref="CellIconCenterX"/> and <see cref="CellCounterTextLeftInset"/>
        /// below.</summary>
        private const float CellIconLeftMargin = 8f;
        private const float CellIconTextGap = 8f;

        /// <summary>The icon's anchored-position X once its pivot is centred (MV-510 round 2 fix -
        /// see the pivot note in <see cref="BuildPowerCellCounter"/>): left margin plus half the
        /// icon's resting size, so the icon's resting left edge lands exactly at
        /// <see cref="CellIconLeftMargin"/>.</summary>
        private const float CellIconCenterX = CellIconLeftMargin + CellCounterIconSize * 0.5f;

        /// <summary>MV-510 round 2 - the count text's left inset, derived (not hardcoded) from the
        /// icon's own geometry so a full pop-scale animation can never reach the digits: the icon's
        /// centre, plus its half-width at <see cref="CellIconMaxPopScale"/> (the animation's actual
        /// peak), plus the icon-to-text gap. Replaces the old fixed <c>8 + size + 8</c> inset, which
        /// assumed a centred pivot the icon didn't actually have (the pivot bug) and, even ignoring
        /// that bug, left under 1px of clearance against the animated peak.</summary>
        private const float CellCounterTextLeftInset =
            CellIconCenterX + CellCounterIconSize * 0.5f * CellIconMaxPopScale + CellIconTextGap;

        /// <summary>MV-510 review round 1: the cell readout's numerals were the loudest thing on the
        /// HUD at 40pt. Capped down to a calmer size (AC A1) — MV-519 later removed the parts chip this
        /// used to sit as a peer with; the cap itself is unchanged.</summary>
        private const float CellCounterTextMinSize = 20f;
        private const float CellCounterTextMaxSize = 32f;

        /// <summary>The banked power-cell counter (MV-352, moved under THE WEAPONS button's mark by
        /// MV-510): a pill with a cyan cell icon and a running total. Cells are a resource the player
        /// spends, so this reads at a glance the way health does. Was its own top-centre band; MV-510
        /// retired the separate bare-number chip that used to sit under the mark (<see cref="PickupWallet.PowerCells"/>
        /// was drawn twice) and gave this, the richer icon+total readout, that slot instead — so the
        /// value now renders in exactly one place. Also now carries the MV-471 affordability flash
        /// (<see cref="SetRigCounterFlash"/>) the old bare chip carried, via <see cref="_cellCounterBg"/>/
        /// <see cref="_cellCounterGlow"/>. Must build after <see cref="BuildWeaponsButton"/> — it
        /// parents onto <see cref="_weaponsButtonRoot"/>.</summary>
        private void BuildPowerCellCounter()
        {
            var root = NewRect("Parts", _weaponsButtonRoot);
            Anchor(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f));
            root.sizeDelta = new Vector2(CellCounterWidth, CellCounterHeight);
            root.anchoredPosition = new Vector2(0f, -CellReadoutGap); // directly below the hex mark
            _cellCounterRoot = root;

            _cellCounterGlow = AddImage(root, HudTextures.RoundedBox(64, 0.5f), Color.clear, "Glow Ring");
            Stretch(_cellCounterGlow.rectTransform, 6f); // expands beyond the pill as a halo
            _cellCounterGlow.type = Image.Type.Sliced;
            _cellCounterGlow.raycastTarget = false;

            // MV-510 review round 1 (AC A2): the background never leaves PanelColor — it is the same
            // recessive fill every other HUD panel uses. The idle<->actionable flash used to swap this
            // to a saturated CellColor mass; now (see SetRigCounterFlash's swapBackground) it only
            // animates the glow ring above, so cyan stays an accent (also on the icon), never mass.
            _cellCounterBg = AddImage(root, HudTextures.RoundedBox(44, 0.5f), PanelColor, "BG");
            Stretch(_cellCounterBg.rectTransform); _cellCounterBg.type = Image.Type.Sliced; _cellCounterBg.raycastTarget = false;

            // A purpose-built battery cell (YT-134) — a disc read as "a thing", not "a power cell".
            // The sprite bakes its own cyan/dark, so tint white to render it as authored.
            _cellIcon = AddImage(root, WeaponHudIcons.PowerCell(64), Color.white, "Part Icon");
            // MV-510 round 2 fix: pivot is now centred (was left-edge while the position math assumed
            // centre), so anchoredPosition.x = CellIconCenterX correctly places the icon's CENTRE, not
            // its left edge, at that x - this is the pivot bug the overlap defect traced to.
            Anchor(_cellIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f));
            _cellIcon.rectTransform.sizeDelta = new Vector2(CellCounterIconSize, CellCounterIconSize);
            _cellIcon.rectTransform.anchoredPosition = new Vector2(CellIconCenterX, 0f);
            _cellIcon.raycastTarget = false;

            _cellCount = AddText(root, CellCounterTextMaxSize, BoneWhite, TextAnchor.MiddleLeft);
            Anchor(_cellCount.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f));
            // MV-510 round 2: reserved width now derived from the icon's own max pop-scale geometry
            // (CellCounterTextLeftInset) instead of a fixed 8+size+8 that assumed a centred pivot the
            // icon didn't have and, even once fixed, left under 1px of clearance at full pop.
            _cellCount.rectTransform.offsetMin = new Vector2(CellCounterTextLeftInset, 0f);
            _cellCount.rectTransform.offsetMax = new Vector2(-12f, 0f);
            _cellCount.fontStyle = FontStyle.Bold;
            _cellCount.resizeTextForBestFit = true;
            _cellCount.resizeTextMinSize = (int)CellCounterTextMinSize;
            _cellCount.resizeTextMaxSize = (int)CellCounterTextMaxSize;
            _cellCount.text = $"{MaxWorlds.Pickups.PickupWallet.PowerCells}/{MaxWorlds.Pickups.PickupWallet.Capacity}";
        }

        /// <summary>Hexagon bounding-box texture size THE WEAPONS button's background/ring/halo sprites
        /// bake at (MV-425, doubled MV-510) — independent of the RectTransform's own
        /// <see cref="WeaponsButtonSize"/>, which is what actually sets the tap target.</summary>
        private const int WeaponsButtonHexTex = 173;

        /// <summary>MV-425 AC1 (the live bug that ticket fixed): 96px read 42.2pt on the 932x430pt
        /// 6-inch target (<c>SettingsPanel.Scale6Inch</c>, 0.44) — under Apple's 44pt HIG minimum.
        /// 108px -&gt; 47.5pt cleared it. <c>WeaponsButtonAlertTests</c> (EditMode) pins both the old
        /// failure and that pass. MV-510 doubled it again, 108 -&gt; 216, purely for legibility at a
        /// glance (Lee, playtest) — the 44pt HIG floor was already cleared, so this doesn't re-litigate
        /// AC1, it just goes further. MV-510 round 2 (Lee, 2026-08-21) sized it back down: "Reduce size of
        /// symbol by 20%." 216 x 0.8 = 172.8, rounded to 173 - still roughly 76pt at Scale6Inch, well
        /// clear of the 44pt floor, so this doesn't re-litigate AC1 either.</summary>
        private const float WeaponsButtonSize = 173f;

        /// <summary>MV-510 round 2: a hair of buffer past <see cref="RigCornerInset"/> so the
        /// module-captured halo's own antialiasing doesn't touch the exact safe-area edge (AC3).</summary>
        private const float HaloRightSafetyMargin = 2f;

        /// <summary>The always-available WEAPONS button (YT-178, redrawn MV-425): a hexagonal mark —
        /// three linked nodes, a miniature of THE RIG board itself — replacing the old ABILITIES pill
        /// in place. Corner-anchored (MV-606: top-right, was right-edge/vertically-centred) via
        /// <see cref="RigCornerInset"/>. All procedural: hexagons, circles,
        /// strokes, no art asset, no font glyph (<c>HudFont</c> has no coverage for this symbol). The
        /// ring/halo are driven every frame in <see cref="UpdateWeaponsButton"/> off
        /// <see cref="WeaponsButtonAlert"/>; the two corner badges are a separate build,
        /// <see cref="BuildWeaponsButtonBadges"/>.
        ///
        /// MV-581: the tap target used to be just this hex (raycastTarget + Button on the mark's own
        /// background image), so tapping the cell readout <see cref="BuildPowerCellCounter"/> sits
        /// beneath it did nothing even though the two read as one control. <see cref="_weaponsTapRoot"/>
        /// is now the sole raycastable/Button — an invisible rect sized to the union of the hex and the
        /// cell readout below it — and <see cref="_weaponsButtonRoot"/> nests inside it at the same
        /// absolute size and position it always had (AC3: the visible mark must not change), anchored
        /// to the wrapper's top edge so the hex's own geometry is untouched by the wrapper's height.</summary>
        private void BuildWeaponsButton()
        {
            _weaponsTapRoot = NewRect("Weapons Tap Target", Root);
            // MV-606: top-right corner (was right-edge, vertically centred) — the hex now sits in the
            // corner the retired B/U ability slots vacated, with the cell readout filling the rest of
            // the wrapper's height below it.
            Anchor(_weaponsTapRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            float tapHeight = WeaponsButtonSize + CellReadoutGap + CellCounterHeight;
            _weaponsTapRoot.sizeDelta = new Vector2(WeaponsButtonSize, tapHeight);
            _weaponsTapRoot.anchoredPosition = new Vector2(-RigCornerInset, -RigCornerInset);

            var tapTarget = AddImage(_weaponsTapRoot, null, Color.clear, "Tap Target");
            Stretch(tapTarget.rectTransform);
            tapTarget.raycastTarget = true;
            var tapButton = tapTarget.gameObject.AddComponent<Button>();
            tapButton.transition = Selectable.Transition.None;
            tapButton.onClick.AddListener(OnWeaponsButtonTapped);

            _weaponsButtonRoot = NewRect("Weapons Button", _weaponsTapRoot);
            Anchor(_weaponsButtonRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            _weaponsButtonRoot.sizeDelta = new Vector2(WeaponsButtonSize, WeaponsButtonSize);
            _weaponsButtonRoot.anchoredPosition = Vector2.zero; // pinned to the wrapper's top edge — same absolute spot the mark always occupied

            // Module-captured halo (double ring, MV-425 spec): behind everything else, only ever active
            // for ModuleCaptured/Both (RefreshWeaponsButtonAlert). Sized as multiples of the button's
            // own radius, same GlowRadiusMultiplier-style idiom THE RIG board's own node glow uses.
            //
            // MV-510 round 2: uncapped, the outer ring's own padding (halfSize * 0.42f) bleeds past the
            // safe area's right edge by a wide margin once the mark sits only RigCornerInset
            // units from it (AC3 requires it actually held, not just eyeballed). The right side alone
            // is capped to the room the mark's own position leaves; left/top/bottom keep the full
            // authored bloom.
            _weaponsModuleHaloRoot = NewRect("Module Halo", _weaponsButtonRoot);
            Stretch(_weaponsModuleHaloRoot);
            float halfSize = WeaponsButtonSize * 0.5f;
            float haloRightPad = Mathf.Max(0f, RigCornerInset - HaloRightSafetyMargin);
            _weaponsModuleHaloOuter = AddImage(_weaponsModuleHaloRoot, HudTextures.Glow(128), Color.clear, "Halo Outer");
            StretchCapRight(_weaponsModuleHaloOuter.rectTransform, halfSize * 0.42f, haloRightPad); // r*1.42
            _weaponsModuleHaloOuter.raycastTarget = false;
            _weaponsModuleHaloInner = AddImage(_weaponsModuleHaloRoot, HudTextures.Glow(128), Color.clear, "Halo Inner");
            StretchCapRight(_weaponsModuleHaloInner.rectTransform, halfSize * 0.24f, haloRightPad); // r*1.24
            _weaponsModuleHaloInner.raycastTarget = false;
            _weaponsModuleHaloRoot.gameObject.SetActive(false);

            var bg = AddImage(_weaponsButtonRoot, HudTextures.Polygon(6, -90f, WeaponsButtonHexTex, WeaponsButtonHexTex), PanelColor, "Weapons BG");
            Stretch(bg.rectTransform);
            bg.raycastTarget = false; // MV-581: _weaponsTapRoot's own Tap Target image is the sole raycastable/Button now

            _weaponsButtonRing = AddImage(_weaponsButtonRoot, HudTextures.PolygonOutline(6, -90f, WeaponsButtonHexTex, WeaponsButtonHexTex, 4f), WeaponsButtonIdleRingColor, "Ring");
            Stretch(_weaponsButtonRing.rectTransform);
            _weaponsButtonRing.raycastTarget = false;

            // The mark: three linked nodes, a spine forking into two — deliberately geometric, echoing
            // THE RIG board's own ability nodes at a glance, in a 44x44 vector-icon box (HudTextures
            // convention). White/alpha so Update can dim it for the recessive Idle state without a
            // second sprite.
            _weaponsButtonMark = AddImage(_weaponsButtonRoot, HudTextures.VectorIcon(
                "<path d=\"M0,-13 L-11,11\" fill=\"none\" stroke=\"#ICON#\" stroke-width=\"3.4\" stroke-linecap=\"round\"/>" +
                "<path d=\"M0,-13 L11,11\" fill=\"none\" stroke=\"#ICON#\" stroke-width=\"3.4\" stroke-linecap=\"round\"/>" +
                "<circle cx=\"0\" cy=\"-13\" r=\"5.5\" fill=\"#ICON#\" stroke=\"none\"/>" +
                "<circle cx=\"-11\" cy=\"11\" r=\"5.5\" fill=\"#ICON#\" stroke=\"none\"/>" +
                "<circle cx=\"11\" cy=\"11\" r=\"5.5\" fill=\"#ICON#\" stroke=\"none\"/>", 64),
                BoneWhite, "Mark");
            Center(_weaponsButtonMark.rectTransform, WeaponsButtonSize * 0.6f);
            _weaponsButtonMark.raycastTarget = false;
        }

        private const float MapButtonSize = 120f;
        // MV-645: left edge X so the button's centre lands on the shared left play-area column
        // (X=150, same as Force Field/Water Balloon/the Settings gear) — 150 minus half of 120.
        private const float MapButtonLeftInset = 90f;
        // Anchor is vertical-mid (Y=540), so this is an offset from there, not an absolute Y —
        // MV-676: was 306 (desired centre 846), raised to desired centre 894 as part of the same
        // column-wide gap widening as ForceFieldRise/WaterBalloonJoystickRise/SettingsPanel.GearRise —
        // 894 minus 540.
        private const float MapButtonRise = 354f;

        /// <summary>The always-available MAP button (MV-563), replacing the old always-on minimap this
        /// ticket removes outright. Mirrors <see cref="BuildWeaponsButton"/>'s own placement — mid-left,
        /// above the move stick, instead of mid-right above the aim stick — so the two read as a matched
        /// pair of primary HUD actions, clear of both twin sticks and the top-left utility column. Opens
        /// <see cref="MapScreen"/> full-screen; the game pauses exactly as it does for THE RIG.</summary>
        private void BuildMapButton()
        {
            var root = NewRect("Map Button", Root);
            Anchor(root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            root.sizeDelta = new Vector2(MapButtonSize, MapButtonSize);
            root.anchoredPosition = new Vector2(MapButtonLeftInset, MapButtonRise); // topmost of the left play-area column (MV-645)

            var bg = AddImage(root, HudTextures.RoundedBox(64, 0.28f), PanelColor, "Map BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;

            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnMapButtonTapped);

            var ring = AddImage(root, HudTextures.RoundedBoxOutline(64, 0.28f, 3f), WeaponsButtonIdleRingColor, "Ring");
            Stretch(ring.rectTransform);
            ring.raycastTarget = false;

            var label = AddText(root, 20f, BoneWhite, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, -10f);
            label.text = "MAP";
            label.fontStyle = FontStyle.Bold;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 12;
            label.resizeTextMaxSize = 22;
            label.raycastTarget = false;
        }

        /// <summary>The button's two corner badges (MV-425), replacing the single 56px chip YT-131/
        /// YT-178/MV-358 built up over time: amber count top-left ("parts to fit" — a part or ability
        /// credit banked), cyan "!" top-right ("module captured" — a Morphing Module draft waiting,
        /// <see cref="PendingMorphingModule"/>). Fixed opposite corners so both can be up at once
        /// (the "Both" state) without colliding. Same glow-ring-behind-chip shape the old single badge
        /// used (MV-300), just split and shrunk to fit two.</summary>
        private void BuildWeaponsButtonBadges()
        {
            _moduleBadgeRoot = BuildCornerBadge("Module Badge", new Vector2(1f, 1f), new Vector2(10f, 12f),
                out _moduleBadgeGlow, out _moduleBadgeBg);
            _moduleBadgeMark = AddText(_moduleBadgeRoot, 22f, Color.black, TextAnchor.MiddleCenter);
            Stretch(_moduleBadgeMark.rectTransform);
            _moduleBadgeMark.text = "!";
            _moduleBadgeMark.fontStyle = FontStyle.Bold;
            _moduleBadgeMark.raycastTarget = false;

            RefreshWeaponsButtonAlert();
        }

        private RectTransform BuildCornerBadge(string name, Vector2 corner, Vector2 offset, out Image glow, out Image bg)
        {
            var root = NewRect(name, _weaponsButtonRoot);
            Anchor(root, corner, corner, corner);
            root.sizeDelta = new Vector2(40f, 40f);
            root.anchoredPosition = offset;

            glow = AddImage(root, HudTextures.RoundedBox(64, 0.5f), Color.clear, "Glow Ring");
            Stretch(glow.rectTransform, 8f); // expands beyond the chip as a halo
            glow.type = Image.Type.Sliced;
            glow.raycastTarget = false;

            bg = AddImage(root, HudTextures.RoundedBox(48, 0.5f), SupercellColor, "Chip");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;  // the WEAPONS button underneath handles taps

            root.gameObject.SetActive(false);
            return root;
        }

        private void BuildFloatingLayer()
        {
            var go = new GameObject("Floating Text", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(FullRoot, false);
            Stretch(rect);
            _floating = go.AddComponent<FloatingTextLayer>();
            _floating.Init(rect, _canvas, _worldCamera);
        }

        /// <summary>MV-519: a full-stretch layer the Supercell pickup event's burst + "+10" flyup live
        /// on — parented at <see cref="FullRoot"/>, same as <see cref="_floating"/>, so a world position
        /// projects into it exactly the same way pickup toasts already do. Both children start inactive;
        /// <see cref="StartSupercellFx"/> activates them per event.</summary>
        private void BuildSupercellFx()
        {
            var root = NewRect("Supercell FX", FullRoot);
            Stretch(root);
            _supercellFxRoot = root;

            _supercellFxBurst = AddImage(root, HudTextures.RoundedBox(64, 0.5f), SupercellColor, "Burst");
            Anchor(_supercellFxBurst.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _supercellFxBurst.rectTransform.sizeDelta = new Vector2(40f, 40f);
            _supercellFxBurst.raycastTarget = false;
            _supercellFxBurst.gameObject.SetActive(false);

            _supercellFxLabel = AddText(root, 30f, SupercellColor, TextAnchor.MiddleCenter);
            Anchor(_supercellFxLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _supercellFxLabel.rectTransform.sizeDelta = new Vector2(160f, 50f);
            _supercellFxLabel.fontStyle = FontStyle.Bold;
            _supercellFxLabel.gameObject.SetActive(false);
        }

        // ---------- small UI helpers ----------

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private Image AddImage(Transform parent, Sprite sprite, Color color, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        private Text AddText(Transform parent, float size, Color color, TextAnchor align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = HudFont.Get();
            t.fontSize = Mathf.RoundToInt(size);
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static void Anchor(RectTransform r, Vector2 min, Vector2 max, Vector2 pivot)
        {
            r.anchorMin = min; r.anchorMax = max; r.pivot = pivot;
        }

        private static void Stretch(RectTransform r, float padding = 0f)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(-padding, -padding);
            r.offsetMax = new Vector2(padding, padding);
        }

        /// <summary>MV-510 round 2 - <see cref="Stretch"/> with the right-side padding independently
        /// capped (e.g. so a decorative glow doesn't bleed past a nearby safe-area edge on that one
        /// side, while still blooming out to <paramref name="padding"/> everywhere else).</summary>
        private static void StretchCapRight(RectTransform r, float padding, float rightPadding)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(-padding, -padding);
            r.offsetMax = new Vector2(Mathf.Min(padding, rightPadding), padding);
        }

        private static void Center(RectTransform r, float size)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(size, size);
            r.anchoredPosition = Vector2.zero;
        }
    }
}
