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
            bool captureBypass = PressKitDirector.Armed() || MaxWorlds.Dev.UiScreensDirector.Armed() ||
                MaxWorlds.Dev.PerfCaptureDirector.Armed() || MaxWorlds.Dev.CaptureDirector.Armed();

            // MV-1032: a CaptureDirector preset's own BeforeSceneLoad often seeds ActiveSlot = 0 itself
            // (so time is never paused in the first place — see CaptureDirector's own presets), which
            // used to hit the "a slot is already live" branch below and trust whatever slot 0 already
            // held on disk verbatim — a genuinely live/paused run from a prior session, not a controlled
            // one. A capture must always fall through to the clean bypass instead, same as the other
            // three directors already do.
            if (SaveSystem.ActiveSlot >= 0 && !captureBypass)
            {
                // MV-985: HomeScreen.OnResume left a cross-world resume pending just before triggering
                // this very reload (the checkpoint's world differed from what was already built) — this
                // is that reload's first Start(), so finish the resume it deferred, exactly once, instead
                // of falling through to the plain "a slot is already live" mark below.
                if (SaveSystem.PendingResume.HasValue && SaveSystem.PendingResume.Value.Slot == SaveSystem.ActiveSlot)
                {
                    int pendingSlot = SaveSystem.PendingResume.Value.Slot;
                    bool pendingFinaleJump = SaveSystem.PendingDevFinaleJump;
                    SaveSystem.PendingResume = null;
                    SaveSystem.PendingDevFinaleJump = false;
                    if (pendingFinaleJump) ApplyDevFinaleJump(pendingSlot);
                    else ApplyResume(pendingSlot);
                    return;
                }

                // A slot is already live: either a defensive re-add, or exactly the Replay-triggered
                // reload YT-216 targets (sub-3-second AC) — this is the earliest point that reload
                // hands control back, so it's the "controllable" mark for that path.
                BootTiming.Mark("controllable-replay");
                return;
            }

            if (captureBypass)
            {
                // Filming (press-kit), a fixed-state UI capture (ui-screens), an unattended frame-time
                // sample (MV-494), or a CaptureDirector preset (MV-1032) all have nothing to click the
                // modal with — hand off to slot 0 straight away, without pausing (Open() below sets
                // Time.timeScale = 0, which would freeze the very simulation a perf capture exists to
                // measure) or showing anything, the same lever PressKitDirector already used (YT-97;
                // MV-441). StartSlot's own WipeForFreshRun clears any stale checkpoint first, so a
                // capture never resumes a live run left over from a previous session.
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
        /// <item>false (WORLD 2, MV-737; SUPPORT carve-out MV-1095): World 1's own EXIT state —
        /// ENERGY/MOVE additionally unlocked and maxed to World 1's own level caps
        /// (<see cref="UnlockAndMaxCategory"/>, capped via <see cref="RigBoard.SnapshotMaxLevels"/> —
        /// MV-856: never World 2's own higher caps/extra nodes, which stay for the player to buy in
        /// World 2), PRIMARY left on the morph's owned-but-unupgraded floor, SECONDARY mystery-locked
        /// untouched, SUPPORT LOCKED (MV-1095: a real player arriving this way has it locked too, same
        /// as SECONDARY — <see cref="WeaponSystemState.ApplyWeaponCoreMorph"/> already remembers
        /// whatever this dev jump would otherwise have maxed, which is nothing on a fresh dev slot),
        /// exactly as a player who cleared World 1 would arrive.</item>
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
                // switched RigBoard onto (World 2's, which raises existing caps, e.g. e_ff's maxLevel
                // 8 -> 10) — a WORLD 2 start must arrive exactly as a player who cleared World 1 would,
                // with World 2's own higher ceilings left for the player to buy into in World 2.
                // MV-1157: e_cmg (Cell Magneto) is no longer a World-2-only addition here — it now
                // lives on World 1's own board too, so this cap sweep maxes it like any other owned
                // ENERGY node, same as a player who actually earned it in World 1 would have.
                IReadOnlyDictionary<string, int> world1MaxLevels = RigBoard.SnapshotMaxLevels(0);
                UnlockAndMaxCategory("ENERGY", world1MaxLevels);
                UnlockAndMaxCategory("MOVE", world1MaxLevels);
                // MV-1095: SUPPORT is deliberately NOT unlocked/maxed here any more — a real player
                // arriving this way has the family LOCKED (ApplyWeaponCoreMorph above already does
                // that), re-earned through this world's own cadence, same as SECONDARY.
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
        /// read World 2's board (needed for them to succeed at all on a genuinely world-2-only id, were
        /// one ever added — MV-1157 moved the one id that used to be the example here, <c>e_cmg</c>,
        /// onto World 1's own board too) — this method's own <c>cap</c> check is what stops the sweep
        /// short of whatever higher ceiling the active board allows.</summary>
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
        /// definition not a first launch).
        ///
        /// MV-985: the checkpoint's own world (<see cref="SaveSlotData.CheckpointWorldIndex"/>) can
        /// differ from whichever world is already built behind this screen — e.g. after
        /// <see cref="MaxWorlds.UI.RunFlow.QuitToMenu"/>, which always rebuilds World 1
        /// (<see cref="MaxWorlds.Arena.BackyardPath.ActiveWorldIndex"/> reads World 0 with no active
        /// slot). <see cref="SaveSystem.ResolveResumePlan"/> is what decides: if the target world is
        /// already up, <see cref="ApplyResume"/> runs immediately exactly as before this ticket;
        /// otherwise this reloads the scene the same way <see cref="OnWorldDevStart"/> does, having left
        /// <see cref="SaveSystem.PendingResume"/> set so the freshly-booted scene builds the CHECKPOINT'S
        /// world (not this slot's <see cref="SaveSlotData.WorldIndex"/>) and <see cref="Start"/> finishes
        /// the resume on its very first frame.</summary>
        private void OnResume(int slot)
        {
            SaveSlotData data = SaveSystem.Load(slot);
            if (!data.HasRunInProgress) return;   // guards a stray tap on what should be non-interactable

            var path = FindFirstObjectByType<MaxWorlds.Arena.BackyardPath>();
            int builtWorldIndex = path != null ? path.ResolvedWorldIndex : 0;
            SaveSystem.ResumePlan plan = SaveSystem.ResolveResumePlan(slot, builtWorldIndex);

            if (!plan.NeedsReload)
            {
                ApplyResume(slot);
                return;
            }

            SaveSystem.ActiveSlot = slot;
            SaveSystem.PendingResume = new SaveSystem.PendingResumePlan(slot, plan.WorldIndex);
            Time.timeScale = 1f;
            Scene scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
        }

        /// <summary>The actual restore, shared by <see cref="OnResume"/>'s immediate (same-world) path
        /// and <see cref="Start"/>'s post-reload (cross-world, MV-985) path — everything that used to run
        /// unconditionally inside <see cref="OnResume"/> once past its <c>HasRunInProgress</c> guard.
        ///
        /// MV-1023: the target world is resolved the same way both those callers already did before
        /// deciding whether to reload (<see cref="SaveSystem.ResolveResumePlan"/>'s own
        /// <c>WorldIndex</c> — the checkpoint's own world, falling back to <see cref="SaveSlotData.WorldIndex"/>
        /// for a pre-MV-985 save) — <c>builtWorldIndex</c> only feeds that method's <c>NeedsReload</c>,
        /// which this call never reads, so 0 is passed with no effect on the resolved world.</summary>
        private void ApplyResume(int slot)
        {
            int worldIndex = SaveSystem.ResolveResumePlan(slot, builtWorldIndex: 0).WorldIndex;
            ApplyResumeState(slot, worldIndex);

            SaveSlotData data = SaveSystem.Load(slot);
            Close();

            var runner = FindFirstObjectByType<MaxWorlds.Arena.WorldRunner>();
            runner?.ResumeCheckpoint(data.CheckpointAreaIndex, data.CheckpointGateId);

            // MV-1129: this world's own finale gate (if its final boss area is already recorded
            // defeated) never learns that from ApplyResumeState's own RestoreCheckpoint call above --
            // BossCensus.ApplyCheckpointDefeatedAreas deliberately never re-fires BossDefeated, see that
            // method's own doc comment -- so without this call a RESUME into a world whose final boss
            // already died, mid-finale, strands the run.
            var gate = FindFirstObjectByType<WorldFinaleGate>();
            gate?.ResumeFromCheckpoint(data.CheckpointFinaleWeaponGranted, data.CheckpointFinaleExitOpen);
        }

        /// <summary>MV-1023: the resume restore's own public static seam, testable without a scene —
        /// everything <see cref="ApplyResume"/> used to do unconditionally before its UI/runner tail.
        /// Applies <paramref name="worldIndex"/>'s loadout (<see cref="WeaponSystemState.ApplyWorldLoadout"/>)
        /// BEFORE <see cref="SaveSystem.RestoreCheckpoint"/> so <see cref="RigState.RestoreSnapshot"/>
        /// resolves the checkpoint's node ids against the RIGHT world's board — before this ticket,
        /// <see cref="RigBoard"/>/<see cref="WeaponSystemState.ActivePrimary"/>/<see cref="WeaponSystemState.SecondaryKind"/>
        /// were process statics nothing on the resume path touched, so a cold RESUME into World 2/3 kept
        /// firing World 1's RCDA on World 1's board while <c>RigState</c> quietly held World 2/3 node ids
        /// nothing could read. A still-pending Weapon Core (<see cref="SaveSlotData.WeaponCorePending"/>,
        /// restored below) is untouched here — <see cref="WeaponSystemState.OpenWeaponCoreMorphIfPending"/>
        /// still runs it on the next RIG open/run start exactly as in live play, and its result wins,
        /// since it runs strictly after this.
        ///
        /// MV-1080: the loadout applied is the checkpoint's own RIG BOARD world
        /// (<see cref="SaveSlotData.CheckpointRigBoardWorldIndex"/>), not <paramref name="worldIndex"/>
        /// (the PLAYED world) directly — a finale's CLEAN-UP window can leave those two different, since
        /// the Core morphs the board onto the next world the instant it's collected, before the played
        /// world itself advances. <paramref name="worldIndex"/> is still what the map loads for (resolved
        /// separately by <see cref="SaveSystem.ResolveResumePlan"/>) and is the fallback for a save that
        /// predates this field.</summary>
        public static void ApplyResumeState(int slot, int worldIndex)
        {
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

            SaveSlotData data = SaveSystem.Load(slot);
            int rigBoardWorldIndex = data.CheckpointRigBoardWorldIndex >= 0 ? data.CheckpointRigBoardWorldIndex : worldIndex;
            WeaponSystemState.ApplyWorldLoadout(rigBoardWorldIndex);
            SaveSystem.RestoreCheckpoint(slot);
            // RestoreCheckpoint sets RigState directly, which never touches WeaponSystemState's own
            // acquisition-order list — without this every restored ability reads as unacquired on the
            // Weapons screen despite working correctly in combat (RigState is what AbilityLevel/
            // IsAcquired actually read).
            WeaponSystemState.RebuildAcquiredFromRigState();
        }

        /// <summary>DEV: FINAL AREA tapped (MV-1057) — Lee's own dev shortcut to reach a world's finale
        /// area (whichever area carries the "boss" role at the end of <see cref="MaxWorlds.Arena.WorldConfig.dials"/>'s
        /// <c>areaCount</c>) without replaying the whole world, so a world-to-world transition can be
        /// tested without a full playthrough. Starts the slot's own saved world — its checkpoint's world
        /// if a run is in progress, else <see cref="SaveSlotData.WorldIndex"/> (world 1 for a never-played
        /// slot), via the exact same <see cref="SaveSystem.ResolveResumePlan"/> RESUME itself uses — and
        /// keeps the slot's own RIG/loadout exactly as RESUME would (<see cref="ApplyResumeState"/>);
        /// nothing here maxes or otherwise inflates it, and nothing here is written back to the save.
        /// Same cross-world reload shape as <see cref="OnResume"/> (MV-985): reloads only when the target
        /// world differs from what's already built, via <see cref="SaveSystem.PendingResume"/>, with
        /// <see cref="SaveSystem.PendingDevFinaleJump"/> marking that the reload's own <see cref="Start"/>
        /// should finish as this jump rather than an ordinary RESUME.</summary>
        private void OnDevFinalAreaTapped(int slot)
        {
            var path = FindFirstObjectByType<MaxWorlds.Arena.BackyardPath>();
            int builtWorldIndex = path != null ? path.ResolvedWorldIndex : 0;
            int worldIndex = SaveSystem.ResolveResumePlan(slot, builtWorldIndex).WorldIndex;

            if (worldIndex == builtWorldIndex)
            {
                ApplyDevFinaleJump(slot);
                return;
            }

            SaveSystem.ActiveSlot = slot;
            SaveSystem.PendingResume = new SaveSystem.PendingResumePlan(slot, worldIndex);
            SaveSystem.PendingDevFinaleJump = true;
            Time.timeScale = 1f;
            Scene scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
        }

        /// <summary>The actual jump (MV-1057), shared by <see cref="OnDevFinalAreaTapped"/>'s immediate
        /// (same-world) path and <see cref="Start"/>'s post-reload (cross-world) path — restores the
        /// slot's own RIG/loadout the same way <see cref="ApplyResume"/> does, then hands off to
        /// <see cref="MaxWorlds.Arena.WorldRunner.JumpToFinaleArea"/> for the prerequisite-clearing and
        /// landing itself. Closes the modal exactly as <see cref="ApplyResume"/> does — harmless when
        /// called from <see cref="Start"/>, where this instance was never opened in the first place.</summary>
        private void ApplyDevFinaleJump(int slot)
        {
            int worldIndex = SaveSystem.ResolveResumePlan(slot, builtWorldIndex: 0).WorldIndex;
            ApplyResumeState(slot, worldIndex);
            Close();

            var runner = FindFirstObjectByType<MaxWorlds.Arena.WorldRunner>();
            runner?.JumpToFinaleArea();
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
            // MV-986: keyed by world index, filled at most once per world across this Build() call —
            // several slots can name the same world.
            var worldInfoCache = new Dictionary<int, (string Name, int AreaCount)>();
            for (int i = 0; i < SaveSystem.SlotCount && i < cardX.Length; i++)
            {
                BuildCard(stage.rectTransform, i, cardX[i], cardTop, cardW, cardH, worldInfoCache);
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
            const float w = 290f, h = 90f;
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

            // Same TechRings procedural icon the in-game gear used to draw (MV-961 removed that
            // gear), tinted Bone to match this button's own text rather than the panel's Accent green.
            var icon = AddImage(rt, HudTextures.TechRings(96, 3), Bone, "Icon");
            icon.raycastTarget = false;
            Anchor(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            icon.rectTransform.sizeDelta = new Vector2(44f, 44f);
            icon.rectTransform.anchoredPosition = new Vector2(20f, 0f);

            var label = AddText(rt, 28f, Bone, TextAnchor.MiddleLeft, FontStyle.Bold);
            Anchor(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            label.rectTransform.sizeDelta = new Vector2(w - 76f - 16f, 40f);
            label.rectTransform.anchoredPosition = new Vector2(76f, 0f);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.text = "SETTINGS";

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(OnSettingsTapped);
        }

        private void BuildCard(RectTransform stage, int slot, float x, float y, float w, float h,
            Dictionary<int, (string Name, int AreaCount)> worldInfoCache)
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
            status.gameObject.name = "Status";
            // MV-986: raised (was y 124, h 68) to fit three lines and still end at/above y 204, clear
            // of RESUME/PLAY at y 210.
            PlaceTL(status.rectTransform, 28f, 96f, 520f, 108f);
            status.text = Summarise(data, worldInfoCache);

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

        /// <summary>WORLD 2/3 dev entry points (MV-726; MV-736; relocated off the cards by MV-960), plus
        /// DEV: FINAL AREA (MV-1057) — NOT part of the design, built to be deleted later: every button
        /// this builds lives in this one method, called from the single guarded line in
        /// <see cref="Build"/>, so removing them is exactly delete-this-method + delete-the-const +
        /// delete-the-one-call. MV-1057 shrank WORLD 2/3's own width from 285 to a three-across split of
        /// the same card footprint (580 wide) to make room for the third button — DEV: FINAL AREA is
        /// visible on every slot (same "always there, only tappable when it means something" idiom as
        /// RESET on an occupied/empty card) but only interactable on an occupied one, since there is
        /// nothing to jump to on an empty slot.</summary>
        private void BuildDevWorldShortcuts(RectTransform stage)
        {
            var yellow = new Color(242f / 255f, 196f / 255f, 58f / 255f);
            var fill = Color.Lerp(PanelColor, yellow, 0.08f);
            float[] cardX = { 40f, 650f, 1260f };
            const float y = 846f, h = 66f, cardW = 580f, gap = 10f;
            const float w = (cardW - 2f * gap) / 3f;

            void BuildButton(string label, float x, bool interactable, UnityEngine.Events.UnityAction onClick)
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

                Color textColor = interactable ? yellow : new Color(yellow.r, yellow.g, yellow.b, 0.35f);
                var text = AddText(rt, 22f, textColor, TextAnchor.MiddleCenter, FontStyle.Bold);
                Stretch(text.rectTransform);
                text.text = label;

                var btn = go.GetComponent<Button>();
                btn.targetGraphic = img;
                btn.interactable = interactable;
                btn.onClick.AddListener(onClick);
            }

            for (int slot = 0; slot < SaveSystem.SlotCount && slot < cardX.Length; slot++)
            {
                int capturedSlot = slot;
                bool occupied = SaveSystem.Load(slot).HasData;
                BuildButton("DEV - WORLD 2", cardX[slot], true, () => OnWorldDevStart(capturedSlot, 1, maxRig: false));
                BuildButton("DEV - WORLD 3", cardX[slot] + w + gap, true, () => OnWorldDevStart(capturedSlot, 2, maxRig: true));
                BuildButton("DEV: FINAL AREA", cardX[slot] + 2f * (w + gap), occupied, () => OnDevFinalAreaTapped(capturedSlot));
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

        /// <summary>The card's three-line status block (MV-986; supersedes MV-427's single "best: N
        /// deaths" line, which named neither the world, the area of N, nor how long a run in progress
        /// had been played). <paramref name="worldInfoCache"/> is filled at most once per world index
        /// across one <see cref="Build"/> call (see call site).</summary>
        private static string Summarise(SaveSlotData data, Dictionary<int, (string Name, int AreaCount)> worldInfoCache)
        {
            if (!data.HasData) return "Empty";

            if (data.HasRunInProgress)
            {
                int worldIndex = data.CheckpointWorldIndex >= 0 ? data.CheckpointWorldIndex : data.WorldIndex;
                (string name, int areaCount) = WorldInfo(worldIndex, worldInfoCache);
                string line1 = $"World {worldIndex + 1}: {name} - Area {data.CheckpointAreaIndex} of {areaCount}";
                string line2 = $"Played {FormatPlayedTime(data.CheckpointElapsedSeconds)} - {DeathsPhrase(data.CheckpointDeathsTaken)}";
                return $"{line1}\n{line2}\n{BestLine(data.BestDeathsToVictory)}";
            }

            (string nextName, _) = WorldInfo(data.WorldIndex, worldInfoCache);
            string next = $"Next: World {data.WorldIndex + 1}: {nextName}";
            string furthest = $"Furthest: World {data.FurthestWorldIndex + 1}";
            return $"{next}\n{BestLine(data.BestDeathsToVictory)}\n{furthest}";
        }

        /// <summary>Loads <paramref name="worldIndex"/>'s config at most once per <see cref="Build"/>
        /// call (several slots can name the same world) and pulls the card-facing name/area count out
        /// of it.</summary>
        private static (string Name, int AreaCount) WorldInfo(int worldIndex,
            Dictionary<int, (string Name, int AreaCount)> cache)
        {
            if (cache.TryGetValue(worldIndex, out var cached)) return cached;

            string name = string.Empty;
            int areaCount = 0;
            MaxWorlds.Arena.WorldConfig cfg = MaxWorlds.Arena.WorldLibrary.Load(MaxWorlds.Arena.WorldLibrary.KeyForIndex(worldIndex));
            if (cfg != null)
            {
                name = WorldDisplayName(cfg.world);
                if (cfg.dials != null) areaCount = cfg.dials.areaCount;
            }

            var info = (name, areaCount);
            cache[worldIndex] = info;
            return info;
        }

        /// <summary>A world config's <c>world</c> field is authored "World N &#x2014; Name" (em dash);
        /// the card shows only the part after it, trimmed, and never the em dash itself (card text is
        /// ASCII-only).</summary>
        private static string WorldDisplayName(string configWorldString)
        {
            if (string.IsNullOrEmpty(configWorldString)) return string.Empty;
            int dash = configWorldString.IndexOf('—');
            return dash < 0 ? configWorldString.Trim() : configWorldString.Substring(dash + 1).Trim();
        }

        /// <summary>Whole minutes, rounded down: under a minute reads as "under 1 min" rather than
        /// "0 min"; under an hour as plain minutes; an hour or more gains an "H h MM min" prefix with
        /// the minutes zero-padded.</summary>
        private static string FormatPlayedTime(float elapsedSeconds)
        {
            int totalMinutes = Mathf.FloorToInt(elapsedSeconds / 60f);
            if (totalMinutes < 1) return "under 1 min";
            if (totalMinutes < 60) return $"{totalMinutes} min";
            return $"{totalMinutes / 60} h {totalMinutes % 60:00} min";
        }

        private static string DeathsPhrase(int deaths) => deaths == 1 ? "1 death" : $"{deaths} deaths";

        private static string BestLine(int bestDeaths) =>
            bestDeaths < 0 ? "No finished run yet" : $"Best: {DeathsPhrase(bestDeaths)}";

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
