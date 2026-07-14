using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Player;
using MaxWorlds.Combat;

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
        private static readonly Color EnergyColor = new Color(0.118f, 0.533f, 0.898f); // #1E88E5 blue
        private static readonly Color TechRingColor = new Color(0.31f, 0.76f, 0.97f);
        private static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.09f, 0.55f);
        private static readonly Color ReadyGlow = new Color(1f, 0.85f, 0.35f);
        private static readonly Color BiomeTint = new Color(0.96f, 0.62f, 0.20f, 0.06f); // warm orange overlay
        private static readonly Color BossColor = new Color(0.85f, 0.12f, 0.12f);
        private static readonly Color BoneWhite = new Color(0.96f, 0.94f, 0.86f);
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
        private FloatingTextLayer _floating;

        // Status strip
        private Image _hpFill, _xpFill, _energyFill;
        private Image _hpGhost;

        // YT-54 presentation state. None of this feeds the model — it only animates what the model
        // already says.
        private static readonly Color HpGhostColor = new Color(1f, 0.72f, 0.62f, 0.55f);
        private readonly BarState _hpBar = new BarState();
        private readonly BarState _energyBar = new BarState();
        private readonly BarState _xpBar = new BarState();
        private readonly DamageNumberAggregator _damageNumbers = new DamageNumberAggregator();
        private readonly System.Collections.Generic.List<DamageNumberAggregator.Entry> _damageBuffer =
            new System.Collections.Generic.List<DamageNumberAggregator.Entry>(16);
        private readonly float[] _slotReadyFlash = new float[3];
        private readonly bool[] _slotWasReady = new bool[3];
        private int _lastLevel = 1;
        private Text _hpLabel, _xpLabel, _energyLabel;

        // Ability slots (0 Dash, 1 Bomb, 2 Ultimate)
        private readonly Image[] _slotRadial = new Image[3];
        private readonly Image[] _slotGlow = new Image[3];

        // Joysticks
        private Image _moveRings, _moveArrow;
        private RectTransform _moveKnob, _moveArrowRect;
        private Image _aimRings, _aimCross;
        private RectTransform _aimKnob;

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
            BuildJoysticks();
            BuildArenaIndicator();
            BuildBossBar();
            BuildWarning();
            BuildFloatingLayer();
            BuildMap();

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
            FlushDamageNumbers();
            CheckLevelUp();
        }

        private void UpdateStatusStrip(float dt)
        {
            if (_health != null)
            {
                _hpBar.Update(_health.Normalized, dt);
                _hpFill.fillAmount = _hpBar.Fill;
                if (_hpGhost != null)
                {
                    // The chip bar: holds where the health WAS, then drains. It shows you how much
                    // you just lost, which a bar that snaps to its new value never tells you.
                    _hpGhost.fillAmount = _hpBar.Ghost;
                    var gc = HpGhostColor; gc.a *= Mathf.Clamp01(_hpBar.Ghost - _hpBar.Fill) > 0.001f ? 1f : 0f;
                    _hpGhost.color = gc;
                }
                _hpFill.color = Color.Lerp(HpColor, Color.white, _hpBar.Flash);
                _hpLabel.text = Mathf.CeilToInt(_health.Current).ToString();
            }
            if (_blaster != null && _blaster.Energy != null)
            {
                // Energy drains and refills constantly while firing, so it gets smoothing but no
                // ghost — a chip bar on a bar that's always moving would just be noise.
                _energyBar.Update(_blaster.Energy.Normalized, dt, fillSpeed: 6f, hold: 0f);
                _energyFill.fillAmount = _energyBar.Fill;
                _energyLabel.text = Mathf.CeilToInt(_blaster.Energy.Current).ToString();
            }
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
        }

        private RectTransform Root => (RectTransform)_canvas.transform;

        private void BuildBiomeTint()
        {
            var img = AddImage(Root, HudTextures.Solid(), BiomeTint, "Biome Tint");
            Stretch(img.rectTransform);
            img.raycastTarget = false;
        }

        private void BuildStatusStrip()
        {
            // Container ~40% width, top-centre.
            var strip = NewRect("Status Strip", Root);
            Anchor(strip, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            strip.sizeDelta = new Vector2(RefW * 0.40f, 90f);
            strip.anchoredPosition = new Vector2(0f, -28f);

            // HP (left, wide) | XP (centre, thin) | Energy (right, wide)
            _hpFill = BuildBar(strip, "HP", HpColor, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(RefW * 0.15f, 26f), out _hpLabel);
            _energyFill = BuildBar(strip, "Energy", EnergyColor, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, 0f), new Vector2(RefW * 0.15f, 26f), out _energyLabel);
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

            // The chip/ghost bar sits BETWEEN the background and the fill, so the fill draws over
            // it and only the difference between them is visible — that difference is the damage
            // you just took. (YT-54; only the HP bar uses it.)
            var ghostImg = AddImage(holder, HudTextures.RoundedBox(24, 0.5f), HpGhostColor, "Ghost");
            Stretch(ghostImg.rectTransform, -3f);
            ghostImg.type = Image.Type.Filled;
            ghostImg.fillMethod = Image.FillMethod.Horizontal;
            ghostImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            ghostImg.fillAmount = 1f;
            ghostImg.raycastTarget = false;
            if (name == "HP") _hpGhost = ghostImg;
            else ghostImg.gameObject.SetActive(false);

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

        private void BuildAbilitySlots()
        {
            string[] glyphs = { "D", "B", "U" }; // Dash, Bomb, Ultimate
            var col = NewRect("Ability Slots", Root);
            Anchor(col, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            col.anchoredPosition = new Vector2(-24f, -24f);
            col.sizeDelta = new Vector2(72f, 240f);
            for (int i = 0; i < 3; i++)
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

                var letter = AddText(slot.rectTransform, 30f, BoneWhite, TextAnchor.MiddleCenter);
                Stretch(letter.rectTransform);
                letter.text = glyphs[i];

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

        private void BuildJoysticks()
        {
            // Bottom-left: movement.
            var moveRoot = NewRect("Move Joystick", Root);
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
            go.transform.SetParent(Root, false);
            Map = go.AddComponent<MapScreen>();
            Map.Build(Root, RefW, RefH);

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

        private void BuildFloatingLayer()
        {
            var go = new GameObject("Floating Text", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(Root, false);
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
