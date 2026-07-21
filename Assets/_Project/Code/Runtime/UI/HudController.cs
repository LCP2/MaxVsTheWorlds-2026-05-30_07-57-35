using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Player;
using MaxWorlds.Combat;
using MaxWorlds.VFX;

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
        // Backyard palette (Art Direction §Colour identity + HUD spec).
        private static readonly Color HpColor = new Color(0.90f, 0.22f, 0.20f);
        private static readonly Color XpColor = new Color(0.957f, 0.788f, 0.365f); // #F4C95D golden
        private static readonly Color TechRingColor = new Color(0.31f, 0.76f, 0.97f);
        private static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.09f, 0.55f);
        private static readonly Color ReadyGlow = new Color(1f, 0.85f, 0.35f);
        private static readonly Color BiomeTint = new Color(0.96f, 0.62f, 0.20f, 0.06f); // warm orange overlay
        private static readonly Color BossColor = new Color(0.85f, 0.12f, 0.12f);
        private static readonly Color BoneWhite = new Color(0.96f, 0.94f, 0.86f);
        // Robot-drop colours (YT-131): cyan power cell — matched to the world pickup's cyan core.
        private static readonly Color CellColor = new Color(0.31f, 0.86f, 0.98f);
        // The part-ready chip shares the on-ground collectible aura's colour (YT-147): the HUD tell and
        // the pickup it points at read as ONE language. Sourced from the constant the aura uses, not a
        // matched copy, so an art retune moves both at once. It is the shared ORANGE, deliberately NOT
        // the old gold (0.98,0.72,0.22) that read as yellow — the ticket's whole point.
        private static readonly Color PartColor = MaxWorlds.VFX.PickupArtDirector.CollectibleGlow;
        /// <summary>The power-up shout (YT-67). Hot cyan-white: it has to out-shout the golden
        /// SPARKS numbers flying around it, or the one moment that matters gets lost in them.</summary>
        private static readonly Color BoostColor = new Color(0.45f, 0.95f, 1f);

        private const float RefW = 1920f, RefH = 1080f;

        private HudModel _model;
        private PlayerHealth _health;
        private WaterBlaster _blaster;
        private PlayerController _player;
        private Camera _worldCamera;

        private Canvas _canvas;
        private RectTransform _safeRoot;
        private FloatingTextLayer _floating;

        // Status strip — just the level pip since YT-121 (life + water float over Max).
        private Image _xpFill;

        // YT-54 presentation state. None of this feeds the model — it only animates what the model
        // already says.
        private readonly BarState _xpBar = new BarState();
        private readonly DamageNumberAggregator _damageNumbers = new DamageNumberAggregator();
        private readonly System.Collections.Generic.List<DamageNumberAggregator.Entry> _damageBuffer =
            new System.Collections.Generic.List<DamageNumberAggregator.Entry>(16);
        private readonly float[] _slotReadyFlash = new float[3];
        private readonly bool[] _slotWasReady = new bool[3];
        private int _lastLevel = 1;
        private Text _xpLabel;

        // Ability slots (0 Dash, 1 Bomb, 2 Ultimate)
        private readonly Image[] _slotRadial = new Image[3];
        private readonly Image[] _slotGlow = new Image[3];

        // Joysticks
        private Image _moveRings, _moveArrow;
        private RectTransform _moveKnob, _moveArrowRect;
        private Image _aimRings, _aimCross;
        private RectTransform _aimKnob;

        // Touch controls (YT-98): the joystick/dash roots the on-screen sticks + button attach to.
        private RectTransform _moveJoystickRoot, _aimJoystickRoot, _dashButtonRoot;

        // Arena indicator
        private Text _arenaLabel;
        private float _arenaProminence; // 1 = full, fades toward a faint idle

        // Boss
        private RectTransform _bossRoot;
        private Image _bossFill;
        private RectTransform _bossSegments;
        private Text _bossName;

        // Warnings
        private Text _warning;
        private float _warningTimer;
        private float _bossIncomingTimer;

        // Robot drops (YT-131): banked power-cell counter + the flashing "install available" chip.
        private Text _cellCount;
        private Image _cellIcon;
        private float _cellPop;              // one-shot scale pop when a cell is banked
        private RectTransform _partAlertRoot;
        private Image _partAlertBg;
        private Text _partAlertLabel;

        private void Awake()
        {
            _health = FindFirstObjectByType<PlayerHealth>();
            _blaster = FindFirstObjectByType<WaterBlaster>();
            _player = FindFirstObjectByType<PlayerController>();
            _worldCamera = Camera.main;
            _model = new HudModel();

            BuildCanvas();
            BuildBiomeTint();
            BuildStatusStrip();
            BuildUtilityIcons();
            BuildAbilitySlots();
            BuildDashButton();
            BuildJoysticks();
            BuildArenaIndicator();
            BuildBossBar();
            BuildWarning();
            BuildPowerCellCounter();
            BuildPartAlert();
            BuildFloatingLayer();
            BuildMap();
            BuildTouchControls();

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
            MaxWorlds.Pickups.PickupWallet.PartsChanged += OnParts;
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
            MaxWorlds.Pickups.PickupWallet.PartsChanged -= OnParts;
        }

        private void OnPowerCells(int total)
        {
            if (_cellCount != null) _cellCount.text = total.ToString();
            _cellPop = 1f;   // a brief scale pop so a banked cell registers
        }

        private void OnParts(int pending)
        {
            // The chip is shown while any part is waiting to be installed (YT-131). It flashes in
            // Update; here we just toggle its presence. YT-132's upgrade screen spends the part.
            if (_partAlertRoot != null) _partAlertRoot.gameObject.SetActive(pending > 0);
        }

        /// <summary>Tapping the flashing chip opens the paused upgrade screen for the part at the front
        /// of the pending queue (YT-132/133) — the specific one Max picked up.</summary>
        private void OpenUpgrade()
        {
            if (!MaxWorlds.Pickups.PickupWallet.TryPeekPart(out var kind)) return;
            var screen = FindFirstObjectByType<UpgradeScreen>();
            if (screen != null) screen.Open(MaxWorlds.Upgrades.UpgradeCatalog.For(kind));
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
            _floating?.Spawn(pos + Vector3.up * 1.8f, $"+{_model.XpPerKill} SPARKS", XpColor, false, 1.0f, 30f);
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

            UpdateStatusStrip(dt);
            UpdateAbilitySlots(dt);
            UpdateJoysticks();
            UpdateArena(dt);
            UpdateBoss();
            UpdateWarnings(dt);
            UpdateDrops(dt);
            FlushDamageNumbers();
            CheckLevelUp();
        }

        private void UpdateDrops(float dt)
        {
            // Cell icon pops on a bank and settles back.
            _cellPop = Mathf.Max(0f, _cellPop - dt * 3f);
            if (_cellIcon != null)
            {
                float s = 1f + 0.35f * _cellPop;
                _cellIcon.rectTransform.localScale = new Vector3(s, s, 1f);
            }

            // The part chip FLASHES while it's shown — the "you have an upgrade waiting" tell that YT-132
            // turns into the upgrade screen. It now beats in the shared collectible orange (YT-147): a
            // real dim->bright + scale pulse that reads as a beacon, not the old barely-there alpha fade
            // on a gold badge that read as static and yellow.
            if (_partAlertRoot != null && _partAlertRoot.gameObject.activeSelf)
            {
                float t = PartAlertFlash(Time.unscaledTime);
                if (_partAlertBg != null) _partAlertBg.color = PartAlertColor(t);

                // A scale pop on the beat, so the flash reads even in a busy corner of the screen.
                float s = 1f + 0.11f * t;
                _partAlertRoot.localScale = new Vector3(s, s, 1f);

                if (_partAlertLabel != null)
                {
                    var lc = BoneWhite; lc.a = 0.65f + 0.35f * t; _partAlertLabel.color = lc;
                }
            }
        }

        /// <summary>
        /// The part-ready chip's flash, 0..1. Pure and driven by unscaled time so it keeps flashing
        /// while the upgrade screen has the game paused with the part still waiting (YT-147). ~1 Hz —
        /// a touch quicker than the on-ground aura's ambient breath, because this is an alert.
        /// </summary>
        public static float PartAlertFlash(float unscaledTime)
            => 0.5f + 0.5f * Mathf.Sin(unscaledTime * 6f);

        /// <summary>
        /// The chip's colour at flash amount <paramref name="t"/>: the shared collectible orange swung
        /// dim->full so it reads as an active beacon, not a static badge (YT-147). The hue is
        /// <see cref="PartColor"/> — the same orange the on-ground pickup glows — so the two never drift
        /// and neither is the forbidden yellow.
        /// </summary>
        public static Color PartAlertColor(float t)
        {
            t = Mathf.Clamp01(t);
            Color c = PartColor * (0.5f + 0.5f * t);   // dim -> full orange
            c.a = 0.72f + 0.28f * t;
            return c;
        }

        private void UpdateStatusStrip(float dt)
        {
            // Life and water are drawn over Max's head now (YT-121, WorldHealthBar). All that lives
            // up here is the level pip.
            _xpBar.Update(_model.Xp.Normalized, dt, fillSpeed: 2.2f, hold: 0f);
            _xpFill.fillAmount = _xpBar.Fill;
            _xpLabel.text = $"Lv {_model.Xp.Level}";
        }

        private void CheckLevelUp()
        {
            int level = _model.Xp.Level;
            if (level == _lastLevel) return;

            if (level > _lastLevel)
            {
                Vector3 at = _player != null ? _player.transform.position : Vector3.zero;

                // The level-up used to be a number and nothing else. Say what was actually won,
                // and tell gameplay so Max genuinely gets stronger (YT-67).
                _floating?.Spawn(at + Vector3.up * 2.6f,
                    $"LEVEL {level}", XpColor, crit: true, life: 1.3f, fontSize: 38f);
                _floating?.Spawn(at + Vector3.up * 3.4f,
                    PowerRamp.BoostLabel(level), BoostColor, crit: true, life: 1.5f, fontSize: 32f);

                HudSignals.EmitLevelUp(level, at);
            }
            _lastLevel = level;
        }

        private void UpdateAbilitySlots(float dt)
        {
            // Dash (slot 0) reflects the real PlayerController cooldown.
            float dashFill = _player != null ? _player.DashCooldownNormalized : 0f;
            bool dashReady = _player == null || _player.DashReady;
            SetSlot(0, dashFill, dashReady);
            SetSlot(1, _model.Bomb.RadialFill, _model.Bomb.Ready);
            SetSlot(2, _model.UltimateRadialFill, _model.UltimateReady);
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
            else if (_blaster != null && _blaster.Energy != null && _blaster.Energy.Normalized <= 0.001f)
            {
                msg = "ENERGY OUT"; col = XpColor;
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

        private void BuildStatusStrip()
        {
            // Just the level pip now (YT-121). Max's life and water moved to a floating stack over
            // his head, so the top-of-screen HP and Energy bars that used to flank this are gone —
            // they were a redundant second copy of what now sits above Max. XP is neither life nor
            // water, so it stays, centred where it always was.
            var strip = NewRect("Status Strip", Root);
            Anchor(strip, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            strip.sizeDelta = new Vector2(RefW * 0.40f, 90f);
            strip.anchoredPosition = new Vector2(0f, -28f);

            _xpFill = BuildBar(strip, "XP", XpColor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -2f), new Vector2(RefW * 0.06f, 14f), out _xpLabel);
        }

        private Image BuildBar(RectTransform parent, string name, Color fill, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 pos, Vector2 size, out Text number)
        {
            var holder = NewRect($"{name} Bar", parent);
            Anchor(holder, anchorMin, anchorMax, new Vector2(anchorMin.x, 0.5f));
            holder.sizeDelta = size;
            holder.anchoredPosition = pos;

            var bg = AddImage(holder, HudTextures.RoundedBox(24, 0.5f), PanelColor, "BG");
            Stretch(bg.rectTransform);
            bg.type = Image.Type.Sliced;

            var fillImg = AddImage(holder, HudTextures.RoundedBox(24, 0.5f), fill, "Fill");
            Stretch(fillImg.rectTransform, -3f); // inset inside the bg for a bordered look
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImg.fillAmount = 1f;

            number = AddText(holder, 20f, Color.white, TextAnchor.MiddleCenter);
            Anchor(number.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f));
            number.rectTransform.sizeDelta = new Vector2(size.x, 24f);
            number.rectTransform.anchoredPosition = new Vector2(0f, 20f); // number floats above the bar
            return fillImg;
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
            }
        }

        /// <summary>
        /// The top-right slots. Dash used to be the first of these (YT-116) — it now has its own
        /// button down by the thumb, because top-right is the one corner a thumb holding a phone
        /// cannot reach, and dash is the only one of the three that does anything.
        ///
        /// Bomb and Ultimate stay here, and stay honest: neither is implemented, so both are drawn
        /// dimmed with a LOCKED caption rather than glowing as though they were a button you were
        /// failing to find. Slot indices are unchanged (0 dash, 1 bomb, 2 ultimate) so the cooldown
        /// driver does not have to care where a slot is drawn.
        /// </summary>
        private void BuildAbilitySlots()
        {
            string[] glyphs = { "B", "U" };      // Bomb, Ultimate — index 1 and 2
            var col = NewRect("Ability Slots", Root);
            Anchor(col, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            col.anchoredPosition = new Vector2(-24f, -24f);
            col.sizeDelta = new Vector2(72f, 160f);
            for (int g = 0; g < glyphs.Length; g++)
            {
                int i = g + 1;                   // slot 0 is the dash button, built separately
                var slot = AddImage(col, HudTextures.RoundedBox(72, 0.24f), PanelColor, $"Slot {glyphs[g]}");
                Anchor(slot.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
                slot.rectTransform.sizeDelta = new Vector2(72f, 72f);
                slot.rectTransform.anchoredPosition = new Vector2(0f, -g * 80f);
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
                letter.text = glyphs[g];

                var locked = AddText(slot.rectTransform, 15f,
                                     new Color(BoneWhite.r, BoneWhite.g, BoneWhite.b, 0.5f),
                                     TextAnchor.LowerCenter);
                Stretch(locked.rectTransform);
                locked.text = "LOCKED";

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
        /// The dash button (YT-116) — a round action button up and to the left of the aim stick,
        /// where the right thumb already is.
        ///
        /// It was in the top-right slot column, which is the far corner of a phone held in two
        /// hands: reaching it means letting go of aim. This is the Brawl-Stars placement — the
        /// action button sits inside the arc the aiming thumb already sweeps.
        ///
        /// The position is picked to clear the aim stick's TOUCH pad, not just its rings. That pad
        /// is 30 px larger than the visible stick on every side (see AddOnScreenStick), so a button
        /// tucked against the artwork would have stolen drags meant for aiming.
        /// </summary>
        private void BuildDashButton()
        {
            var root = NewRect("Dash Button", Root);
            Anchor(root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f));
            root.anchoredPosition = new Vector2(-DashButtonInset, DashButtonRise);
            root.sizeDelta = new Vector2(DashButtonSize, DashButtonSize);
            _dashButtonRoot = root;

            // Ready glow first, behind everything. A RING, not a filled disc (YT-124): when dash is
            // ready the pulse rides the outline as a gold rim, leaving the interior see-through like
            // the joysticks — a filled glow was tinting the whole button gold and reading as solid.
            var glow = AddImage(root, HudTextures.TechRings(160, 3), Color.clear, "Glow");
            Stretch(glow.rectTransform, 4f);
            glow.raycastTarget = false;
            _slotGlow[0] = glow;

            // The button now speaks the joysticks' language (YT-124): a thin TechRings outline with a
            // see-through interior, instead of a solid disc. That drops the opacity to match the
            // move/aim controls and thins the outer outline in one move — the colour (dash gold) and
            // position are unchanged, which is what Lee wanted kept. No solid face any more; the
            // cooldown radial darkens the interior while charging and clears to transparent when ready.
            var ring = AddImage(root, HudTextures.TechRings(160, 3), DashColor, "Ring");
            Stretch(ring.rectTransform);
            ring.raycastTarget = false;

            var label = AddText(root, 26f, DashColor, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.text = "DASH";

            // Cooldown wipe, identical treatment to the slots so the two read as one language.
            var radial = AddImage(root, HudTextures.Disc(160), new Color(0f, 0f, 0f, 0.5f), "Radial");
            Stretch(radial.rectTransform, -6f);
            radial.type = Image.Type.Filled;
            radial.fillMethod = Image.FillMethod.Radial360;
            radial.fillOrigin = (int)Image.Origin360.Top;
            radial.fillClockwise = true;
            radial.fillAmount = 0f;
            radial.raycastTarget = false;
            _slotRadial[0] = radial;
        }

        // Far enough from the corner to clear the aim stick's touch pad (the stick is 200 wide at
        // (-150, 150) and its pad adds 30 on each side, so it owns out to x = -310).
        private const float DashButtonSize = 140f;
        private const float DashButtonInset = 400f;
        private const float DashButtonRise = 330f;

        // The dash gold — the colour Lee likes and asked to keep (YT-124). Same value the ready glow
        // pulses in, so the ring and its pulse are one hue.
        private static readonly Color DashColor = ReadyGlow;

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
        /// only visualisers; here we lay a transparent <see cref="OnScreenStick"/> pad over each and
        /// an <see cref="OnScreenButton"/> over the Dash slot, all driving the SAME synthetic-gamepad
        /// controls <see cref="PlayerController"/> already binds (<c>&lt;Gamepad&gt;/leftStick</c>,
        /// <c>/rightStick</c>, <c>/buttonSouth</c>). So a finger feeds the exact input path a real
        /// controller would, with zero change to gameplay code, and — because each stick captures its
        /// own pointer — move and aim work as simultaneous multi-touch. On-device feel (drag range,
        /// tap vs drag) is tuned in Lee's device pass.
        /// </summary>
        private void BuildTouchControls()
        {
            EnsureEventSystem();

            if (_moveJoystickRoot != null)
                AddOnScreenStick(_moveJoystickRoot, "<Gamepad>/leftStick", "Move Touch");
            if (_aimJoystickRoot != null)
                AddOnScreenStick(_aimJoystickRoot, "<Gamepad>/rightStick", "Aim Touch");
            if (_dashButtonRoot != null)
                AddOnScreenButton(_dashButtonRoot, "<Gamepad>/buttonSouth", "Dash Touch");
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
        }

        private static void AddOnScreenButton(RectTransform slotRoot, string controlPath, string name)
        {
            var pad = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(OnScreenButton));
            var rect = (RectTransform)pad.transform;
            rect.SetParent(slotRoot, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-8f, -8f); rect.offsetMax = new Vector2(8f, 8f);

            var img = pad.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = true;

            pad.GetComponent<OnScreenButton>().controlPath = controlPath;
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
            var a = _model.Arena;
            _arenaLabel.text = $"SUB-ZONE {a.SubZonesCleared}/{a.SubZonesTotal}     FACTORIES {a.FactoriesDestroyed}/{a.FactoriesTotal}";
        }

        /// <summary>The in-run map (YT-72) — its own component, so the minimap can reuse the same
        /// renderer at a different scale rather than the HUD growing a second copy of it.</summary>
        public MapScreen Map { get; private set; }

        /// <summary>The always-on minimap (YT-73) — the same renderer, small and see-through.</summary>
        public Minimap Minimap { get; private set; }

        private void BuildMap()
        {
            var go = new GameObject("Map Screen");
            go.transform.SetParent(FullRoot, false);
            Map = go.AddComponent<MapScreen>();
            Map.Build(FullRoot, RefW, RefH);

            // Below the utility icon column (P / ? / S), which owns the top-left down to y = -208.
            var mini = new GameObject("Minimap", typeof(RectTransform));
            Minimap = mini.AddComponent<Minimap>();
            Minimap.Build(Root, Map, new Vector2(24f, -228f));
        }

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

        /// <summary>The banked power-cell counter (YT-131): a small pill under the level pip with a
        /// cyan cell icon and a running total. Display-only currency for now — it just has to be
        /// visibly accumulating as you clear the tough robots.</summary>
        private void BuildPowerCellCounter()
        {
            var root = NewRect("Power Cells", Root);
            Anchor(root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            root.sizeDelta = new Vector2(150f, 44f);
            root.anchoredPosition = new Vector2(0f, -104f); // just below the centred level pip

            var bg = AddImage(root, HudTextures.RoundedBox(44, 0.5f), PanelColor, "BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced; bg.raycastTarget = false;

            // A purpose-built battery cell (YT-134) — a disc read as "a thing", not "a power cell".
            // The sprite bakes its own cyan/dark, so tint white to render it as authored.
            _cellIcon = AddImage(root, WeaponHudIcons.PowerCell(64), Color.white, "Cell Icon");
            Anchor(_cellIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            _cellIcon.rectTransform.sizeDelta = new Vector2(30f, 30f);
            _cellIcon.rectTransform.anchoredPosition = new Vector2(16f, 0f);
            _cellIcon.raycastTarget = false;

            _cellCount = AddText(root, 24f, BoneWhite, TextAnchor.MiddleLeft);
            Anchor(_cellCount.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f));
            _cellCount.rectTransform.offsetMin = new Vector2(42f, 0f);
            _cellCount.rectTransform.offsetMax = new Vector2(-10f, 0f);
            _cellCount.fontStyle = FontStyle.Bold;
            _cellCount.text = MaxWorlds.Pickups.PickupWallet.PowerCells.ToString();
        }

        /// <summary>The flashing "install available" chip (YT-131): a gold part badge pinned to the
        /// right edge that appears and pulses the moment a part is picked up. It is the tell that
        /// drives YT-132's upgrade flow (tap it → pause → fit the part); here it just flashes.</summary>
        private void BuildPartAlert()
        {
            _partAlertRoot = NewRect("Part Alert", Root);
            Anchor(_partAlertRoot, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            _partAlertRoot.sizeDelta = new Vector2(96f, 96f);
            _partAlertRoot.anchoredPosition = new Vector2(-28f, 120f); // right edge, above the aim stick

            _partAlertBg = AddImage(_partAlertRoot, HudTextures.RoundedBox(72, 0.3f), PartColor, "Chip");
            Stretch(_partAlertBg.rectTransform); _partAlertBg.type = Image.Type.Sliced;
            _partAlertBg.raycastTarget = true;   // it's a button now (YT-132): tap it to open the upgrade screen

            // Tapping the chip opens the paused upgrade screen for the part you picked up (YT-132).
            var chipButton = _partAlertBg.gameObject.AddComponent<Button>();
            chipButton.transition = Selectable.Transition.None;   // the flash pulse drives its colour
            chipButton.onClick.AddListener(OpenUpgrade);

            _partAlertLabel = AddText(_partAlertRoot, 24f, BoneWhite, TextAnchor.MiddleCenter);
            Stretch(_partAlertLabel.rectTransform);
            _partAlertLabel.fontStyle = FontStyle.Bold;
            _partAlertLabel.text = "PART";

            _partAlertRoot.gameObject.SetActive(MaxWorlds.Pickups.PickupWallet.PartsPending > 0);
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
