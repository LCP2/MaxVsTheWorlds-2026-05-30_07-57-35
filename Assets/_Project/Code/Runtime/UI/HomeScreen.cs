using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
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
    /// PLAY also triggers <see cref="IntroCinematic"/> (YT-155/156) whenever it starts a run on a slot
    /// holding no data (MV-826, superseding MV-550's whole-device version):
    /// <see cref="ShouldPlayIntroForSlot"/> is true for that slot and no capture director is armed. The
    /// gate is derived from <see cref="SaveSystem"/>'s live slot state every call, never persisted —
    /// see that method's doc.
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

        // MV-960: the stage's own footprint, in ref units, before StageScale's uniform shrink.
        private const float StageW = 1880f;
        private const float StageH = 920f;

        // Fully opaque — Dim now covers the whole screen (not just the safe area), so nothing of the
        // live arena behind Home may show anywhere, including beside the notch (Lee's iPhone
        // observation, MV-960; originally near-opaque per Lee's 0.3.26 feedback, YT-174).
        private static readonly Color Scrim = new Color(0f, 0f, 0f, 1f);
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
        private float _prevTimeScale = 1f;
        private bool _open;

        /// <summary>Is the pick-a-slot modal currently up (and the game paused)? Tests read this.</summary>
        public bool IsOpen => _open;

        /// <summary>MV-960: the stage's own uniform scale — 1.0 whenever the safe area is at least as
        /// big as the stage's authored <see cref="StageW"/>x<see cref="StageH"/> footprint, shrinking
        /// only as far as needed to fit a narrower/shorter safe area (never upscaled past 1). Pure and
        /// static so the layout test can assert it directly, independent of any live Canvas/SafeArea
        /// setup.</summary>
        public static float StageScale(Vector2 safeSizeRef)
        {
            if (safeSizeRef.x <= 0f || safeSizeRef.y <= 0f) return 1f;
            return Mathf.Min(1f, Mathf.Min(safeSizeRef.x / StageW, safeSizeRef.y / StageH));
        }

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

        /// <summary>Public (MV-960's layout test builds the real screen through this, same idiom as
        /// <c>WeaponsScreen.Open</c>) — otherwise only called internally, from <see cref="Start"/>.</summary>
        public void Open()
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
            if (_root != null) Destroy(_root);
            _confirmRoot = null;   // was a child of _root; already gone
            // YT-216 — a slot was just picked; on the no-cinematic path Max is live and moving right now.
            if (!cinematicStarted) BootTiming.Mark("controllable");

            // MV-503: the CharacterController's state at this exact handoff, unconditionally — whether
            // or not "rotates but never translates" reproduces this run.
            var player = FindFirstObjectByType<PlayerController>();
            if (player != null) player.LogHandoffDiagnostic();
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

        /// <summary>The fresh-run wipe both <see cref="StartSlot"/> (PLAY) and
        /// <see cref="StartSlotWorld2"/> (the WORLD 2 dev entry point, MV-726) share verbatim — factored
        /// out so the two can never drift apart (MV-726 AC4). World/primary seeding is the only
        /// difference between the two callers, and stays in each caller, not here.</summary>
        private static void WipeForFreshRun(int slot)
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
            // MV-841: same reasoning for the Result screen's whole-world clock/kill count — a fresh
            // run must not inherit a previous slot's tally left over in this same process.
            MaxWorlds.Arena.RunProgressState.Reset();
        }

        /// <summary>Picking a profile: create it if this is the first time (YT-218 — its identity
        /// and personal best, seeded once, never reset by a later play), then drop the player into a
        /// fresh run. MV-524: PLAY always clears any checkpoint the slot was carrying
        /// (<see cref="SaveSystem.ClearCheckpoint"/>) — RESUME is the only path that restores one (see
        /// <see cref="OnResume"/>). Returns true if this pick started <see cref="IntroCinematic"/>
        /// (MV-550) — the caller uses that to decide who marks <c>BootTiming</c>'s "controllable".</summary>
        private bool StartSlot(int slot, bool playIntro)
        {
            WipeForFreshRun(slot);
            // force: true — ShouldPlayIntroForSlot's derived per-slot gate (or a manual
            // IntroCinematic.Enabled override, see OnPlay) decides playIntro; TryPlay must not re-apply
            // its own Enabled check on top of that decision.
            return playIntro && IntroCinematic.TryPlay(force: true);
        }

        /// <summary>WORLD 2/3 dev entry points (MV-726, generalised MV-736): reaching either world
        /// legitimately means clearing every area of the world(s) before it and collecting the Weapon
        /// Core, which makes them untestable in practice — this is one extra, unflagged entry point
        /// onto the same machinery, not new machinery. Runs the same wipe PLAY does, then seeds a fresh
        /// run of <paramref name="worldIndex"/> directly via the same
        /// <see cref="WeaponSystemState.ApplyWeaponCoreMorph"/> call THE RIG's own morph ceremony
        /// makes — rather than hand-rolling that RigBoard/RigState transition a second time. Public
        /// static and side-effect-pure of any UI so an EditMode test can invoke it directly with no
        /// scene/GameObject involved. Never plays <see cref="IntroCinematic"/> — this is a development
        /// shortcut, not a first launch.
        ///
        /// <paramref name="maxRig"/> selects which populated state the button hands the tester:
        /// <list type="bullet">
        /// <item>false (WORLD 2, MV-737): World 1's own EXIT state — ENERGY/MOVE/SUPPORT additionally
        /// unlocked and maxed to World 1's own level caps (<see cref="UnlockAndMaxCategory"/>, capped via
        /// <see cref="RigBoard.SnapshotMaxLevels"/> — MV-856: never World 2's own higher caps/extra nodes,
        /// which stay for the player to buy in World 2), PRIMARY left on the morph's
        /// owned-but-unupgraded floor, SECONDARY mystery-locked untouched, exactly as a player who
        /// cleared World 1 would arrive.</item>
        /// <item>true (WORLD 3, MV-736): a fully maxed rig — every category unlocked and every node on
        /// <paramref name="worldIndex"/>'s own board raised to its <see cref="RigBoard.MaxLevel"/>
        /// (<see cref="MaxOutRig"/>) — World 3 exists to test World 3 content, not to make the tester
        /// grind a rig first. FORGE itself stays untouched (MV-850: World 1 only until redesigned).</item>
        /// </list>
        /// <see cref="StartSlotWorld2"/> is a one-line call onto this with the original MV-726 shape
        /// (world 1, maxRig: false) so the two paths can never drift (MV-726 AC4).</summary>
        public static void StartSlotWorld(int slot, int worldIndex, bool maxRig)
        {
            WipeForFreshRun(slot);

            SaveSlotData data = SaveSystem.Load(slot);
            data.WorldIndex = worldIndex;
            data.PrimaryKind = worldIndex >= 2 ? WeaponCatalog.PrimaryKind.Undertow : WeaponCatalog.PrimaryKind.Lppe;
            data.WeaponCorePending = false;
            SaveSystem.Save(slot, data);

            WeaponSystemState.ApplyWeaponCoreMorph(worldIndex);

            if (maxRig)
            {
                MaxOutRig();
            }
            else
            {
                // MV-856: cap at World 1's own levels, not whichever board ApplyWeaponCoreMorph just
                // switched RigBoard onto (World 2's, which is higher from MV-840 and adds nodes World 1
                // never had, e.g. e_cmg) — a WORLD 2 start must arrive exactly as a player who cleared
                // World 1 would, with World 2's own additions left for the player to buy in World 2.
                IReadOnlyDictionary<string, int> world1MaxLevels = RigBoard.SnapshotMaxLevels(0);
                UnlockAndMaxCategory("ENERGY", world1MaxLevels);
                UnlockAndMaxCategory("MOVE", world1MaxLevels);
                UnlockAndMaxCategory("SUPPORT", world1MaxLevels);
            }
        }

        /// <summary>WORLD 2 dev entry point (MV-726) — the original single-world shape, kept as a
        /// one-line call onto <see cref="StartSlotWorld"/> so it can never drift from it (MV-726
        /// AC4). Unchanged by MV-736: still world 1, maxRig: false.</summary>
        public static void StartSlotWorld2(int slot) => StartSlotWorld(slot, worldIndex: 1, maxRig: false);

        /// <summary>MV-736: fully maxes THE RIG for whichever board is currently active (already
        /// switched onto <paramref name="worldIndex"/>'s board by the caller's own
        /// <see cref="WeaponSystemState.ApplyWeaponCoreMorph"/> call) — every category unlocked and
        /// every node id <see cref="RigBoard.AllIds"/> lists raised to its own
        /// <see cref="RigBoard.MaxLevel"/>, through <see cref="RigState.RestoreSnapshot"/> (the same
        /// shape a mid-run checkpoint restore already uses, so this needs no parallel state-setting
        /// path), then an attempt at every FORGE fusion through <see cref="RigFusionState.TryForge"/>
        /// (both parent categories are lit by construction once every node is owned). The id/fusion
        /// lists are read from the loaded board each time, never hard-coded, so this keeps working
        /// unmodified if a node or fusion is ever added to the board. Maxing <c>s_rkt</c> here also
        /// clears <see cref="RigState.SecondaryLocked"/> on its own (that flag is just "mystery armed
        /// AND s_rkt owned") — no special case needed.
        ///
        /// MV-850: for World 2+ (every world this method's own caller can reach — see
        /// <see cref="StartSlotWorld"/>'s <c>worldIndex &gt;= 2</c> WORLD 3 button), the
        /// <see cref="RigFusionState.TryForge"/> calls below are a no-op — FORGE is World 1 only until
        /// the four fusions are redesigned for World 2's kit, so a "fully maxed" World 3 rig correctly
        /// leaves FORGE untouched rather than forging fusions the board no longer even shows.</summary>
        private static void MaxOutRig()
        {
            var levels = new Dictionary<string, int>();
            foreach (string id in RigBoard.AllIds)
                levels[id] = RigBoard.MaxLevel(id);

            RigState.RestoreSnapshot(levels, RigBoard.AllCategoryIds);
            WeaponSystemState.RebuildAcquiredFromRigState();

            foreach (RigFusionDef fusion in RigBoard.Fusions)
                RigFusionState.TryForge(fusion.Id);
        }

        /// <summary>MV-737: unlocks <paramref name="category"/> and raises every one of its nodes up to
        /// its own cap in <paramref name="capById"/>, through the same public calls a shed unlock/
        /// Morphing Module draft/part spend makes (<see cref="RigState.UnlockCategory"/>,
        /// <see cref="WeaponSystemState.AcquireById"/>, <see cref="WeaponSystemState.RaiseLevelById"/>)
        /// — no debug back door, and no direct call into the raw <see cref="RigState"/> grant/raise
        /// primitives outside <see cref="WeaponSystemState"/> itself (MV-435: a raw call silently skips
        /// <see cref="WeaponSystemState.Changed"/> and leaves anything gated on it stale). A child node
        /// only becomes reachable once its parent is owned, so this sweeps the category repeatedly until
        /// a pass makes no further progress, which converges in as many passes as the tree is deep
        /// regardless of <see cref="RigBoard.AllIds"/>'s own authored order.
        ///
        /// MV-856: the cap comes from <paramref name="capById"/>, not <see cref="RigBoard.MaxLevel"/> of
        /// whichever board is currently active — the WORLD 2 start must stop at World 1's own levels
        /// even though World 2's board (already switched onto by the caller) allows higher ones. A node
        /// id absent from <paramref name="capById"/> is left untouched at its current (0) level — it
        /// doesn't exist on World 1's board, so it stays for the player to unlock in World 2 itself.
        /// The underlying <see cref="RigBoard.Exists"/>/<see cref="RigBoard.MaxLevel"/> checks
        /// <see cref="WeaponSystemState.AcquireById"/>/<see cref="RaiseLevelById"/> make themselves still
        /// read World 2's board (needed for them to succeed at all on a World-2-only id like
        /// <c>e_cmg</c>'s own eventual player purchase) — this method's own <c>cap</c> check is what
        /// stops the sweep short of that higher ceiling.</summary>
        private static void UnlockAndMaxCategory(string category, IReadOnlyDictionary<string, int> capById)
        {
            RigState.UnlockCategory(category);

            bool progressed = true;
            while (progressed)
            {
                progressed = false;
                foreach (string id in RigBoard.AllIds)
                {
                    if (RigBoard.Category(id) != category) continue;
                    if (!capById.TryGetValue(id, out int cap)) continue;
                    if (RigState.Level(id) >= cap) continue;

                    if (RigState.Level(id) == 0)
                        progressed |= WeaponSystemState.AcquireById(id);
                    else
                        progressed |= WeaponSystemState.RaiseLevelById(id);
                }
            }
        }

        /// <summary>MV-826's derived per-slot gate (supersedes MV-550's whole-device
        /// <c>ShouldPlayIntroOnFirstLaunch</c>): true whenever PLAY is about to start a run on a slot
        /// that holds no data — a never-used slot, or one just RESET — AND no capture director is
        /// armed — a filming or fixed-state run must never wait on the ~14.5s film. Reads
        /// <see cref="SaveSystem"/>'s live slot state on every call; nothing here is persisted or
        /// cached, so a slot reads as intro-eligible again immediately after RESET (Lee's decision —
        /// no <c>SeenIntro</c> flag). Must be evaluated BEFORE <see cref="StartSlot"/>, which creates
        /// the slot's data and would otherwise make every slot read as occupied.</summary>
        public static bool ShouldPlayIntroForSlot(int slot)
        {
            if (PressKitDirector.Armed() || MaxWorlds.Dev.UiScreensDirector.Armed() ||
                MaxWorlds.Dev.PerfCaptureDirector.Armed())
                return false;

            return !SaveSystem.Load(slot).HasData;
        }

        private void OnPlay(int slot)
        {
            // Enabled stays a manual override on top of the derived gate (AC3) — a dev/test can still
            // force the cinematic on a slot that already has save data. Evaluated before StartSlot,
            // which creates the slot's data (see ShouldPlayIntroForSlot's doc).
            bool playIntro = IntroCinematic.Enabled || ShouldPlayIntroForSlot(slot);
            bool introStarted = StartSlot(slot, playIntro);
            Close(introStarted);
        }

        /// <summary>WORLD 2/3 tapped (MV-726, generalised MV-736). Unlike PLAY/RESUME, the arena for
        /// the freshly-seeded world was never built — <see cref="MaxWorlds.Arena.BackyardPath"/> only
        /// resolves <see cref="SaveSlotData.WorldIndex"/> on its own <c>Awake</c>, which already ran
        /// once at boot, same as <see cref="MaxWorlds.UI.RunFlow.StartNextWorld"/> after a Victory — so
        /// this reloads the scene the same way, letting that next <c>Awake</c> pick up the WorldIndex
        /// just saved. <see cref="SaveSystem.ActiveSlot"/> is already set by the time the reload's own
        /// <see cref="Start"/> runs, which is what stops Home reopening on it (same guard the
        /// Replay-triggered reload relies on) — and <see cref="RigState"/>/<see cref="WeaponSystemState"/>
        /// are plain static state, untouched by a scene reload, so the rig <see cref="StartSlotWorld"/>
        /// just seeded survives it exactly as <c>WorldIndex</c> does.</summary>
        private void OnWorldDevStart(int slot, int worldIndex, bool maxRig)
        {
            StartSlotWorld(slot, worldIndex, maxRig);
            Time.timeScale = 1f;
            Scene scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
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

        /// <summary>SETTINGS tapped (MV-960) — opens the existing SettingsPanel above Home rather than
        /// building a second settings surface; Home stays up (still paused) underneath it.</summary>
        private void OnSettingsTapped()
        {
            var settings = FindFirstObjectByType<SettingsPanel>();
            if (settings != null) settings.OpenFromHome();
        }

        // ------------------------------------------------------------------ build

        // MV-960: the dev WORLD shortcuts are NOT part of the design — see BuildDevWorldShortcuts.
        // Removing them later is exactly: delete that method, this const, and the one call below.
        private const bool ShowDevWorldShortcuts = true;

        private const string HeroSpriteResourcePath = "Art/HomeHero";
        private static Sprite s_heroSprite;

        private void Build()
        {
            var go = new GameObject("Home Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _root = go;

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 220;   // above Upgrade (210) and Result/Settings (200)
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 0.5f;

            // MV-960: full-screen and parented to the canvas ROOT, not Safe Area — the old Dim only
            // covered the safe rect, so the live HUD showed through beside the notch.
            var dim = AddImage(go.transform, HudTextures.Solid(), Scrim, "Dim");
            Stretch(dim.rectTransform);

            var safeRoot = NewRect("Safe Area", go.transform, Vector2.zero, Vector2.one);
            Stretch(safeRoot);
            safeRoot.gameObject.AddComponent<SafeArea>();
            _safeRoot = safeRoot;

            // MV-960: replaces the old fixed 1200x990 panel, which overflowed an iPhone landscape
            // safe area — this stage instead shrinks (never grows) to fit whatever safe rect it's
            // given, via StageScale, so it always resolves fully inside it.
            float scale = StageScale(safeRoot.rect.size);
            var stage = AddImage(safeRoot, HudTextures.RoundedBox(80, 0.5f), PanelColor, "Stage");   // radius 40
            stage.type = Image.Type.Sliced;
            Center(stage.rectTransform, StageW, StageH);
            stage.rectTransform.localScale = new Vector3(scale, scale, 1f);

            BuildHeroBand(stage.rectTransform);
            BuildSettingsButton(stage.rectTransform);

            float[] cardX = { 40f, 650f, 1260f };
            const float cardTop = 390f, cardW = 580f, cardH = 440f;
            for (int i = 0; i < SaveSystem.SlotCount && i < cardX.Length; i++)
            {
                BuildCard(stage.rectTransform, i, cardX[i], cardTop, cardW, cardH);
            }

            if (ShowDevWorldShortcuts) BuildDevWorldShortcuts(stage.rectTransform);
        }

        /// <summary>Top of the stage, showing the shipped hero art (MV-960) — only its top two
        /// corners follow the stage's own radius; the bottom edge is a straight cut mid-panel, not a
        /// corner, so it stays square (<see cref="HudTextures.RoundedBoxTopOnly"/>).</summary>
        private void BuildHeroBand(RectTransform stage)
        {
            var frame = new GameObject("Hero", typeof(RectTransform), typeof(Image), typeof(Mask));
            frame.transform.SetParent(stage, false);
            var frameRt = (RectTransform)frame.transform;
            PlaceTL(frameRt, 0f, 0f, StageW, 380f);

            var stencil = frame.GetComponent<Image>();
            stencil.sprite = HudTextures.RoundedBoxTopOnly(80, 0.5f);   // radius 40, matches the stage
            stencil.type = Image.Type.Sliced;
            frame.GetComponent<Mask>().showMaskGraphic = false;   // the stencil itself is invisible

            if (s_heroSprite == null) s_heroSprite = Resources.Load<Sprite>(HeroSpriteResourcePath);
            var art = AddImage(frameRt, s_heroSprite, Color.white, "Hero Art");
            art.type = Image.Type.Simple;
            art.preserveAspect = false;   // stretched to the band, per the ticket
            Stretch(art.rectTransform);
        }

        private void BuildSettingsButton(RectTransform stage)
        {
            const float w = 220f, h = 90f;
            var go = new GameObject("SETTINGS", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(stage, false);
            var rt = (RectTransform)go.transform;
            PlaceTL(rt, StageW - 24f - w, 24f, w, h);

            var img = go.GetComponent<Image>();
            img.sprite = HudTextures.RoundedBox(90, 0.5f);   // radius 45 == h/2, fully rounded
            img.type = Image.Type.Sliced;
            img.color = new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.78f);

            var outline = AddImage(rt, HudTextures.RoundedBoxOutline(90, 0.5f, 3f), new Color(Bone.r, Bone.g, Bone.b, 0.55f), "Outline");
            outline.type = Image.Type.Sliced;
            Stretch(outline.rectTransform);
            outline.raycastTarget = false;

            // Same procedural icon SettingsPanel.BuildGearButton draws, tinted Bone to match this
            // button's own text rather than that panel's Accent green.
            var icon = AddImage(rt, HudTextures.TechRings(96, 3), Bone, "Icon");
            icon.raycastTarget = false;
            Anchor(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            icon.rectTransform.sizeDelta = new Vector2(44f, 44f);
            icon.rectTransform.anchoredPosition = new Vector2(20f, 0f);

            var label = AddText(rt, 28f, Bone, TextAnchor.MiddleLeft, FontStyle.Bold);
            Anchor(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            label.rectTransform.sizeDelta = new Vector2(w - 76f - 16f, 40f);
            label.rectTransform.anchoredPosition = new Vector2(76f, 0f);
            label.text = "SETTINGS";

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(OnSettingsTapped);
        }

        private void BuildCard(RectTransform stage, int slot, float x, float y, float w, float h)
        {
            var card = AddImage(stage, HudTextures.RoundedBox(64, 0.5f), CardColor, $"Card {slot + 1}");   // radius 32
            card.type = Image.Type.Sliced;
            var cardRt = card.rectTransform;
            PlaceTL(cardRt, x, y, w, h);

            var rim = AddImage(cardRt, HudTextures.RoundedBoxOutline(64, 0.5f, 3f), CardRim, "Rim");
            rim.type = Image.Type.Sliced;
            Stretch(rim.rectTransform);
            rim.raycastTarget = false;

            SaveSlotData data = SaveSystem.Load(slot);

            // Occupied slots pick up Max's hot-orange for the slot label — a live save reads as
            // "his" progress at a glance, not just another empty box.
            string displayName = data.HasData ? data.DisplayName : SaveSystem.DefaultDisplayName(slot);
            var label = AddText(cardRt, 40f, data.HasData ? MaxOrange : Bone, TextAnchor.UpperLeft, FontStyle.Bold);
            // Width stops 10px clear of RESET's own left edge (w - 28 - 180), which is also inset 28
            // from the card's left — i.e. w - 246 (see RESET's own PlaceTL below).
            PlaceTL(label.rectTransform, 28f, 30f, w - 246f, 48f);
            label.text = displayName;

            // RESET: top-right, 180 wide, inset 28 right / 20 top. Visible on every slot (AC1) but
            // only tappable on an occupied one — there is nothing to wipe on an already-empty slot
            // (MV-282). MV-960's own AC1 requires RESET to resolve >= 100 ref units tall (the ticket's
            // §5 prose says 180x86, but its Observation section names RESET's OLD 140x40 among the
            // sub-44pt controls this ticket exists to fix, and AC1 explicitly lists RESET in the
            // >=100 floor — 100 tall wins over the 86 in prose; flagged in the fix comment).
            var resetBtn = AddButton(cardRt, "RESET", data.HasData ? DestructiveRed : CardRim,
                data.HasData, () => OnResetTapped(slot), fontSize: 28f);
            PlaceTL(resetBtn, w - 28f - 180f, 20f, 180f, 100f);
            if (!data.HasData)
            {
                var resetText = resetBtn.GetComponentInChildren<Text>();
                if (resetText != null) resetText.color = new Color(1f, 1f, 1f, 0.35f);
            }

            var status = AddText(cardRt, 26f, Dim, TextAnchor.UpperLeft, FontStyle.Normal);
            PlaceTL(status.rectTransform, 28f, 124f, 520f, 68f);
            // MV-524: a slot carrying a checkpoint names which area it's parked in — the ticket's own
            // fallback naming (no WorldConfig loaded here to resolve the area's authored display name).
            // MV-960: no leading "NAME - " — the name is already shown separately above (Summarise).
            string summary = data.HasData ? Summarise(data) : "Empty";
            status.text = data.HasRunInProgress
                ? $"{summary}\nRun in progress - Area {data.CheckpointAreaIndex}"   // ASCII hyphen, see Summarise
                : summary;

            if (data.HasRunInProgress)
            {
                // MV-524: RESUME restores the checkpoint; PLAY still starts fresh and clears it (AC4) —
                // both stay on-screen so the fresh-start option is never hidden behind the resume one.
                var resumeBtn = AddButton(cardRt, "RESUME", MaxOrange, true, () => OnResume(slot), fontSize: 36f);
                PlaceTL(resumeBtn, 28f, 210f, 524f, 100f);

                var playBtn = AddButton(cardRt, "PLAY", CardRim, true, () => OnPlay(slot), fontSize: 32f);
                PlaceTL(playBtn, 28f, 322f, 524f, 100f);
            }
            else
            {
                var playBtn = AddButton(cardRt, "PLAY", MaxOrange, true, () => OnPlay(slot), fontSize: 36f);
                PlaceTL(playBtn, 28f, 210f, 524f, 100f);
            }
        }

        /// <summary>WORLD 2/3 dev entry points (MV-726; MV-736; relocated off the cards by MV-960) —
        /// NOT part of the design, built to be deleted later: every button this builds lives in this
        /// one method, called from the single guarded line in <see cref="Build"/>, so removing them is
        /// exactly delete-this-method + delete-the-const + delete-the-one-call.</summary>
        private void BuildDevWorldShortcuts(RectTransform stage)
        {
            var yellow = new Color(242f / 255f, 196f / 255f, 58f / 255f);
            var fill = Color.Lerp(PanelColor, yellow, 0.08f);
            float[] cardX = { 40f, 650f, 1260f };
            const float y = 846f, h = 66f, w = 285f, gap = 10f;

            void BuildButton(string label, float x, UnityEngine.Events.UnityAction onClick)
            {
                var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(stage, false);
                var rt = (RectTransform)go.transform;
                PlaceTL(rt, x, y, w, h);

                var img = go.GetComponent<Image>();
                img.sprite = HudTextures.RoundedBox(32, 0.25f);
                img.type = Image.Type.Sliced;
                img.color = fill;

                var outline = AddImage(rt, HudTextures.RoundedBoxOutline(32, 0.25f, 3f), yellow, "Outline");
                outline.type = Image.Type.Sliced;
                Stretch(outline.rectTransform);
                outline.raycastTarget = false;

                var text = AddText(rt, 24f, yellow, TextAnchor.MiddleCenter, FontStyle.Bold);
                Stretch(text.rectTransform);
                text.text = label;

                var btn = go.GetComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(onClick);
            }

            for (int slot = 0; slot < SaveSystem.SlotCount && slot < cardX.Length; slot++)
            {
                int capturedSlot = slot;
                BuildButton("DEV - WORLD 2", cardX[slot], () => OnWorldDevStart(capturedSlot, 1, maxRig: false));
                BuildButton("DEV - WORLD 3", cardX[slot] + w + gap, () => OnWorldDevStart(capturedSlot, 2, maxRig: true));
            }
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

        /// <summary>"best: N deaths" (MV-427; supersedes YT-218's peak-Domination-% example, which
        /// stopped discriminating once a death no longer ends the run) — nothing else survives between
        /// runs, so the personal best is the whole story a slot card has to tell. MV-960: no longer
        /// prefixes the name — the card already shows it separately above this text.</summary>
        private static string Summarise(SaveSlotData data)
        {
            if (data.BestDeathsToVictory < 0) return "no finished run yet";
            string deaths = data.BestDeathsToVictory == 1 ? "1 death" : $"{data.BestDeathsToVictory} deaths";
            return $"best: {deaths}";
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
            UnityEngine.Events.UnityAction onClick, float fontSize = 22f)
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

            var t = AddText((RectTransform)go.transform, fontSize, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
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

        /// <summary>Places a rect at (x, y) in its parent's own top-left space, y increasing DOWNWARD
        /// — the stage-coordinate convention the ticket's own layout spec (MV-960) is written in, so
        /// call sites can paste its numbers directly instead of re-deriving anchored-position signs.</summary>
        private static void PlaceTL(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = new Vector2(x, -y);
        }
    }
}
