using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Arena;
using MaxWorlds.Player;
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
        /// <summary>The Sentinel deploy button's colour (MV-362, MV-422: one sentinel only) — the
        /// primary's own blue, since the turret is meant to read as Max's own tech ("a hose pipe on a
        /// stick").</summary>
        private static readonly Color SentinelColor = new Color(0.45f, 0.65f, 0.85f);
        // The part-ready chip shares the on-ground collectible aura's colour (YT-147): the HUD tell and
        // the pickup it points at read as ONE language. Sourced from the constant the aura uses, not a
        // matched copy, so an art retune moves both at once. It is the shared ORANGE, deliberately NOT
        // the old gold (0.98,0.72,0.22) that read as yellow — the ticket's whole point.
        private static readonly Color PartColor = MaxWorlds.VFX.PickupArtDirector.CollectibleGlow;
        /// <summary>The WEAPONS button's idle-state ring (MV-425) — "deliberately recessive... it
        /// should disappear mid-fight," a thin cool grey rather than any of the amber/cyan alert hues.</summary>
        private static readonly Color WeaponsButtonIdleRingColor = new Color(0.55f, 0.58f, 0.62f, 1f);
        // Minimap fog-of-war (MV-264, spatial rework MV-341): a visited room is a plain dim readout,
        // current borrows the tech-ring cyan already used for "this is you" elsewhere on the HUD, and
        // a hidden room is not drawn at all — the panel behind it IS the fog.
        private static readonly Color MinimapVisitedColor = new Color(BoneWhite.r, BoneWhite.g, BoneWhite.b, 0.5f);
        private static readonly Color MinimapCurrentColor = TechRingColor;

        private const float RefW = 1920f, RefH = 1080f;

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
        private readonly float[] _slotReadyFlash = new float[2];
        private readonly bool[] _slotWasReady = new bool[2];

        // Ability slots (0 Bomb, 1 Ultimate — "B"/"U", also the two FORGE HUD slot ids, MV-426)
        private readonly Image[] _slotRadial = new Image[2];
        private readonly Image[] _slotGlow = new Image[2];
        private readonly Image[] _slotIcon = new Image[2];
        private readonly Text[] _slotLetter = new Text[2];
        private readonly Text[] _slotLocked = new Text[2];

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

        // Minimap (MV-264, spatial rework MV-341): a top-down room diagram scaled off the real
        // MapZone footprints, with a marker tracking the player's live position. Built lazily from
        // Update — not Awake — because BackyardPath loads its map in its own Awake, whose order
        // relative to this one Unity does not promise; EnsureMinimapBuilt keeps retrying each frame
        // until a map is actually there to read.
        private BackyardPath _backyardPath;
        private RectTransform _minimapFrame;
        private Image[] _minimapZoneImages;
        private RectTransform _minimapPlayerMarker;
        private Image _minimapBg;
        private Rect _minimapAreaBounds;
        private Vector2 _minimapFrameSize;
        private AreaVisibility[] _minimapStates = System.Array.Empty<AreaVisibility>();
        private int _minimapAreaCount;
        private int _shownMinimapArea = -1;

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
        // badges are built and animated separately below.
        private RectTransform _weaponsButtonRoot;
        private Image _weaponsButtonRing;
        private Image _weaponsButtonMark;
        private RectTransform _weaponsModuleHaloRoot;
        private Image _weaponsModuleHaloOuter, _weaponsModuleHaloInner;

        private RectTransform _moduleBadgeRoot;
        private Image _moduleBadgeGlow, _moduleBadgeBg;
        private Text _moduleBadgeMark;

        // MV-471: the always-on parts counter attached to THE RIG mark itself — replacing the old
        // "Parts Badge" that only appeared while a part was banked, regardless of whether that part
        // could actually buy anything. Stays visible and flashes only off RigActions' own
        // affordability check, never off "you are holding something". Its cell-side twin (the old
        // bare-number chip below the mark) is gone as of MV-510 — the moved power-cell readout
        // (_cellCounterRoot et al. above) took its slot and its flash instead.
        private RectTransform _rigPartCounterRoot;
        private Image _rigPartCounterGlow, _rigPartCounterBg;
        private Text _rigPartCounterText;

        private void Awake()
        {
            _health = FindFirstObjectByType<PlayerHealth>();
            _player = FindFirstObjectByType<PlayerController>();
            _abilities = FindFirstObjectByType<PlayerAbilities>();
            _backyardPath = FindFirstObjectByType<BackyardPath>();
            _worldCamera = Camera.main;
            _model = new HudModel();

            BuildCanvas();
            BuildBiomeTint();
            BuildUtilityIcons();
            BuildHomeButton();
            BuildAbilitySlots();
            BuildHydroButton();
            BuildForceFieldButton();
            BuildSentinelJoystick();
            BuildWaterBalloonJoystick();
            BuildWaterBalloonAutoFireToggle();
            BuildTeleportJoystick();
            BuildJoysticks();
            BuildArenaIndicator();
            BuildInvasionDial();
            BuildBossBar();
            BuildWarning();
            BuildWeaponsButton();
            BuildPowerCellCounter(); // parents onto _weaponsButtonRoot — must follow BuildWeaponsButton
            BuildWeaponsButtonBadges();
            BuildFloatingLayer();
            if (!SkipTouchControlsForTests) BuildTouchControls();

            _model.Boss.ActiveChanged += OnBossActiveChanged;
        }

        private void OnEnable()
        {
            HudSignals.DamageDealt += OnDamage;
            HudSignals.Pickup += OnPickup;
            HudSignals.EnemyKilled += OnEnemyKilled;
            HudSignals.FactoryRegistered += OnFactoryRegistered;
            HudSignals.FactoryDestroyed += OnFactoryDestroyed;
            HudSignals.BossRegistered += OnBossRegistered;
            HudSignals.BossEngaged += OnBossEngaged;
            HudSignals.BossHealthChanged += OnBossHealth;
            HudSignals.BossDefeated += OnBossDefeated;
            MaxWorlds.Pickups.PickupWallet.PowerCellsChanged += OnPowerCells;
            MaxWorlds.Pickups.PickupWallet.CapacityChanged += OnCellCapacity;
            MaxWorlds.Pickups.PickupWallet.PartsChanged += OnParts;
            UpgradeState.Changed += OnUpgradesChanged;
            WeaponSystemState.Changed += OnAbilitiesChanged;
            AbilityCreditBank.Changed += OnAbilityCreditsChanged;
            PendingMorphingModule.Changed += OnPendingModuleChanged;
        }

        private void OnDisable()
        {
            HudSignals.DamageDealt -= OnDamage;
            HudSignals.Pickup -= OnPickup;
            HudSignals.EnemyKilled -= OnEnemyKilled;
            HudSignals.FactoryRegistered -= OnFactoryRegistered;
            HudSignals.FactoryDestroyed -= OnFactoryDestroyed;
            HudSignals.BossRegistered -= OnBossRegistered;
            HudSignals.BossEngaged -= OnBossEngaged;
            HudSignals.BossHealthChanged -= OnBossHealth;
            HudSignals.BossDefeated -= OnBossDefeated;
            MaxWorlds.Pickups.PickupWallet.PowerCellsChanged -= OnPowerCells;
            MaxWorlds.Pickups.PickupWallet.CapacityChanged -= OnCellCapacity;
            MaxWorlds.Pickups.PickupWallet.PartsChanged -= OnParts;
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
            if (_forceFieldButtonRoot != null)
                _forceFieldButtonRoot.gameObject.SetActive(WeaponSystemState.IsAcquired(AbilityKind.ForceField));
        }

        private void OnPowerCells(int total)
        {
            if (_cellCount != null) _cellCount.text = $"{total}/{MaxWorlds.Pickups.PickupWallet.Capacity}";
            _cellPop = 1f;   // a brief scale pop so a banked cell registers
        }

        /// <summary>MV-374: the reserve's cap itself moved (a Cell Capacity level-up) — the count
        /// didn't change, but the "current/max" text still needs to redraw for the new max.</summary>
        private void OnCellCapacity(int capacity)
        {
            if (_cellCount != null) _cellCount.text = $"{MaxWorlds.Pickups.PickupWallet.PowerCells}/{capacity}";
        }

        private void OnParts(int banked) => RefreshWeaponsButtonAlert();

        /// <summary>MV-358: a banked shed credit flashes the exact same WEAPONS-button badge a banked
        /// part already does — "something is waiting in the Abilities screen", regardless of which kind
        /// — rather than a separate tell the player has to learn twice.</summary>
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
            if (_rigPartCounterText != null)
                _rigPartCounterText.text = (MaxWorlds.Pickups.PickupWallet.PartsBanked + AbilityCreditBank.Banked).ToString();
        }

        /// <summary>MV-471: the ring's amber "parts to fit" state now tracks the same "is a part spend
        /// actually possible" question as the new PART counter (<see cref="RigActions.AnyPartActionAffordable"/>)
        /// instead of "are you merely holding one" — a banked ability credit is always immediately
        /// spendable (BUILD ABILITY never fails while one is banked), so it still counts on its own.</summary>
        private static WeaponsButtonAlert CurrentWeaponsButtonAlert() => ComputeWeaponsButtonAlert(
            AnyPartAlertActionable(),
            PendingMorphingModule.HasPending);

        private static bool AnyPartAlertActionable() =>
            RigActions.AnyPartActionAffordable(MaxWorlds.Pickups.PickupWallet.PartsBanked, MaxWorlds.Pickups.PickupWallet.PowerCells) ||
            AbilityCreditBank.Banked > 0;

        /// <summary>Pure predicate (MV-358) — pinned by an EditMode test without building a canvas: a
        /// spend is waiting if either kind of banked token is &gt; 0.</summary>
        public static bool ShouldShowPartAlert(int partsBanked, int abilityCreditsBanked) =>
            partsBanked > 0 || abilityCreditsBanked > 0;

        /// <summary>The WEAPONS button's four alert states (MV-425). Amber ("parts to fit") means
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
        /// matched copy" idiom <see cref="PartColor"/> already follows.</summary>
        public static Color ModuleColor => RigBoardLayout.Colour("module");

        /// <summary>The ring/mark stroke colour for each state — module cyan sourced from
        /// <see cref="ModuleColor"/> so the HUD tell and the board it points at (MV-433's node glow)
        /// never drift apart.</summary>
        public static Color WeaponsButtonRingColor(WeaponsButtonAlert alert) =>
            ShowsModuleRing(alert) ? ModuleColor
            : alert == WeaponsButtonAlert.PartsToFit ? PartColor
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

        private void OnBossRegistered() => _model.UseExternalBoss();
        private void OnBossEngaged(string name, int phases) => _model.EngageBossExternal(name, phases);
        private void OnBossHealth(float normalized) => _model.SetBossHealth(normalized);
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

        private void OnEnemyKilled(Vector3 pos)
        {
            _model.RegisterKill();
            _floating?.Spawn(pos + Vector3.up * 1.8f, $"+{_model.SparksPerKill} SPARKS", XpColor, false, 1.0f, 30f);
        }

        private void OnBossActiveChanged(bool active)
        {
            _bossRoot.gameObject.SetActive(active);
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

            UpdateAbilitySlots(dt);
            UpdateHydroButton(dt);
            UpdateForceFieldButton(dt);
            UpdateSentinelJoystick();
            UpdateAbilityControls();
            UpdateJoysticks();
            UpdateArena(dt);
            EnsureMinimapBuilt();
            UpdateMinimap();
            UpdateInvasionDial(dt);
            UpdateBoss();
            UpdateWarnings(dt);
            UpdateDrops(dt);
            UpdateWeaponsButton();
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
        /// <see cref="PartColor"/> — the same orange the on-ground pickup glows — so the two never drift
        /// and neither is the forbidden yellow.
        /// </summary>
        public static Color PartAlertColor(float t)
        {
            t = Mathf.Clamp01(t);
            // MV-300: a deeper trough so the beat reads as a strong pulse, not a gentle wobble.
            Color c = PartColor * (0.32f + 0.68f * t);   // dim -> full orange
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

        /// <summary>MV-471: redraws both always-on RIG-mark counters every frame — live count text plus
        /// a flash that engages only when <see cref="RigActions"/> says that currency actually buys
        /// something right now. A banked ability credit is always instantly spendable, so it counts
        /// toward the PART counter's flash the same way it does for the ring, <see cref="AnyPartAlertActionable"/>.</summary>
        private void UpdateRigCounters()
        {
            int partsBanked = MaxWorlds.Pickups.PickupWallet.PartsBanked;
            int creditsBanked = AbilityCreditBank.Banked;
            if (_rigPartCounterRoot != null)
            {
                if (_rigPartCounterText != null) _rigPartCounterText.text = (partsBanked + creditsBanked).ToString();
                bool actionable = RigActions.AnyPartActionAffordable(partsBanked, MaxWorlds.Pickups.PickupWallet.PowerCells) || creditsBanked > 0;
                SetRigCounterFlash(_rigPartCounterRoot, _rigPartCounterBg, _rigPartCounterGlow, PartColor, actionable);
            }

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

        private static readonly string[] AbilitySlotGlyphs = { "B", "U" };

        /// <summary>MV-426: a forged FORGE fusion permanently occupies its named slot ("B"/"U"),
        /// replacing the LOCKED placeholder with its icon and a steady ready-glow — none of the four
        /// fusion effects (DELUGE/BLINKGUARD/OVERCHARGE/SKIRMISH) are player-activated, so there is no
        /// cooldown to wipe; the slot simply reads as permanently equipped. An unforged slot keeps the
        /// pre-RIG Bomb/Ultimate placeholder behaviour untouched.</summary>
        private void UpdateAbilitySlots(float dt)
        {
            UpdateAbilitySlot(0, _model.Bomb.RadialFill, _model.Bomb.Ready);
            UpdateAbilitySlot(1, _model.UltimateRadialFill, _model.UltimateReady);
        }

        private void UpdateAbilitySlot(int i, float placeholderRadialFill, bool placeholderReady)
        {
            string fusionId = RigFusionState.ForgedInSlot(AbilitySlotGlyphs[i]);
            bool forged = fusionId != null;

            _slotLocked[i].gameObject.SetActive(!forged);
            _slotLetter[i].gameObject.SetActive(!forged);
            _slotIcon[i].gameObject.SetActive(forged);

            if (forged)
            {
                _slotIcon[i].sprite = HudTextures.VectorIcon(RigBoardLayout.Icon("fuse"), 40);
                _slotIcon[i].color = BoneWhite;
                _slotRadial[i].fillAmount = 0f;
                SetSlot(i, 0f, true);
            }
            else
            {
                SetSlot(i, placeholderRadialFill, placeholderReady);
            }
        }

        private void SetSlot(int i, float radialFill, bool ready)
        {
            _slotRadial[i].fillAmount = radialFill;

            // A one-shot bright flash at the MOMENT the slot comes off cooldown, decaying into the
            // steady ready-pulse. The steady glow alone tells you the slot is ready; it doesn't tell
            // you that it *just became* ready, which is the moment the player is waiting for.
            if (ready && !_slotWasReady[i]) _slotReadyFlash[i] = 1f;
            _slotWasReady[i] = ready;
            _slotReadyFlash[i] = Mathf.Max(0f, _slotReadyFlash[i] - Time.deltaTime * 3.2f);

            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 4f));
            var c = Color.Lerp(ReadyGlow, Color.white, _slotReadyFlash[i] * 0.7f);
            c.a = ready ? Mathf.Clamp01(pulse + _slotReadyFlash[i]) : 0f;
            _slotGlow[i].color = c;

            float pop = 1f + 0.12f * _slotReadyFlash[i];
            _slotGlow[i].rectTransform.localScale = new Vector3(pop, pop, 1f);
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
            string[] glyphs = { "P", "?", "S" }; // Pack/Journal, Help, Settings (greybox letters)
            var col = NewRect("Utility Icons", Root);
            Anchor(col, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            col.anchoredPosition = new Vector2(24f, -24f);
            col.sizeDelta = new Vector2(56f, 200f);
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
        /// The top-right slots — Bomb and Ultimate, and they stay honest: neither is implemented, so
        /// both are drawn dimmed with a LOCKED caption rather than glowing as though they were a
        /// button you were failing to find.
        /// </summary>
        private void BuildAbilitySlots()
        {
            string[] glyphs = { "B", "U" };      // Bomb, Ultimate — index 0 and 1
            var col = NewRect("Ability Slots", Root);
            Anchor(col, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            col.anchoredPosition = new Vector2(-24f, -24f);
            col.sizeDelta = new Vector2(72f, 160f);
            for (int i = 0; i < glyphs.Length; i++)
            {
                var slot = AddImage(col, HudTextures.RoundedBox(72, 0.24f), PanelColor, $"Slot {glyphs[i]}");
                Anchor(slot.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
                slot.rectTransform.sizeDelta = new Vector2(72f, 72f);
                slot.rectTransform.anchoredPosition = new Vector2(0f, -i * 80f);
                slot.type = Image.Type.Sliced;

                // Ready glow (behind everything else in the slot).
                var glow = AddImage(slot.rectTransform, HudTextures.RoundedBox(80, 0.24f), Color.clear, "Glow");
                Stretch(glow.rectTransform, 6f); // expands 6px beyond the slot as a border ring
                glow.type = Image.Type.Sliced;
                glow.raycastTarget = false;
                _slotGlow[i] = glow;

                // Dimmed, and captioned. These were reported as buttons of unknown purpose (YT-116);
                // the truth is they are placeholders for abilities nobody has built, and a slot that
                // looks live is the thing that made them worth asking about.
                var letter = AddText(slot.rectTransform, 30f,
                                     new Color(BoneWhite.r, BoneWhite.g, BoneWhite.b, 0.45f),
                                     TextAnchor.MiddleCenter);
                Stretch(letter.rectTransform);
                letter.text = glyphs[i];
                _slotLetter[i] = letter;

                var locked = AddText(slot.rectTransform, 15f,
                                     new Color(BoneWhite.r, BoneWhite.g, BoneWhite.b, 0.5f),
                                     TextAnchor.LowerCenter);
                Stretch(locked.rectTransform);
                locked.text = "LOCKED";
                _slotLocked[i] = locked;

                // A forged FORGE fusion's icon (MV-426) — hidden until RigFusionState.ForgedInSlot
                // says this slot is occupied; see UpdateAbilitySlots.
                var icon = new GameObject("Fusion Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                icon.transform.SetParent(slot.rectTransform, false);
                Stretch(icon.rectTransform, 14f);
                icon.raycastTarget = false;
                icon.gameObject.SetActive(false);
                _slotIcon[i] = icon;

                // Cooldown radial wipe overlay (darkens the covered fraction).
                var radial = AddImage(slot.rectTransform, HudTextures.Disc(96), new Color(0f, 0f, 0f, 0.62f), "Radial");
                Stretch(radial.rectTransform, -8f); // sits just inside the slot box
                radial.type = Image.Type.Filled;
                radial.fillMethod = Image.FillMethod.Radial360;
                radial.fillOrigin = (int)Image.Origin360.Top;
                radial.fillClockwise = true;
                radial.fillAmount = 0f;
                radial.raycastTarget = false;
                _slotRadial[i] = radial;
            }
        }

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
        /// The Force Field button (MV-361) — stacked above Hydro in the same right-hand column, same
        /// round action-button shape. Hidden until <see cref="AbilityKind.ForceField"/> is acquired.
        /// </summary>
        private void BuildForceFieldButton()
        {
            var root = NewRect("Force Field Button", Root);
            Anchor(root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f));
            root.anchoredPosition = new Vector2(-HydroButtonInset, HydroButtonRise + HydroButtonSize + ForceFieldButtonGap);
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

            _forceFieldLabel = AddText(root, 20f, ForceFieldColor, TextAnchor.MiddleCenter);
            Stretch(_forceFieldLabel.rectTransform);
            _forceFieldLabel.text = "FIELD";
            _forceFieldLabel.fontStyle = FontStyle.Bold;
            _forceFieldLabel.raycastTarget = false;
            _forceFieldLabel.resizeTextForBestFit = true;
            _forceFieldLabel.resizeTextMinSize = 10;
            _forceFieldLabel.resizeTextMaxSize = 22;

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

        private const float ForceFieldButtonGap = 16f;   // clearance above the Hydro button below it

        // The Sentinel deploy joystick (MV-362, aimed placement MV-399, one sentinel only MV-422):
        // well clear of the Hydro/Force Field column's own stack below (top edge ~620) and the boss
        // bar's y-band (rise 300, half 8) beneath that — same "half-extent-plus-margin clearance"
        // reasoning the Water Balloon/Teleport column below uses for itself.
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
            var denied = AddImage(_sentinelRoot, WeaponHudIcons.PowerCellDenied(64), Color.white, "Insufficient Cells");
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

        // The left-hand mirror of the Hydro column (WV-240, spec §6a): Water Balloon's joystick sits
        // above the Move stick the same way Hydro sits above the Aim stick, so aiming a throw never
        // costs the player their movement thumb. Teleport stacks above it, with extra clearance for
        // the joystick's own oversized invisible touch pad (matches AddOnScreenStick's ±30 px
        // fat-finger margin), not just its artwork.
        // Raised clear of the boss bar's y-band (rise 300, half 8) so a boss fight never crosses it.
        // Expressed as a shared Root-local X so the two controls line up visually even though
        // AbilityControlArt.BuildJoystick anchors to the parent's bottom-CENTER while BuildButton
        // anchors to bottom-RIGHT — each conversion below accounts for that.
        private const float AbilityControlColumnX = 450f;
        private const float WaterBalloonJoystickRise = 480f;
        private const float WaterBalloonJoystickMaxHalfSize = 100f;   // half of BuildJoystick's 200 px cap
        private const float WaterBalloonTouchPadMargin = 30f;
        // MV-338: Teleport is now a joystick too (same shape as Water Balloon's, including its own
        // fat-finger touch pad), so it stacks above Water Balloon with the same half-extent-plus-margin
        // clearance on both sides rather than a button's smaller footprint.
        private const float TeleportJoystickRise = WaterBalloonJoystickRise
            + (WaterBalloonJoystickMaxHalfSize + WaterBalloonTouchPadMargin) * 2f + 24f;

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
            Vector2 anchoredPos = new Vector2(AbilityControlColumnX - RefW * 0.5f, WaterBalloonJoystickRise);
            _waterBalloonVisual = AbilityControlArt.BuildJoystick(
                Root, "Water Balloon Joystick", anchoredPos, WaterBalloonColor, "Balloon", level, maxLevel);
            _waterBalloonRoot = _waterBalloonVisual.Root;

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
            // Sits just above the joystick's own rings, clear of the Teleport joystick's touch pad
            // stacked above it (TeleportJoystickRise's own margin math starts higher still).
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
        /// press/drag/release aim + blink, the same hand-off shape Water Balloon's joystick uses.</summary>
        private void BuildTeleportJoystick() => RebuildTeleportJoystick();

        private void RebuildTeleportJoystick()
        {
            if (_teleportRoot != null) Destroy(_teleportRoot.gameObject);

            int level = Mathf.Max(1, WeaponSystemState.AbilityLevel(AbilityKind.Teleport));
            int maxLevel = WeaponCatalog.MaxLevel(AbilityKind.Teleport);
            Vector2 anchoredPos = new Vector2(AbilityControlColumnX - RefW * 0.5f, TeleportJoystickRise);
            _teleportVisual = AbilityControlArt.BuildJoystick(
                Root, "Teleport Joystick", anchoredPos, TeleportColor, "Teleport", level, maxLevel);
            _teleportRoot = _teleportVisual.Root;

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

        /// <summary>Test hook (MV-264): what the minimap is currently showing, one entry per area in
        /// order — the same states <see cref="UpdateMinimap"/> just painted, not a second computation
        /// of them. Empty until a map with area zones has actually loaded.</summary>
        public AreaVisibility[] MinimapStates => _minimapStates;

        /// <summary>Test hook (MV-278): true once the minimap has a visible backing panel behind it,
        /// so the widget reads against any 3D background instead of floating bare over the world.
        /// False until a map with area zones has loaded and built the frame.</summary>
        public bool MinimapHasBackdrop => _minimapBg != null && _minimapBg.color.a > 0f;

        /// <summary>
        /// The spatial minimap (MV-264 introduced the fog-of-war area strip; MV-341 redraws it as a
        /// true top-down room diagram — the strip's tiny stacked pips read as decoration, not a map,
        /// and gave no sense of the player's actual position). One rectangle per "area&lt;N&gt;" zone
        /// the loaded map defines (never a hardcoded ten), scaled to its real footprint via
        /// <see cref="MinimapModel.AreaBounds"/>/<see cref="MinimapModel.NormalizedZoneRect"/>.
        ///
        /// MV-354: moved to the LEFT side — the right side is the thumb-side of the screen (ability
        /// slots, Hydro, the aim stick), and the minimap was competing with those controls for
        /// space. Sits under the Utility Icons/Home Button column, the same clearance gap that column
        /// gave the old top-right minimap under the ability slots. Its x-range (24-224) sits well clear
        /// of the Water Balloon/Teleport joysticks (centred at x=450, ±130 with touch-pad margin), and
        /// its y-range is well above the Move joystick (bottom-left) — see <see cref="HudLayoutPlayTests"/>-
        /// style non-overlap discipline; no widget here is under the player's left thumb.
        ///
        /// Deferred to <see cref="Update"/> rather than built in <see cref="Awake"/>: <see cref="BackyardPath"/>
        /// loads its map inside its own Awake, and Unity does not promise this component's Awake runs
        /// after that one's. Idempotent — bails the instant it has built (or given up on) a map.
        /// </summary>
        private void EnsureMinimapBuilt()
        {
            if (_minimapZoneImages != null) return;

            MapData map = _backyardPath != null ? _backyardPath.Map : null;
            if (map == null) return; // BackyardPath hasn't loaded its map yet — try again next frame

            _minimapAreaCount = MinimapModel.CountAreas(map);
            if (_minimapAreaCount <= 0)
            {
                _minimapZoneImages = System.Array.Empty<Image>(); // no area-gated map here — stop retrying
                return;
            }

            const float FrameSize = 200f, Padding = 14f;
            _minimapAreaBounds = MinimapModel.AreaBounds(map);
            _minimapFrameSize = new Vector2(FrameSize - Padding * 2f, FrameSize - Padding * 2f);

            var root = NewRect("Minimap", Root);
            Anchor(root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            root.sizeDelta = new Vector2(FrameSize, FrameSize);
            root.anchoredPosition = new Vector2(24f, -250f); // under the utility icon column

            // A backing panel (MV-278): every other HUD readout — status bar, ability slots, utility
            // icons — sits on a solid PanelColor backdrop, so a hidden (undrawn) room reads as fog
            // rather than as a hole punched through to whatever terrain is behind the HUD.
            var bg = AddImage(root, HudTextures.RoundedBox(28, 0.18f), PanelColor, "Minimap BG");
            Stretch(bg.rectTransform);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;
            _minimapBg = bg;

            _minimapFrame = NewRect("Frame", root);
            Anchor(_minimapFrame, Vector2.zero, Vector2.zero, Vector2.zero);
            _minimapFrame.sizeDelta = _minimapFrameSize;
            _minimapFrame.anchoredPosition = new Vector2(Padding, Padding);

            _minimapZoneImages = new Image[_minimapAreaCount];
            foreach (MapZone zone in map.zones)
            {
                if (zone == null) continue;
                int areaIndex = AreaAccumulationDirector.AreaIndexOf(zone.id);
                if (areaIndex <= 0 || areaIndex > _minimapAreaCount) continue;

                Rect norm = MinimapModel.NormalizedZoneRect(_minimapAreaBounds, zone);
                var room = AddImage(_minimapFrame, HudTextures.RoundedBox(16, 0.3f), MinimapVisitedColor, $"Area {areaIndex}");
                Anchor(room.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero);
                room.rectTransform.anchoredPosition = new Vector2(norm.x * _minimapFrameSize.x, norm.y * _minimapFrameSize.y);
                room.rectTransform.sizeDelta = new Vector2(
                    Mathf.Max(8f, norm.width * _minimapFrameSize.x),
                    Mathf.Max(8f, norm.height * _minimapFrameSize.y));
                room.type = Image.Type.Sliced;
                room.raycastTarget = false;
                room.gameObject.SetActive(false); // fog-of-war: UpdateMinimap reveals it once reached
                _minimapZoneImages[areaIndex - 1] = room;
            }

            // The player marker (MV-341 AC: "showing the player's current position") — a bright dot
            // with a soft glow, the same tech-ring cyan used for "this is you" elsewhere on the HUD.
            _minimapPlayerMarker = NewRect("Player Marker", _minimapFrame);
            Anchor(_minimapPlayerMarker, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            _minimapPlayerMarker.sizeDelta = new Vector2(22f, 22f);

            var glow = AddImage(_minimapPlayerMarker, HudTextures.Disc(32),
                new Color(MinimapCurrentColor.r, MinimapCurrentColor.g, MinimapCurrentColor.b, 0.4f), "Glow");
            Stretch(glow.rectTransform);
            glow.raycastTarget = false;

            var dot = AddImage(_minimapPlayerMarker, HudTextures.Disc(24), BoneWhite, "Dot");
            Center(dot.rectTransform, 10f);
            dot.raycastTarget = false;

            UpdateMinimapPlayerMarker();
        }

        /// <summary>Repaints the room rectangles off the live
        /// <see cref="AreaAccumulationDirector.CurrentArea"/> — only when it has actually changed, so a
        /// built map costs nothing on the frames between area entries — then updates the player marker
        /// every frame so it tracks smoothly rather than snapping on area boundaries.</summary>
        private void UpdateMinimap()
        {
            if (_minimapZoneImages == null || _minimapZoneImages.Length == 0) return;

            int currentArea = 1;
            if (_backyardPath != null && _backyardPath.AreaDirector != null)
                currentArea = _backyardPath.AreaDirector.CurrentArea;

            if (currentArea != _shownMinimapArea)
            {
                _shownMinimapArea = currentArea;
                _minimapStates = MinimapModel.BuildStates(_minimapAreaCount, currentArea);
                for (int i = 0; i < _minimapZoneImages.Length; i++)
                {
                    Image room = _minimapZoneImages[i];
                    if (room == null) continue;

                    AreaVisibility state = _minimapStates[i];
                    room.gameObject.SetActive(state != AreaVisibility.Hidden); // fog-of-war
                    room.color = state == AreaVisibility.Current ? MinimapCurrentColor : MinimapVisitedColor;
                }
            }

            UpdateMinimapPlayerMarker();
        }

        private void UpdateMinimapPlayerMarker()
        {
            if (_minimapPlayerMarker == null || _player == null) return;

            Vector3 pos = _player.transform.position;
            Vector2 norm = MinimapModel.NormalizedPosition(_minimapAreaBounds, pos.x, pos.z);
            _minimapPlayerMarker.anchoredPosition = new Vector2(norm.x * _minimapFrameSize.x, norm.y * _minimapFrameSize.y);
        }

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

        private void BuildWarning()
        {
            _warning = AddText(Root, 60f, Color.white, TextAnchor.MiddleCenter);
            Anchor(_warning.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _warning.rectTransform.sizeDelta = new Vector2(900f, 100f);
            _warning.rectTransform.anchoredPosition = new Vector2(0f, 160f);
            _warning.fontStyle = FontStyle.Bold;
            _warning.gameObject.SetActive(false);
        }

        /// <summary>Gap between THE WEAPONS button's hex mark and the counter chip/readout on either
        /// side of it (MV-471's original 6f, kept for the parts chip above; MV-510 gives the larger
        /// cell readout below its own, roomier gap so it doesn't crowd the doubled mark).</summary>
        private const float RigCounterGap = 6f;
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
        /// HUD at 40pt. Capped down to sit as a visual peer with the parts count's own cap
        /// (<see cref="RigPartTextMaxSize"/>), not above it (AC A1).</summary>
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
            var root = NewRect("Power Cells", _weaponsButtonRoot);
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
            _cellIcon = AddImage(root, WeaponHudIcons.PowerCell(64), Color.white, "Cell Icon");
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

        private const float WeaponsButtonRightInset = 8f;

        /// <summary>The always-available WEAPONS button (YT-178, redrawn MV-425): a hexagonal mark —
        /// three linked nodes, a miniature of THE RIG board itself — replacing the old ABILITIES pill
        /// in place (same anchor; the (-28, 120) position MV-425 gave it moved to
        /// <see cref="WeaponsButtonRightInset"/> in MV-510 round 2). All procedural: hexagons, circles,
        /// strokes, no art asset, no font glyph (<c>HudFont</c> has no coverage for this symbol). The
        /// ring/halo are driven every frame in <see cref="UpdateWeaponsButton"/> off
        /// <see cref="WeaponsButtonAlert"/>; the two corner badges are a separate build,
        /// <see cref="BuildWeaponsButtonBadges"/>.</summary>
        private void BuildWeaponsButton()
        {
            _weaponsButtonRoot = NewRect("Weapons Button", Root);
            Anchor(_weaponsButtonRoot, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            _weaponsButtonRoot.sizeDelta = new Vector2(WeaponsButtonSize, WeaponsButtonSize);
            _weaponsButtonRoot.anchoredPosition = new Vector2(-WeaponsButtonRightInset, 120f); // right edge, above the aim stick

            // Module-captured halo (double ring, MV-425 spec): behind everything else, only ever active
            // for ModuleCaptured/Both (RefreshWeaponsButtonAlert). Sized as multiples of the button's
            // own radius, same GlowRadiusMultiplier-style idiom THE RIG board's own node glow uses.
            _weaponsModuleHaloRoot = NewRect("Module Halo", _weaponsButtonRoot);
            Stretch(_weaponsModuleHaloRoot);
            float halfSize = WeaponsButtonSize * 0.5f;
            _weaponsModuleHaloOuter = AddImage(_weaponsModuleHaloRoot, HudTextures.Glow(128), Color.clear, "Halo Outer");
            Stretch(_weaponsModuleHaloOuter.rectTransform, halfSize * 0.42f); // r*1.42
            _weaponsModuleHaloOuter.raycastTarget = false;
            _weaponsModuleHaloInner = AddImage(_weaponsModuleHaloRoot, HudTextures.Glow(128), Color.clear, "Halo Inner");
            Stretch(_weaponsModuleHaloInner.rectTransform, halfSize * 0.24f); // r*1.24
            _weaponsModuleHaloInner.raycastTarget = false;
            _weaponsModuleHaloRoot.gameObject.SetActive(false);

            var bg = AddImage(_weaponsButtonRoot, HudTextures.Polygon(6, -90f, WeaponsButtonHexTex, WeaponsButtonHexTex), PanelColor, "Weapons BG");
            Stretch(bg.rectTransform);
            bg.raycastTarget = true;

            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnWeaponsButtonTapped);

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

            BuildRigPartCounter();

            RefreshWeaponsButtonAlert();
        }

        /// <summary>MV-425's hex-nut-and-bolt glyph for the PART counter (MV-510 item 6 — mirror the
        /// cell readout's icon-plus-number, so both counters read the same way). Plain geometry via
        /// <see cref="HudTextures.VectorIcon"/>, the same idiom THE WEAPONS button's own mark uses —
        /// no font glyph, no committed art.</summary>
        private const string RigPartGlyphSvg =
            "<path d=\"M0,-11 L9.5,-5.5 L9.5,5.5 L0,11 L-9.5,5.5 L-9.5,-5.5 Z\" fill=\"none\" stroke=\"#ICON#\" stroke-width=\"3\"/>" +
            "<circle cx=\"0\" cy=\"0\" r=\"3.2\" fill=\"#ICON#\" stroke=\"none\"/>";

        private const float RigPartChipWidth = 110f;
        private const float RigPartChipHeight = 48f;
        private const float RigPartTextMinSize = 20f;

        /// <summary>MV-510 review round 1 (AC A1): kept equal to <see cref="CellCounterTextMaxSize"/>
        /// so the two resolved best-fit sizes land as peers, not just "close by construction".</summary>
        private const float RigPartTextMaxSize = CellCounterTextMaxSize;
        private const float RigPartIconSize = 26f;

        /// <summary>MV-471: the always-on PART count above the mark, replacing the old "Parts Badge"
        /// corner chip that only showed up while a part was banked. <see cref="UpdateRigCounters"/>
        /// drives its text and flash every frame. MV-510 enlarged the text (18 -&gt; <see cref="RigPartTextMaxSize"/>)
        /// and gave it its own icon, matching the moved cell readout below the mark; its old cell-side
        /// twin is gone (that value now lives in the moved readout, see <see cref="BuildPowerCellCounter"/>).
        /// Review round 1 also made it best-fit driven, matching the cell readout's own mechanism.</summary>
        private void BuildRigPartCounter()
        {
            var root = NewRect("Rig Part Counter", _weaponsButtonRoot);
            Anchor(root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f));
            root.sizeDelta = new Vector2(RigPartChipWidth, RigPartChipHeight);
            root.anchoredPosition = new Vector2(0f, RigCounterGap);
            _rigPartCounterRoot = root;

            _rigPartCounterGlow = AddImage(root, HudTextures.RoundedBox(64, 0.5f), Color.clear, "Glow Ring");
            Stretch(_rigPartCounterGlow.rectTransform, 6f); // expands beyond the chip as a halo
            _rigPartCounterGlow.type = Image.Type.Sliced;
            _rigPartCounterGlow.raycastTarget = false;

            _rigPartCounterBg = AddImage(root, HudTextures.RoundedBox(48, 0.5f), PanelColor, "Chip");
            Stretch(_rigPartCounterBg.rectTransform); _rigPartCounterBg.type = Image.Type.Sliced;
            _rigPartCounterBg.raycastTarget = false; // the WEAPONS button underneath handles taps

            var icon = AddImage(root, HudTextures.VectorIcon(RigPartGlyphSvg, 40), BoneWhite, "Part Icon");
            Anchor(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            icon.rectTransform.sizeDelta = new Vector2(RigPartIconSize, RigPartIconSize);
            icon.rectTransform.anchoredPosition = new Vector2(8f + RigPartIconSize * 0.5f, 0f);
            icon.raycastTarget = false;

            _rigPartCounterText = AddText(root, RigPartTextMaxSize, BoneWhite, TextAnchor.MiddleRight);
            Anchor(_rigPartCounterText.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f));
            _rigPartCounterText.rectTransform.offsetMin = new Vector2(8f + RigPartIconSize + 4f, 0f);
            _rigPartCounterText.rectTransform.offsetMax = new Vector2(-8f, 0f);
            _rigPartCounterText.fontStyle = FontStyle.Bold;
            // MV-510 review round 1 (AC A1): best-fit driven, like the cell readout, so the "roughly
            // the same optical size" requirement is a resolved-value fact, not two independently
            // authored constants that happen to match today.
            _rigPartCounterText.resizeTextForBestFit = true;
            _rigPartCounterText.resizeTextMinSize = (int)RigPartTextMinSize;
            _rigPartCounterText.resizeTextMaxSize = (int)RigPartTextMaxSize;
            _rigPartCounterText.raycastTarget = false;
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

            bg = AddImage(root, HudTextures.RoundedBox(48, 0.5f), PartColor, "Chip");
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

        private static void Center(RectTransform r, float size)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(size, size);
            r.anchoredPosition = Vector2.zero;
        }
    }
}
