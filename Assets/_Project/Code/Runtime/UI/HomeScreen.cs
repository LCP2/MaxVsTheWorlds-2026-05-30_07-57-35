using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Core;
using MaxWorlds.Dev;
using MaxWorlds.Intro;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Save;
using MaxWorlds.Upgrades;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.UI
{
    /// <summary>
    /// The game's Home screen (YT-151; profiles per YT-218): the first thing up on boot, three
    /// player-profile slots wide. Every slot offers PLAY, which always drops the player into a fresh
    /// run and clears any captured run on that slot (<see cref="SaveSystem.ClearCheckpoint"/>). Since
    /// MV-524 a slot that carries a mid-run checkpoint (<see cref="SaveSlotData.HasRunInProgress"/> —
    /// an area-entry snapshot, not a world snapshot) ALSO offers RESUME, which restores it and drops
    /// the player back at that area's entry (<see cref="OnResume"/>,
    /// <see cref="MaxWorlds.Arena.WorldRunner.ResumeCheckpoint"/>). Picking a slot hands off to
    /// <see cref="SaveSystem.ActiveSlot"/>, which is also what stops this screen reopening on a
    /// Replay-triggered scene reload — once a slot is live, <see cref="MaxWorlds.Core.SceneInstallers"/>
    /// re-running <see cref="Install"/> after a death/Replay finds the slot already set and leaves the
    /// run alone.
    ///
    /// Code-driven overlay, same idiom as <see cref="ResultScreen"/>/<see cref="UpgradeScreen"/>: its
    /// own canvas above the HUD, built in code, paused via <see cref="Time.timeScale"/> = 0 while a
    /// choice is pending. Skips itself entirely (silently drops into slot 0) when
    /// <see cref="PressKitDirector.Armed"/> or <see cref="MaxWorlds.Dev.UiScreensDirector.Armed"/> — a
    /// filming or fixed-state UI capture run has nothing to click the modal with, and the captured
    /// shots must not open on a frozen pick-a-slot screen (YT-97; MV-441 — this screen's own
    /// sortingOrder=220 canvas was sitting on top of every ui-screens capture uncaught).
    ///
    /// PLAY also triggers <see cref="IntroCinematic"/> (YT-155/156) on a derived true-first-launch
    /// (MV-550): <see cref="ShouldPlayIntroOnFirstLaunch"/> is true only when every save slot is empty
    /// and no capture director is armed. The gate is derived from <see cref="SaveSystem"/>'s live slot
    /// state every call, never persisted — see that method's doc.
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

            if (PressKitDirector.Armed() || MaxWorlds.Dev.UiScreensDirector.Armed() ||
                MaxWorlds.Dev.PerfCaptureDirector.Armed())
            {
                // Filming (press-kit), a fixed-state UI capture (ui-screens), or an unattended
                // frame-time sample (MV-494) all have nothing to click the modal with — hand off to
                // slot 0 straight away, without pausing (Open() below sets Time.timeScale = 0, which
                // would freeze the very simulation a perf capture exists to measure) or showing
                // anything, the same lever PressKitDirector already used (YT-97; MV-441).
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
            if (_open)
            {
                Time.timeScale = _prevTimeScale;
                ModalFrameRateGate.Exit();
            }
        }

        private void Open()
        {
            if (_open) return;
            _open = true;
            EnsureEventSystem();
            Build();
            _prevTimeScale = TimeScaleCapture.ClampForCapture(Time.timeScale);
            Time.timeScale = 0f;
            ModalFrameRateGate.Enter();   // MV-574: idle the frame rate while this modal is up
        }

        /// <param name="cinematicStarted">MV-550: true when this pick just triggered
        /// <see cref="IntroCinematic"/> — in that case control is NOT yet with the player (the
        /// cinematic holds it for ~25s), so the "controllable" mark belongs to
        /// <see cref="IntroCinematic"/>'s own handoff, not here.</param>
        private void Close(bool cinematicStarted = false)
        {
            _open = false;
            Time.timeScale = _prevTimeScale;
            ModalFrameRateGate.Exit();
            if (_maxStage != null) _maxStage.Hide();
            if (_root != null) Destroy(_root);
            _confirmRoot = null;   // was a child of _root; already gone
            // YT-216 — a slot was just picked; on the no-cinematic path Max is live and moving right now.
            if (!cinematicStarted) BootTiming.Mark("controllable");

            // MV-503: the CharacterController's state at this exact handoff, unconditionally — whether
            // or not "rotates but never translates" reproduces this run.
            var player = FindFirstObjectByType<PlayerController>();
            if (player != null) player.LogHandoffDiagnostic();
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
        /// fresh run. MV-524: PLAY always clears any checkpoint the slot was carrying
        /// (<see cref="SaveSystem.ClearCheckpoint"/>) — RESUME is the only path that restores one (see
        /// <see cref="OnResume"/>). Returns true if this pick started <see cref="IntroCinematic"/>
        /// (MV-550) — the caller uses that to decide who marks <c>BootTiming</c>'s "controllable".</summary>
        private bool StartSlot(int slot, bool playIntro)
        {
            SaveSystem.ActiveSlot = slot;
            SaveSystem.EnsureProfile(slot);
            SaveSystem.ClearCheckpoint(slot);

            UpgradeState.Reset();
            HydroBurst.Reset();   // a fresh run must not inherit a burst/cooldown in progress (YT-215)
            PickupWallet.Reset();
            WeaponSystemState.Reset();   // fresh RCDA tracks at L1, no abilities owned (WV-230)
            // MV-427: was never wired anywhere — a fresh pick could inherit a stale banked ability
            // credit left over from a previous slot's run.
            AbilityCreditBank.Reset();
            // MV-425: same stale-state risk for a banked Morphing Module draft left over from a
            // previous slot's run.
            MaxWorlds.Weapons.PendingMorphingModule.Reset();
            // MV-427: a fresh run starts with every area's part ungranted and no deaths taken —
            // otherwise a profile that died in Area 3 last run would find Area 3's part permanently
            // ungrantable on its next, unrelated run.
            MaxWorlds.Arena.DeathRunState.Reset();
            // force: true — MV-550's derived first-launch gate (or a manual IntroCinematic.Enabled
            // override, see ShouldPlayIntroOnFirstLaunch's caller) decides playIntro; TryPlay must not
            // re-apply its own Enabled check on top of that decision.
            return playIntro && IntroCinematic.TryPlay(force: true);
        }

        /// <summary>MV-550's derived first-launch gate: true only when every save slot is empty
        /// (never played on this device) AND no capture director is armed — a filming or fixed-state
        /// run must never wait on the ~25s sequence. Reads <see cref="SaveSystem"/>'s live slot state
        /// on every call; nothing here is persisted or cached, so a wiped device reads as first-launch
        /// again — deliberate, per MV-550 (no <c>SeenIntro</c> flag).</summary>
        public static bool ShouldPlayIntroOnFirstLaunch()
        {
            if (PressKitDirector.Armed() || MaxWorlds.Dev.UiScreensDirector.Armed() ||
                MaxWorlds.Dev.PerfCaptureDirector.Armed())
                return false;

            for (int i = 0; i < SaveSystem.SlotCount; i++)
                if (SaveSystem.Load(i).HasData) return false;
            return true;
        }

        private void OnPlay(int slot)
        {
            // Enabled stays a manual override on top of the derived gate (AC3) — a dev/test can still
            // force the cinematic on a device that already has save data.
            bool playIntro = IntroCinematic.Enabled || ShouldPlayIntroOnFirstLaunch();
            bool introStarted = StartSlot(slot, playIntro);
            Close(introStarted);
        }

        /// <summary>RESUME tapped on a slot carrying a checkpoint (MV-524 part 3) — restores it and
        /// drops the player back at that area's entry, rather than the fresh run <see cref="OnPlay"/>
        /// always starts. Never runs <see cref="IntroCinematic"/> (an in-progress profile is by
        /// definition not a first launch).</summary>
        private void OnResume(int slot)
        {
            SaveSlotData data = SaveSystem.Load(slot);
            if (!data.HasRunInProgress) return;   // guards a stray tap on what should be non-interactable

            SaveSystem.ActiveSlot = slot;

            // The same transient-state wipe StartSlot does for a fresh PLAY — arena-scoped state a
            // checkpoint never captures, plus a guard against DeathRunState's granted-part flags or
            // UpgradeState's installed set leaking in from a DIFFERENT slot played earlier this same
            // process (e.g. Quit to menu, then pick another slot). Deliberately NOT PickupWallet/
            // WeaponSystemState: those would just be reset here and immediately overwritten below, and
            // both wipe RigState, which RestoreCheckpoint is about to repopulate from the checkpoint.
            UpgradeState.Reset();
            HydroBurst.Reset();
            AbilityCreditBank.Reset();
            MaxWorlds.Weapons.PendingMorphingModule.Reset();
            MaxWorlds.Arena.DeathRunState.Reset();

            SaveSystem.RestoreCheckpoint(slot);
            // RestoreCheckpoint sets RigState directly, which never touches WeaponSystemState's own
            // acquisition-order list — without this every restored ability reads as unacquired on the
            // Weapons screen despite working correctly in combat (RigState is what AbilityLevel/
            // IsAcquired actually read).
            WeaponSystemState.RebuildAcquiredFromRigState();

            int areaIndex = data.CheckpointAreaIndex;
            Close();

            var runner = FindFirstObjectByType<MaxWorlds.Arena.WorldRunner>();
            runner?.ResumeCheckpoint(areaIndex);
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
            // MV-524: a slot carrying a checkpoint names which area it's parked in — the ticket's own
            // fallback naming (no WorldConfig loaded here to resolve the area's authored display name).
            string summary = data.HasData ? Summarise(data) : "Empty";
            status.text = data.HasRunInProgress
                ? $"{summary}\nRun in progress - Area {data.CheckpointAreaIndex}"   // ASCII hyphen, see Summarise
                : summary;

            if (data.HasRunInProgress)
            {
                // MV-524: RESUME restores the checkpoint; PLAY still starts fresh and clears it (AC4) —
                // both stay on-screen so the fresh-start option is never hidden behind the resume one.
                var resumeBtn = AddButton(row.rectTransform, "RESUME", MaxOrange, true, () => OnResume(slot));
                Anchor(resumeBtn, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                resumeBtn.sizeDelta = new Vector2(260f, 56f);
                resumeBtn.anchoredPosition = new Vector2(-110f, 20f);

                var playBtn = AddButton(row.rectTransform, "PLAY", CardRim, true, () => OnPlay(slot));
                Anchor(playBtn, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                playBtn.sizeDelta = new Vector2(260f, 40f);
                playBtn.anchoredPosition = new Vector2(-110f, -46f);
            }
            else
            {
                var playBtn = AddButton(row.rectTransform, "PLAY", MaxOrange, true, () => OnPlay(slot));
                Anchor(playBtn, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                playBtn.sizeDelta = new Vector2(280f, 64f);
                playBtn.anchoredPosition = new Vector2(-110f, 0f);
            }

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
            // MV-524 AC5: RESET is a full profile wipe — identity, personal best, AND any run in
            // progress. Named explicitly when there's a run to lose, so it never reads as "just clears
            // the paused run" the way RESUME/PLAY's split might otherwise imply.
            msg.text = data.HasRunInProgress
                ? $"Reset {name}?\nThis erases all progress on this slot, including your run in progress."
                : $"Reset {name}?\nThis erases all progress on this slot.";

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

        /// <summary>"NAME — best: N deaths" (MV-427; supersedes YT-218's peak-Domination-% example,
        /// which stopped discriminating once a death no longer ends the run) — nothing else survives
        /// between runs, so the personal best is the whole story a slot card has to tell.</summary>
        private static string Summarise(SaveSlotData data)
        {
            if (data.BestDeathsToVictory < 0) return $"{data.DisplayName} - no finished run yet";   // ASCII hyphen: LegacyRuntime.ttf has no em-dash coverage (MV-600)
            string deaths = data.BestDeathsToVictory == 1 ? "1 death" : $"{data.BestDeathsToVictory} deaths";
            return $"{data.DisplayName} - best: {deaths}";   // ASCII hyphen: LegacyRuntime.ttf has no em-dash coverage (MV-600)
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
