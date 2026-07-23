using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Pickups;
using MaxWorlds.Upgrades;
using MaxWorlds.VFX;

namespace MaxWorlds.UI
{
    /// <summary>
    /// The upgrade moment (YT-132): tap the flashing part chip → the game pauses → a screen shows Max
    /// with his hose and reveals the part you picked up, animating it onto the weapon → tap to resume.
    ///
    /// Dropped-part-decides (confirmed with Lee): there is no menu, you get the component you collected.
    /// The five concrete parts and the effect each applies are YT-133; this builds the flow and drives
    /// it with a generic placeholder part, so the screen, the pause, and the reveal-and-fit animation
    /// are all real and just need their five payloads slotted in.
    ///
    /// Self-installing UI director, same idiom as <see cref="SettingsPanel"/>: its own overlay canvas
    /// above the HUD, an EventSystem if the scene has none, hidden until opened. It pauses with
    /// <see cref="Time.timeScale"/> = 0 and therefore animates on <see cref="Time.unscaledDeltaTime"/>,
    /// which keeps running while the world is frozen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeScreen : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<UpgradeScreen>() != null) return;
            new GameObject("UpgradeScreen").AddComponent<UpgradeScreen>();
        }

        private const float RefW = 1920f, RefH = 1080f;

        private static readonly Color Scrim = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.10f, 0.98f);
        private static readonly Color CardColor = new Color(0.12f, 0.14f, 0.17f, 1f);
        private static readonly Color TextColor = Color.white;
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);

        // Animation timing (seconds, unscaled). reveal → fit → settle, then the continue prompt.
        private const float RevealTime = 0.45f;
        private const float FitTime = 0.45f;
        private const float SettleTime = 0.35f;

        private Canvas _canvas;
        private RectTransform _safeRoot;
        private GameObject _root;

        private Text _partLabel;
        private Text _title;
        private Text _continueHint;
        private UpgradeWeaponStage _stage;    // the live 3D weapon render (YT-140)
        private Image _weaponGlow;            // rim flash behind the weapon as the new part seats
        private Sprite _maxSprite;            // the art-bible portrait, on the left

        private bool _open;
        private float _prevTimeScale = 1f;
        private float _t;                     // unscaled time since Open
        private UpgradePart _part;

        /// <summary>Is the upgrade screen currently up (and the game paused)?</summary>
        public bool IsOpen => _open;

        /// <summary>The part currently being installed (valid only while open).</summary>
        public UpgradePart Part => _part;

        private void Start() => Build();

        private void OnDestroy()
        {
            // Never leave the world frozen if we're torn down mid-open (a scene swap, a test).
            if (_open) Time.timeScale = _prevTimeScale;
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        /// <summary>Open the screen for <paramref name="part"/>, pausing the game. Ignored if already
        /// open (you install one part at a time).</summary>
        public void Open(UpgradePart part)
        {
            if (_open) return;
            if (_canvas == null) Build();

            _part = part.IsValid ? part : UpgradePart.Generic;
            _open = true;
            _t = 0f;
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;   // freeze the fight; we animate on unscaled time

            // "HOSE UPGRADE" / "MOVEMENT UPGRADE" / "DETACH UPGRADE" — labels which of the three
            // families (YT-166) this reveal belongs to, so the system reads at a glance instead of
            // every part looking like an undifferentiated drop.
            _title.text = UpgradeCatalog.FamilyLabel(UpgradeCatalog.FamilyFor(_part.Kind)) + " UPGRADE";
            _partLabel.text = _part.Name;
            _partLabel.color = _part.Accent;
            _continueHint.text = "TAP TO CONTINUE";

            _root.SetActive(true);
            if (_stage != null) _stage.Show(_part.Kind);   // assemble the weapon + stage the new part
            ApplyAnim(0f);
        }

        /// <summary>Finish the upgrade: apply the part's effect (YT-133 — the weapon/player re-fit off
        /// <see cref="UpgradeState"/>), take it off the pending queue, resume, hide.</summary>
        public void Continue()
        {
            if (!_open) return;
            _open = false;
            Time.timeScale = _prevTimeScale;
            if (_stage != null) _stage.Hide();  // stop the live weapon render (YT-140)
            UpgradeState.Install(_part.Kind);   // stack the effect; the weapon/player read it live
            CommitToLiveWeapon();               // and re-fit the live weapon on the spot (YT-141)
            PickupWallet.SpendPart();
            _root.SetActive(false);
        }

        /// <summary>
        /// Push the new loadout onto the live weapon the instant a part is confirmed (YT-141), the way
        /// the Hydro tether already pulls its state every frame — so a confirmed part measurably changes
        /// the weapon NOW, not only if a change-event happened to be wired to the right instance.
        ///
        /// Cone, reach and move speed are read at their point of use, so they are already live; this
        /// re-fits the things that are BUILT once and would otherwise keep their old shape: the aim
        /// reticle, the stream, and the tank capacity. Belt-and-suspenders with the event subscription.
        /// </summary>
        private static void CommitToLiveWeapon()
        {
            var blaster = FindFirstObjectByType<MaxWorlds.Combat.WaterBlaster>();
            if (blaster != null) blaster.RefreshUpgrades();
        }

        private void Update()
        {
            if (!_open) return;
            _t += Time.unscaledDeltaTime;
            ApplyAnim(_t);
        }

        // ------------------------------------------------------------------ animation

        private void ApplyAnim(float t)
        {
            // The 3D weapon stage glides the new part onto its mount over the fit window (YT-140).
            if (_stage != null) _stage.Tick(t, RevealTime, FitTime);

            // The rim behind the weapon flashes in the part's accent as it locks on, then eases off.
            float settle = Mathf.Clamp01((t - RevealTime - FitTime) / SettleTime);
            float flash = Mathf.Sin(settle * Mathf.PI);                 // 0 → 1 → 0
            if (_weaponGlow != null)
            {
                var g = _part.Accent; g.a = 0.55f * flash;
                _weaponGlow.color = g;
            }

            // The continue hint pulses once the part is on.
            bool done = t >= RevealTime + FitTime;
            _continueHint.gameObject.SetActive(done);
            if (done)
            {
                var c = Dim; c.a = 0.4f + 0.4f * Mathf.Abs(Mathf.Sin(t * 4f));
                _continueHint.color = c;
            }
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            if (_canvas != null) return;
            EnsureEventSystem();

            var go = new GameObject("Upgrade Canvas", typeof(Canvas), typeof(CanvasScaler),
                                    typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 210;   // above the HUD (100) and the Settings panel (200)

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _safeRoot = NewRect("Safe Area", _canvas.transform, Vector2.zero, Vector2.one);
            Stretch(_safeRoot);
            _safeRoot.gameObject.AddComponent<SafeArea>();

            _root = new GameObject("Upgrade Root", typeof(RectTransform));
            var rootRt = (RectTransform)_root.transform;
            rootRt.SetParent(_safeRoot, false);
            Stretch(rootRt);

            // Full-screen scrim that both dims the frozen fight and, as a button, makes a tap anywhere
            // continue — the "tap to resume" of the ticket, without hunting for a small button.
            var scrim = AddImage(rootRt, HudTextures.Solid(), Scrim, "Scrim");
            Stretch(scrim.rectTransform);
            scrim.raycastTarget = true;
            var scrimBtn = scrim.gameObject.AddComponent<Button>();
            scrimBtn.transition = Selectable.Transition.None;
            scrimBtn.onClick.AddListener(Continue);

            _maxSprite = LoadPortrait();
            _stage = UpgradeWeaponStage.Create(transform);

            BuildPanel(rootRt);

            // Input layer (YT-141): a transparent full-screen button ON TOP of everything, so a tap
            // ANYWHERE — including on the panel, where "TAP TO CONTINUE" sits — dismisses and installs.
            // The panel's own graphics were raycast targets stacked above the scrim, so they ate every
            // tap on the panel and only the bare margin outside it registered. This is the input layer,
            // kept separate from the panel's visual composition (the art rebuild, YT-140, owns that).
            var catcher = AddImage(rootRt, HudTextures.Solid(), new Color(0f, 0f, 0f, 0f), "Tap Catcher");
            Stretch(catcher.rectTransform);
            catcher.raycastTarget = true;
            var catchBtn = catcher.gameObject.AddComponent<Button>();
            catchBtn.transition = Selectable.Transition.None;
            catchBtn.onClick.AddListener(Continue);

            _root.SetActive(false);
        }

        /// <summary>The art-bible Max portrait (right half of the reference sheet), from Resources.
        /// Falls back to null — the card just shows empty — rather than throwing if the art is missing.</summary>
        private static Sprite LoadPortrait()
        {
            var tex = Resources.Load<Texture2D>("Art/max_portrait");
            return tex == null ? null
                : Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        private void BuildPanel(RectTransform parent)
        {
            var panel = NewRect("Panel", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            // Taller than the first cut (was 640) — the extra room is what fixes the "TAP TO CONTINUE"
            // prompt sitting right on top of the part name (YT-166): both the title and the continue
            // hint are anchored to the panel's top/bottom EDGES, so growing the panel around its fixed
            // centre pushes them apart from the (unmoved) name in the middle without touching either
            // one's own anchor math.
            panel.sizeDelta = new Vector2(1180f, 740f);
            panel.anchoredPosition = Vector2.zero;
            var bg = AddImage(panel, HudTextures.RoundedBox(48, 0.5f), PanelColor, "BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced; bg.raycastTarget = true;

            _title = AddText(panel, 52, TextColor, TextAnchor.UpperCenter);
            Anchor(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            _title.rectTransform.sizeDelta = new Vector2(900f, 70f);
            _title.rectTransform.anchoredPosition = new Vector2(0f, -34f);
            _title.fontStyle = FontStyle.Bold;

            // A two-column stage: the real Max on the left, his weapon in all its glory on the right.
            var stage = NewRect("Stage", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            stage.sizeDelta = new Vector2(1080f, 460f);
            stage.anchoredPosition = new Vector2(0f, -6f);

            // A thin rim in Max's own hoodie colour, peeking out from behind the card — his hot-orange
            // identity (MaxRig/CharacterSkin) framing the portrait before you even read the art, the
            // same colour as his ground ring and damage numbers (YT-166).
            var portraitGlow = AddImage(stage, HudTextures.RoundedBox(44, 0.5f),
                                        CharacterSkin.BaseColorFor(CharacterRole.Player), "Portrait Glow");
            Anchor(portraitGlow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            portraitGlow.rectTransform.sizeDelta = new Vector2(376f, 436f);
            portraitGlow.rectTransform.anchoredPosition = new Vector2(-300f, 0f);
            portraitGlow.type = Image.Type.Sliced;
            portraitGlow.raycastTarget = false;

            // LEFT — the art-bible portrait, in a rounded card with a hairline frame so it reads as a
            // hero shot rather than a pasted cut-out. Replaces the two orange blobs (YT-140).
            var portraitCard = AddImage(stage, HudTextures.RoundedBox(40, 0.5f), CardColor, "Portrait Card");
            Anchor(portraitCard.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            portraitCard.rectTransform.sizeDelta = new Vector2(360f, 420f);
            portraitCard.rectTransform.anchoredPosition = new Vector2(-300f, 0f);
            portraitCard.type = Image.Type.Sliced;

            var portrait = AddImage(portraitCard.rectTransform, _maxSprite, Color.white, "Max Portrait");
            Stretch(portrait.rectTransform, -12f);   // inset inside the card frame
            portrait.preserveAspect = true;

            var mlabel = AddText(stage, 30, TextColor, TextAnchor.MiddleCenter);
            Anchor(mlabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            mlabel.rectTransform.sizeDelta = new Vector2(360f, 44f);
            mlabel.rectTransform.anchoredPosition = new Vector2(-300f, -240f);
            mlabel.text = "MAX";
            mlabel.fontStyle = FontStyle.Bold;

            // RIGHT — the weapon, rendered live in 3D (the base sprayer + every installed part), with the
            // new one flying on. A rim behind it flashes the part's accent when it seats. Replaces the
            // blue-square part + green-ellipse connector (YT-140).
            _weaponGlow = AddImage(stage, HudTextures.Disc(256), new Color(0, 0, 0, 0), "Weapon Glow");
            Anchor(_weaponGlow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _weaponGlow.rectTransform.sizeDelta = new Vector2(460f, 460f);
            _weaponGlow.rectTransform.anchoredPosition = new Vector2(250f, 30f);
            _weaponGlow.raycastTarget = false;

            var weapon = AddRawImage(stage, _stage != null ? _stage.Texture : null, "Weapon Render");
            Anchor(weapon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            weapon.rectTransform.sizeDelta = new Vector2(440f, 440f);
            weapon.rectTransform.anchoredPosition = new Vector2(250f, 30f);
            weapon.raycastTarget = false;

            _partLabel = AddText(stage, 34, TextColor, TextAnchor.MiddleCenter);
            Anchor(_partLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _partLabel.rectTransform.sizeDelta = new Vector2(560f, 48f);
            _partLabel.rectTransform.anchoredPosition = new Vector2(250f, -240f);
            _partLabel.fontStyle = FontStyle.Bold;

            _continueHint = AddText(panel, 30, Dim, TextAnchor.LowerCenter);
            Anchor(_continueHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            _continueHint.rectTransform.sizeDelta = new Vector2(700f, 44f);
            _continueHint.rectTransform.anchoredPosition = new Vector2(0f, 26f);
        }

        // ------------------------------------------------------------------ helpers

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>();
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            return rt;
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

        private static RawImage AddRawImage(Transform parent, Texture tex, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<RawImage>();
            img.texture = tex;
            return img;
        }

        private static Text AddText(Transform parent, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = HudFont.Get();
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
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
    }
}
