using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Core;
using MaxWorlds.Dev;
using MaxWorlds.Intro;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.Upgrades;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.UI
{
    /// <summary>
    /// The game's Home screen (YT-151; profiles per YT-218): the first thing up on boot, three
    /// player-profile slots wide. Every slot — empty or already played — offers exactly one action,
    /// PLAY: a profile is an identity plus a personal best, not a paused game, so there is no
    /// Continue/resume and picking one always drops the player into a fresh run. Picking a slot hands
    /// off to <see cref="SaveSystem.ActiveSlot"/>, which is also what stops this screen reopening on a
    /// Replay-triggered scene reload — once a slot is live, <see cref="MaxWorlds.Core.SceneInstallers"/>
    /// re-running <see cref="Install"/> after a death/Replay finds the slot already set and leaves the
    /// run alone.
    ///
    /// Code-driven overlay, same idiom as <see cref="ResultScreen"/>/<see cref="UpgradeScreen"/>: its
    /// own canvas above the HUD, built in code, paused via <see cref="Time.timeScale"/> = 0 while a
    /// choice is pending. Skips itself entirely (silently drops into slot 0) when
    /// <see cref="PressKitDirector.Armed"/> — a filming run has nothing to click the modal with, and the
    /// captured shots must not open on a frozen pick-a-slot screen (YT-97).
    ///
    /// Each occupied slot also carries a RESET control (MV-282) gated by a confirm/cancel dialog —
    /// <see cref="SaveSystem.Delete"/> is the whole reset, since <see cref="SaveSlotData"/> is the only
    /// thing that survives between runs today; Bolts/Vault/Workbench state is still session-only and
    /// already gets wiped by <see cref="StartSlot"/> on every play.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeScreen : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<HomeScreen>() != null) return;
            new GameObject("HomeScreen").AddComponent<HomeScreen>();
        }

        private const float RefW = 1920f, RefH = 1080f;
        private const float RowHeight = 190f;
        private const float RowGap = 20f;

        // Near-opaque so this reads as its own dedicated screen, not a thin overlay on the live
        // arena behind it (Lee's 0.3.26 feedback, YT-174).
        private static readonly Color Scrim = new Color(0f, 0f, 0f, 0.97f);
        private static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.10f, 0.98f);
        private static readonly Color CardColor = new Color(0.12f, 0.14f, 0.17f, 1f);
        private static readonly Color CardRim = new Color(0.20f, 0.23f, 0.27f, 1f);
        private static readonly Color Bone = new Color(0.96f, 0.94f, 0.86f);
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);

        // Max's own hoodie colour (CharacterSkin/MaxRig) — the same hot-orange identity treatment
        // as the Upgrade screen's portrait rim (YT-166), so this reads as unmistakably his menu
        // instead of a generic save list (YT-174).
        private static readonly Color MaxOrange = CharacterSkin.BaseColorFor(CharacterRole.Player);

        // Same destructive-red convention as SettingsPanel's "Quit to menu" button.
        private static readonly Color DestructiveRed = new Color(0.85f, 0.20f, 0.20f);

        private GameObject _root;
        private RectTransform _safeRoot;
        private GameObject _confirmRoot;   // the reset confirm/cancel dialog, non-null only while open
        private MaxPortraitStage _maxStage;   // the live low-poly Max render, same crest as UpgradeScreen (YT-189)
        private float _prevTimeScale = 1f;
        private bool _open;

        /// <summary>Is the pick-a-slot modal currently up (and the game paused)? Tests read this.</summary>
        public bool IsOpen => _open;

        private void Start()
        {
            if (SaveSystem.ActiveSlot >= 0)
            {
                // A slot is already live: either a defensive re-add, or exactly the Replay-triggered
                // reload YT-216 targets (sub-3-second AC) — this is the earliest point that reload
                // hands control back, so it's the "controllable" mark for that path.
                BootTiming.Mark("controllable-replay");
                return;
            }

            if (PressKitDirector.Armed())
            {
                // Filming has nothing to click the modal with — hand off to slot 0 straight away,
                // without pausing or showing anything (YT-97).
                StartSlot(0, playIntro: false);
                return;
            }

            BootTiming.Mark("home-shown");   // YT-216 — cold-launch reference point #2
            Open();
        }

        private void OnDestroy()
        {
            // Never leave the world frozen if this is torn down while still open (a scene swap, a
            // test) — same safety net as UpgradeScreen.
            if (_open) Time.timeScale = _prevTimeScale;
        }

        private void Open()
        {
            if (_open) return;
            _open = true;
            EnsureEventSystem();
            Build();
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        private void Close()
        {
            _open = false;
            Time.timeScale = _prevTimeScale;
            if (_maxStage != null) _maxStage.Hide();
            if (_root != null) Destroy(_root);
            _confirmRoot = null;   // was a child of _root; already gone
            BootTiming.Mark("controllable");   // YT-216 — a slot was just picked; Max is live and moving
        }

        private void Update()
        {
            if (_open && _maxStage != null) _maxStage.Tick(Time.unscaledTime);
        }

        /// <summary>Redraw the whole modal in place (MV-282, after a reset) — cheapest way to get a
        /// slot card back to its fresh "Empty" state without hand-rolling an in-place refresh of every
        /// row.</summary>
        private void Rebuild()
        {
            if (_root != null) Destroy(_root);
            _confirmRoot = null;
            Build();
        }

        // ------------------------------------------------------------------ actions

        /// <summary>Picking a profile: create it if this is the first time (YT-218 — its identity
        /// and personal best, seeded once, never reset by a later play), then drop the player into a
        /// fresh run. There is no mid-run state to restore — no resume, no overwrite.</summary>
        private void StartSlot(int slot, bool playIntro)
        {
            SaveSystem.ActiveSlot = slot;
            SaveSystem.EnsureProfile(slot);

            UpgradeState.Reset();
            HydroBurst.Reset();   // a fresh run must not inherit a burst/cooldown in progress (YT-215)
            PickupWallet.Reset();
            WeaponSystemState.Reset();   // fresh RCDA tracks at L1, no abilities owned (WV-230)
            if (playIntro) IntroCinematic.TryPlay();
        }

        private void OnPlay(int slot)
        {
            StartSlot(slot, playIntro: true);
            Close();
        }

        /// <summary>RESET tapped on an occupied slot (MV-282) — asks for confirmation before wiping
        /// anything; a bare tap must never erase progress by itself.</summary>
        private void OnResetTapped(int slot)
        {
            if (_confirmRoot != null) return;   // a confirm is already up; ignore a stray extra tap
            ShowResetConfirm(slot);
        }

        private void ConfirmReset(int slot)
        {
            SaveSystem.Delete(slot);
            Rebuild();
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            var go = new GameObject("Home Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _root = go;

            // The live low-poly Max render (YT-176's stage, reused here per YT-189), not the rejected
            // 2D painted headshot — parented to this component (not the canvas root) so it survives
            // Close()'s Destroy(_root) the same way UpgradeScreen keeps its stage alongside the canvas.
            if (_maxStage == null) _maxStage = MaxPortraitStage.Create(transform);
            _maxStage.Show();
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 220;   // above Upgrade (210) and Result/Settings (200)
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 0.5f;

            var safeRoot = NewRect("Safe Area", go.transform, Vector2.zero, Vector2.one);
            Stretch(safeRoot);
            safeRoot.gameObject.AddComponent<SafeArea>();
            _safeRoot = safeRoot;

            var dim = AddImage(safeRoot, HudTextures.Solid(), Scrim, "Dim");
            Stretch(dim.rectTransform);

            var panel = AddImage(safeRoot, HudTextures.RoundedBox(48, 0.12f), PanelColor, "Panel");
            panel.type = Image.Type.Sliced;
            Center(panel.rectTransform, 1200f, 990f);

            // A crest of Max himself, rimmed in his own hot-orange, so the panel carries his
            // identity from the first frame instead of opening on an anonymous menu (YT-174).
            var badgeRim = AddImage(panel.rectTransform, HudTextures.RoundedBox(28, 0.4f), MaxOrange, "Badge Rim");
            Top(badgeRim.rectTransform, 0f, -14f, 104f, 104f);
            badgeRim.type = Image.Type.Sliced;

            var badgeCard = AddImage(badgeRim.rectTransform, HudTextures.RoundedBox(24, 0.4f), CardColor, "Badge Card");
            Stretch(badgeCard.rectTransform, -8f);
            badgeCard.type = Image.Type.Sliced;

            var badgePortrait = AddRawImage(badgeCard.rectTransform, _maxStage.Texture, "Badge Portrait");
            Stretch(badgePortrait.rectTransform, -6f);

            var title = AddText(panel.rectTransform, 46f, MaxOrange, TextAnchor.MiddleCenter, FontStyle.Bold);
            Top(title.rectTransform, 0f, -134f, 1000f, 64f);
            title.text = "MAX vs THE WORLDS";

            var sub = AddText(panel.rectTransform, 24f, Dim, TextAnchor.MiddleCenter, FontStyle.Normal);
            Top(sub.rectTransform, 0f, -200f, 1000f, 36f);
            sub.text = "SELECT A PLAYER";

            const float top = -258f;
            for (int i = 0; i < SaveSystem.SlotCount; i++)
            {
                BuildSlotRow(panel.rectTransform, i, top - i * (RowHeight + RowGap));
            }
        }

        private void BuildSlotRow(RectTransform panel, int slot, float y)
        {
            // A thin rim behind the card, same framing trick as the Upgrade screen's portrait rim
            // (YT-166), so each slot reads as a designed card rather than a flat generic box.
            var rim = AddImage(panel, HudTextures.RoundedBox(36, 0.32f), CardRim, $"Slot {slot + 1} Rim");
            rim.type = Image.Type.Sliced;
            Top(rim.rectTransform, 0f, y, 1080f, RowHeight);

            var row = AddImage(rim.rectTransform, HudTextures.RoundedBox(32, 0.3f), CardColor, $"Slot {slot + 1}");
            row.type = Image.Type.Sliced;
            Stretch(row.rectTransform, -3f);

            SaveSlotData data = SaveSystem.Load(slot);

            // Occupied slots pick up Max's hot-orange for the slot label — a live save reads as
            // "his" progress at a glance, not just another empty box.
            string displayName = data.HasData ? data.DisplayName : SaveSystem.DefaultDisplayName(slot);
            var label = AddText(row.rectTransform, 30f, data.HasData ? MaxOrange : Bone, TextAnchor.UpperLeft, FontStyle.Bold);
            Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            label.rectTransform.sizeDelta = new Vector2(400f, 40f);
            label.rectTransform.anchoredPosition = new Vector2(34f, -20f);
            label.text = displayName;

            var status = AddText(row.rectTransform, 22f, Dim, TextAnchor.UpperLeft, FontStyle.Normal);
            Anchor(status.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            status.rectTransform.sizeDelta = new Vector2(620f, 100f);
            status.rectTransform.anchoredPosition = new Vector2(34f, -66f);
            status.text = data.HasData ? Summarise(data) : "Empty";

            var playBtn = AddButton(row.rectTransform, "PLAY", MaxOrange, true, () => OnPlay(slot));
            Anchor(playBtn, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            playBtn.sizeDelta = new Vector2(280f, 64f);
            playBtn.anchoredPosition = new Vector2(-110f, 0f);

            // Visible on every slot (AC1) but only tappable on an occupied one — there is nothing to
            // wipe on an already-empty slot (MV-282).
            var resetBtn = AddButton(row.rectTransform, "RESET", data.HasData ? DestructiveRed : CardRim,
                data.HasData, () => OnResetTapped(slot));
            Anchor(resetBtn, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            resetBtn.sizeDelta = new Vector2(140f, 40f);
            resetBtn.anchoredPosition = new Vector2(-18f, -14f);
        }

        /// <summary>The RESET confirm/cancel dialog (MV-282) — a full-screen raycast-blocking scrim
        /// added as the last sibling under <see cref="_safeRoot"/> so it renders on top of the panel
        /// and swallows every click on the slots behind it while it's up.</summary>
        private void ShowResetConfirm(int slot)
        {
            SaveSlotData data = SaveSystem.Load(slot);
            string name = data.HasData ? data.DisplayName : SaveSystem.DefaultDisplayName(slot);

            var scrim = AddImage(_safeRoot, HudTextures.Solid(), new Color(0f, 0f, 0f, 0.75f), "Reset Confirm Scrim");
            Stretch(scrim.rectTransform);
            _confirmRoot = scrim.gameObject;

            var dialog = AddImage(scrim.rectTransform, HudTextures.RoundedBox(32, 0.3f), PanelColor, "Reset Confirm Dialog");
            dialog.type = Image.Type.Sliced;
            Center(dialog.rectTransform, 640f, 280f);

            var msg = AddText(dialog.rectTransform, 26f, Bone, TextAnchor.MiddleCenter, FontStyle.Bold);
            Top(msg.rectTransform, 0f, -30f, 580f, 150f);
            msg.text = $"Reset {name}?\nThis erases all progress on this slot.";

            var cancelBtn = AddButton(dialog.rectTransform, "CANCEL", CardRim, true, HideResetConfirm);
            Anchor(cancelBtn, Vector2.zero, Vector2.zero, Vector2.zero);
            cancelBtn.sizeDelta = new Vector2(270f, 64f);
            cancelBtn.anchoredPosition = new Vector2(30f, 30f);

            var confirmBtn = AddButton(dialog.rectTransform, "CONFIRM", DestructiveRed, true, () => ConfirmReset(slot));
            Anchor(confirmBtn, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));
            confirmBtn.sizeDelta = new Vector2(270f, 64f);
            confirmBtn.anchoredPosition = new Vector2(-30f, 30f);
        }

        private void HideResetConfirm()
        {
            if (_confirmRoot != null) Destroy(_confirmRoot);
            _confirmRoot = null;
        }

        /// <summary>"NAME — best: NN%" (YT-218's own worked example) — nothing else survives between
        /// runs, so the personal best is the whole story a slot card has to tell.</summary>
        private static string Summarise(SaveSlotData data)
        {
            return $"{data.DisplayName} — best: {RunStats.FormatPercent(data.PersonalBestNormalized)}%";
        }

        // ------------------------------------------------------------------ helpers (ResultScreen/UpgradeScreen idiom)

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            var module = es.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            return rt;
        }

        private RectTransform AddButton(RectTransform parent, string label, Color color, bool interactable,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = HudTextures.RoundedBox(32, 0.3f);
            img.type = Image.Type.Sliced;
            img.color = color;
            var btn = go.GetComponent<Button>();
            btn.interactable = interactable;
            if (onClick != null) btn.onClick.AddListener(onClick);

            var t = AddText((RectTransform)go.transform, 22f, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(t.rectTransform);
            t.text = label;
            return (RectTransform)go.transform;
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

        private static Text AddText(Transform parent, float size, Color color, TextAnchor align, FontStyle style)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = HudFont.Get();
            t.fontSize = Mathf.RoundToInt(size);
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
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
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(-padding, -padding);
            r.offsetMax = new Vector2(padding, padding);
        }

        private static void Center(RectTransform r, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = Vector2.zero;
        }

        private static void Top(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = new Vector2(x, y);
        }
    }
}
