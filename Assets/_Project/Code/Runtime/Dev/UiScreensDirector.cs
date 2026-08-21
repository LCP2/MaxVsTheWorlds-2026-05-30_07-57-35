using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.UI;
using MaxWorlds.Weapons;
using MaxWorlds.Pickups;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// Renders fixed-state screenshots of the game's self-installing UI screens (MV-421), separate
    /// from <see cref="PressKitDirector"/>'s live-gameplay press kit. <see cref="PressKitDirector"/>
    /// can't reach these: its capture technique is a manual <c>Camera.Render()</c>, which never draws
    /// a <c>ScreenSpaceOverlay</c> canvas (WeaponsScreen, HomeScreen, ResultScreen, SettingsPanel are
    /// each a separate self-installing GameObject with their own such canvas — none reachable via
    /// <c>HudController</c> the way the press kit's own <c>ShowHud</c> flip works).
    ///
    /// MV-444: capture technique is a dedicated, UI-only orthographic camera. The shot's target canvas
    /// is flipped from <c>ScreenSpaceOverlay</c> to <c>ScreenSpaceCamera</c> on that camera (the same
    /// idiom <see cref="PressKitDirector"/>'s <c>ShowHud</c> uses for the HUD, generalised to any
    /// canvas), rendered into a <see cref="RenderTexture"/> sized to the shot, and read back with
    /// <c>ReadPixels</c>. This never touches the real back buffer, so it works in <c>-batchmode</c>
    /// with no attached display — <see cref="ScreenCapture.CaptureScreenshotAsTexture()"/>, the
    /// technique this replaces, reads the back buffer and cannot. See MV-444 for the history: this was
    /// diagnosed three times (MV-421 comment 11900, MV-441, MV-443) before actually being fixed.
    ///
    /// INERT in a normal session, same idiom as PressKitDirector: it installs only behind the
    /// <c>-uiscreens</c> command-line flag or a <c>Temp/uiscreens.arm</c> marker (see
    /// <see cref="UiScreensCapture"/>), and never both directors at once — the two marker files are
    /// deliberately distinct so a capture run only ever arms the one it asked for.
    ///
    /// Scope for MV-421: THE RIG board — the screen the ticket calls out as "the one that matters
    /// most" and the one MV-433/424/425/426 need real evidence against. MV-425 extends it with the
    /// HUD's WEAPONS button (its own four alert states); HomeScreen and ResultScreen remain
    /// follow-up work, not rebuilt here.
    /// </summary>
    public sealed class UiScreensDirector : MonoBehaviour
    {
        private const string DoneMarker = "_uiscreens_done.txt";

        /// <summary>MV-441 AC4: the Cowork design chat's reachable folder — same directory
        /// <c>CC_AUTONOMY.md</c> already grants this worker read/write on, no credentials, no CI, no
        /// <c>ui-screens</c> branch needed. Overwritten in place every run; best-effort (see
        /// <see cref="TryWriteSecondary"/>) so a CI box with no such drive doesn't fail the whole job.</summary>
        private const string DesignImagesScreensDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

        private string _outDir;
        private string _outDir2;
        private readonly StringBuilder _manifest = new StringBuilder();
        private readonly List<string> _failures = new List<string>();
        private int _shotsWritten;
        private Camera _captureCam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Armed()) return;
            if (FindFirstObjectByType<UiScreensDirector>() != null) return;
            new GameObject("UiScreensDirector").AddComponent<UiScreensDirector>();
        }

        public static bool Armed()
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "-uiscreens", StringComparison.OrdinalIgnoreCase)) return true;
            try { return File.Exists(Path.Combine("Temp", "uiscreens.arm")); }
            catch { return false; }
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            _outDir = Arg("-uiscreensOut") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press"));
            Directory.CreateDirectory(_outDir);

            try { Directory.CreateDirectory(DesignImagesScreensDir); _outDir2 = DesignImagesScreensDir; }
            catch (Exception e) { LogWarn($"secondary output dir unavailable ({DesignImagesScreensDir}): {e.Message}"); _outDir2 = null; }

            Log($"ui-screens capture starting → {_outDir}" + (_outDir2 != null ? $" (+ {_outDir2})" : ""));

            _captureCam = CreateCaptureCamera();

            yield return CaptureRigBoard();
            yield return CaptureWeaponsButton();

            Finish();

            if (_captureCam != null) { Destroy(_captureCam.gameObject); _captureCam = null; }
        }

        /// <summary>A camera that exists only to render a single flipped-to-camera-space canvas into a
        /// capture <see cref="RenderTexture"/> — never the scene's <c>Camera.main</c>, so this job never
        /// disturbs the live gameplay camera or depends on where it happens to be pointed. Starts with
        /// an empty culling mask; <see cref="ShowCanvasOnCamera"/> ORs in the UI layer per shot, so the
        /// only thing this camera ever draws is the one canvas currently staged for capture — which is
        /// also why "THE RIG board alone" (MV-444 AC2) holds regardless of what the 3D scene behind it
        /// looks like.</summary>
        private static Camera CreateCaptureCamera()
        {
            var go = new GameObject("UiScreensCaptureCamera");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 10f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            cam.depth = -100f;
            cam.enabled = false;   // rendered manually via Camera.Render(), never as part of the automatic camera stack
            return cam;
        }

        // --- THE RIG (MV-421 scope) -------------------------------------------------------------

        /// <summary>MV-463 Part 1: one shot per <see cref="RigBoardLayout.CaptureAspects"/> entry (the
        /// data file, not this method, decides which aspects matter — today that's the 16:9 reference
        /// frame, 1:1 comparable to MV-423.png; the 1.6:1 narrowest-desktop-browser frame, does SUPPORT
        /// clip?; and the ~2.17:1 phone viewport the game is actually played at), plus a fixed 16:9
        /// parts=0 variant (does the amber '+' badge / empty tray render correctly?).</summary>
        private IEnumerator CaptureRigBoard()
        {
            var weapons = FindFirstObjectByType<WeaponsScreen>();
            if (weapons == null) { LogWarn("rig: no WeaponsScreen in the scene"); yield break; }

            // WeaponsScreen self-installs its GameObject in its own RuntimeInitializeOnLoadMethod, but
            // only builds its Canvas later, in its own Start(). Both directors install at AfterSceneLoad
            // and Start() their capture coroutine the same frame, so which Start() runs first (and
            // therefore whether the canvas exists yet) is a same-frame race, not a guarantee — caught
            // live running MV-444: it lost the race and skipped all 3 rig shots. Give it a few frames.
            Canvas canvas = null;
            for (int i = 0; i < 10 && canvas == null; i++)
            {
                canvas = weapons.GetComponentInChildren<Canvas>(true);
                if (canvas == null) yield return null;
            }
            if (canvas == null) { LogWarn("rig: WeaponsScreen built no canvas"); yield break; }

            void ScaleBoardTo(int w, int h) => weapons.ApplyBoardScale((float)w / h);

            foreach (var aspect in RigBoardLayout.CaptureAspects)
                yield return CaptureFixtureScreen($"rig-{aspect.Name}", aspect.W, aspect.H, ApplyRigFixture, weapons.Open, weapons.Close, canvas, ScaleBoardTo);
            yield return CaptureFixtureScreen("rig-noparts-16x9", 1920, 1080, ApplyRigFixtureNoParts, weapons.Open, weapons.Close, canvas, ScaleBoardTo);
            // MV-470/MV-511: same node levels as the main fixture, but too few cells to afford EITHER a
            // cell unlock (10) or the cheapest upgrade (5, level 1) — evidences the "reads as inert" half
            // of AC1 (a node the player can't yet afford), which the 28-cell main fixture can never show
            // since 28 covers every cost on the board.
            yield return CaptureFixtureScreen("rig-lowcells-16x9", 1920, 1080, ApplyRigFixtureLowCells, weapons.Open, weapons.Close, canvas, ScaleBoardTo);
            // MV-470: a true run-start board (only PRIMARY unlocked, per RigState.Reset's own baseline)
            // — every other fixture above forces every category open (ResetRunForFixture), so none of
            // them can evidence the "family not unlocked" lock reason at all.
            yield return CaptureFixtureScreen("rig-freshrun-16x9", 1920, 1080, ApplyRigFixtureFreshRun, weapons.Open, weapons.Close, canvas, ScaleBoardTo);

            // MV-472 (current spec) hand-off verification: the exact three viewport pixel sizes Lee's
            // ticket comment names (977x458, 852x393 iPhone landscape, 1133x744 iPad mini landscape) —
            // distinct from RigBoardLayout.CaptureAspects above, which are the fixed reference-aspect
            // fixtures MV-463's conformance checks run against. These are one-off, name-gated shots (only
            // "rig-16x9" triggers RunConformanceChecks/BuildContactSheet — see CaptureFixtureScreen)
            // purely so Lee can eyeball the literal viewport his own device measurements came from.
            yield return CaptureFixtureScreen("rig-mv472-977x458", 977, 458, ApplyRigFixture, weapons.Open, weapons.Close, canvas, ScaleBoardTo);
            yield return CaptureFixtureScreen("rig-mv472-852x393", 852, 393, ApplyRigFixture, weapons.Open, weapons.Close, canvas, ScaleBoardTo);
            yield return CaptureFixtureScreen("rig-mv472-1133x744", 1133, 744, ApplyRigFixture, weapons.Open, weapons.Close, canvas, ScaleBoardTo);
        }

        /// <summary>Matches the state shown in MV-423.png node-for-node (MV-421's own spec), so the
        /// capture and the design image are directly comparable. Every node not spent here is left at
        /// its <see cref="RigState.Reset"/> baseline — that alone gives p_prc/s_bal/e_mag/m_spd/m_tp
        /// their "reached, not owned" states and u_slt its "not reached" one, off the same
        /// parent-level rule <see cref="RigState.IsReached"/> already enforces, with nothing extra to
        /// stage for them. Public (and static — no scene/canvas needed) so <c>UiScreensFixtureTests</c>
        /// can assert the resulting <see cref="RigState"/>/<see cref="PickupWallet"/> values directly,
        /// without a play-mode capture.</summary>
        public static void ApplyRigFixture()
        {
            ResetRunForFixture();
            SpendRigFixtureLevels();
            PickupWallet.SetPowerCells(28);   // needs e_cel spent to 1 first — Capacity reads RigState
            for (int i = 0; i < 4; i++) PickupWallet.AddPart();   // parts banked: 4
        }

        /// <summary>The same board state, minus the 4 banked parts — comparing this against the
        /// parts=4 shot is how the amber '+' badge and the parts-tray fill state get evidenced.</summary>
        public static void ApplyRigFixtureNoParts()
        {
            ResetRunForFixture();
            SpendRigFixtureLevels();
            PickupWallet.SetPowerCells(28);
        }

        /// <summary>MV-470: the same node levels as <see cref="ApplyRigFixture"/>, but only 4 cells
        /// banked — below both <see cref="CellSpend.UnlockCostCells"/> (10) and the cheapest possible
        /// <see cref="CellSpend.UpgradeCostFor"/> (level 1, 5 cells), so every cell-costed node on the
        /// board reads inert rather than live. MV-511 dropped the fixed 5-cell value this used to bank —
        /// that used to sit safely below the old flat 10-cell upgrade cost, but now equals the new level-1
        /// upgrade cost exactly, which would make e_cel/u_sen/u_rng (each owned at level 1 by
        /// <see cref="SpendRigFixtureLevels"/>) read affordable and break this fixture's own purpose. The
        /// main fixture's 28 cells cover every cost, so it can only ever evidence the affordable half of
        /// AC1.</summary>
        public static void ApplyRigFixtureLowCells()
        {
            ResetRunForFixture();
            SpendRigFixtureLevels();
            PickupWallet.SetPowerCells(4);
        }

        /// <summary>MV-470: an actual run start — <see cref="RigState.Reset"/>'s own baseline (only
        /// PRIMARY unlocked, p_dmg at level 1), nothing force-opened. Every other fixture calls
        /// <see cref="ResetRunForFixture"/>, which deliberately unlocks every category so the reference
        /// shot matches MV-423.png's "whole board open" spec — but that also means none of them can ever
        /// show a family-not-unlocked node, the ticket's own first lock reason.</summary>
        public static void ApplyRigFixtureFreshRun()
        {
            RigState.Reset();
            PickupWallet.Reset();
            PickupWallet.SetPowerCells(15);
        }

        private static void ResetRunForFixture()
        {
            RigState.Reset();
            PickupWallet.Reset();

            // MV-457: sheds now unlock a whole category, root nodes stay unreached until then — the
            // reference shot means to show the whole board open (matching MV-423.png's own "every
            // category lit" reference), not one mid-run's actual shed progression, so force every
            // category open here rather than threading shed picks through the fixture.
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
        }

        private static void SpendRigFixtureLevels()
        {
            SpendToLevel("p_dmg", 4);
            RigState.AcquireCap("p_rng");
            SpendToLevel("p_rng", 3);
            RigState.AcquireCap("p_flw");
            SpendToLevel("p_flw", 2);

            RigState.AcquireCap("e_ff");
            SpendToLevel("e_ff", 2);
            RigState.AcquireCap("e_cel");
            RigState.AcquireCap("e_cd");
            SpendToLevel("e_cd", 3);

            RigState.AcquireCap("u_sen");
            RigState.AcquireCap("u_dmg");
            SpendToLevel("u_dmg", 2);
            RigState.AcquireCap("u_rng");
            SpendToLevel("u_rng", 1);
        }

        private static void SpendToLevel(string id, int target)
        {
            while (RigState.Level(id) < target)
            {
                if (!RigState.RaiseLevel(id))
                {
                    LogWarn($"rig fixture: could not raise {id} to {target} (stuck at {RigState.Level(id)})");
                    break;
                }
            }
        }

        // --- WEAPONS button (MV-425 scope) ------------------------------------------------------

        /// <summary>Four shots, one per <see cref="HudController.WeaponsButtonAlert"/> state — the
        /// "real evidence" MV-425's own AC asks for. HudController is already live in this scene (it's
        /// not self-installing like WeaponsScreen), so there's no open/close pair — the fixture drives
        /// PickupWallet/AbilityCreditBank/PendingMorphingModule directly, the same static-state-fixture
        /// idiom <see cref="ApplyRigFixture"/> already uses for RigState, and the button reacts to the
        /// real signal chain (OnParts/OnPendingModuleChanged) exactly as it would from a live pickup.</summary>
        private IEnumerator CaptureWeaponsButton()
        {
            var hud = FindFirstObjectByType<HudController>();
            if (hud == null) { LogWarn("weapons-button: no HudController in the scene"); yield break; }

            var canvas = hud.GetComponentInChildren<Canvas>(true);
            if (canvas == null) { LogWarn("weapons-button: HudController built no canvas"); yield break; }

            yield return CaptureFixtureScreen("weapons-button-idle", 1920, 1080, ApplyWeaponsButtonIdleFixture, null, null, canvas);
            yield return CaptureFixtureScreen("weapons-button-parts", 1920, 1080, ApplyWeaponsButtonPartsFixture, null, null, canvas);
            yield return CaptureFixtureScreen("weapons-button-module", 1920, 1080, ApplyWeaponsButtonModuleFixture, null, null, canvas);
            yield return CaptureFixtureScreen("weapons-button-both", 1920, 1080, ApplyWeaponsButtonBothFixture, null, null, canvas);

            // MV-510 AC A4 (review round 1 superseded AC8's 977x458 with 1920x1080): the
            // human-judged eyes-on shot, with real (nonzero) numbers on both RIG-mark counters so the
            // doubled mark, the recessive cell pill and the enlarged parts chip all read at once.
            yield return CaptureFixtureScreen("hud-mv510-1920x1080", 1920, 1080, ApplyHudMv510Fixture, null, null, canvas);

            ApplyWeaponsButtonIdleFixture();   // leave the scene in a clean state once the pass is done
        }

        /// <summary>MV-510 AC8 demo state — some parts AND some banked cells, so the human screenshot
        /// shows both RIG-mark counters with real numbers rather than "0" and "0/20".</summary>
        public static void ApplyHudMv510Fixture()
        {
            ApplyWeaponsButtonIdleFixture();
            for (int i = 0; i < 3; i++) PickupWallet.AddPart();
            for (int i = 0; i < 12; i++) PickupWallet.AddPowerCell();
        }

        public static void ApplyWeaponsButtonIdleFixture()
        {
            PickupWallet.Reset();
            AbilityCreditBank.Reset();
            PendingMorphingModule.Reset();
        }

        /// <summary>4 parts banked — matches the "4" badge in MV-425.png.</summary>
        public static void ApplyWeaponsButtonPartsFixture()
        {
            ApplyWeaponsButtonIdleFixture();
            for (int i = 0; i < 4; i++) PickupWallet.AddPart();
        }

        public static void ApplyWeaponsButtonModuleFixture()
        {
            ApplyWeaponsButtonIdleFixture();
            PendingMorphingModule.Set(new[] { "s_bal", "e_ff", "m_spd" });
        }

        public static void ApplyWeaponsButtonBothFixture()
        {
            ApplyWeaponsButtonIdleFixture();
            for (int i = 0; i < 4; i++) PickupWallet.AddPart();
            PendingMorphingModule.Set(new[] { "s_bal", "e_ff", "m_spd" });
        }

        // --- capture -----------------------------------------------------------------------------

        /// <summary>Applies a fixture, opens the screen, checks nothing else occludes it, then routes
        /// <paramref name="canvas"/> through <see cref="ShowCanvasOnCamera"/> onto the dedicated capture
        /// camera and reads the render back with <see cref="RenderCanvasToTexture"/> — see the class doc
        /// for why this replaced <c>ScreenCapture.CaptureScreenshotAsTexture()</c> (MV-444). Every wait
        /// after <paramref name="open"/> uses <see cref="WaitForSecondsRealtime"/>, never
        /// <see cref="WaitForSeconds"/> — <c>WeaponsScreen.Open()</c> sets <c>Time.timeScale = 0</c>,
        /// so a scaled wait would never elapse.
        ///
        /// <paramref name="onSizeKnown"/> (MV-462 defect 2): fires with the actual (w, h) render target
        /// right where <see cref="ShowCanvasOnCamera"/> already overrides the CanvasScaler against
        /// ambient <c>Screen.width</c>/<c>Screen.height</c> — same reason, same timing (before the
        /// settle-frame yields below, so the override is in place when the canvas rebuilds its layout).
        /// <see cref="CaptureRigBoard"/> uses it to drive <c>WeaponsScreen.ApplyBoardScale(float)</c>
        /// with this shot's real aspect instead of the ambient one, which otherwise shrank and recentred
        /// the board on a headless capture whose batchmode window isn't actually 16:9.</summary>
        private IEnumerator CaptureFixtureScreen(string name, int w, int h, Action applyFixture, Action open, Action close, Canvas canvas, Action<int, int> onSizeKnown = null)
        {
            Exception staged = null;
            try
            {
                applyFixture?.Invoke();
                open?.Invoke();
            }
            catch (Exception e) { staged = e; }
            if (staged != null) { LogWarn($"{name}: staging failed — {staged.Message}"); yield break; }

            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(0.1f);   // paused (timeScale 0) — real time only

            // MV-441: the shot must be the ONLY high-sorting-order screen up — HomeScreen's own
            // sortingOrder=220 canvas sat over every ui-screens capture uncaught until this ran.
            // A shot that opens a screen (rig-*) expects exactly that one (still ScreenSpaceOverlay at
            // this point — the flip to camera space happens below, after this check); a HUD-only shot
            // (the WEAPONS button states) opens nothing, so it expects zero — the HUD's own canvas is
            // pinned at exactly 100 (HudController.cs), below this ">100" threshold, by design.
            int expectedOverlays = open != null ? 1 : 0;
            string overlayError = CheckSingleActiveOverlay(expectedOverlays);
            if (overlayError != null)
            {
                string msg = $"{name}: canvas-overlay assertion failed — {overlayError}";
                LogWarn(msg);
                _manifest.AppendLine(msg);
                _failures.Add(msg);
                try { close?.Invoke(); } catch (Exception e) { LogWarn($"{name}: close failed — {e.Message}"); }
                yield break;
            }

            ShowCanvasOnCamera(canvas, _captureCam, w, h);
            onSizeKnown?.Invoke(w, h);

            // MV-444: a ScreenSpaceCamera canvas's on-screen size is computed from the camera's CURRENT
            // pixel dimensions the next time Unity rebuilds canvas geometry (Canvas.SendWillRenderCanvases,
            // driven by the frame yields below) — so the render target must already be assigned BEFORE
            // those yields, not right before Camera.Render(). Assigning it after the settle frames (the
            // original shape here) left the canvas sized for whatever the ambient batchmode window
            // happened to be, so it rendered undersized/letterboxed into this shot's actual w x h texture
            // — caught live running this exact ticket: every shot showed real content correctly, but
            // with solid black margins around it instead of filling the frame.
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prevTarget = _captureCam.targetTexture;
            float prevAspect = _captureCam.aspect;
            _captureCam.aspect = (float)w / h;
            _captureCam.targetTexture = rt;

            yield return null;   // let the canvas rebuild its layout at the new scale factor AND target size
            yield return null;

            try
            {
                var tex = ReadCameraRenderTexture(_captureCam, rt, w, h);
                try
                {
                    byte[] png = tex.EncodeToPNG();
                    File.WriteAllBytes(Path.Combine(_outDir, name + ".png"), png);
                    TryWriteSecondary(name, png);
                    _manifest.AppendLine($"{name}.png ({tex.width}x{tex.height})");
                    _shotsWritten++;
                    Log($"wrote {name}.png ({tex.width}x{tex.height})");

                    if (name == "rig-16x9")
                    {
                        RunOutsideBackgroundProbe(tex);
                        RunConformanceChecks(tex, "rig-16x9", w, h);
                        BuildContactSheet(tex);
                    }
                    // MV-480: the other two captured aspects that matter most to the actual complaints
                    // (rig-phone, rig-ipad-mini) were captured and never measured — see RunConformanceChecks'
                    // own doc comment for the coordinate transform this needed. rig-16x10 stays unmeasured
                    // (this ticket names exactly rig-16x9/rig-phone/rig-ipad-mini; rig-16x10 isn't one of
                    // the three), same as the mv472/lowcells/freshrun/noparts fixture variants.
                    else if (name == "rig-phone" || name == "rig-ipad-mini")
                    {
                        RunConformanceChecks(tex, name, w, h);
                    }
                }
                finally { Destroy(tex); }
            }
            catch (Exception e) { LogWarn($"{name}: capture failed — {e.Message}"); }
            finally
            {
                _captureCam.aspect = prevAspect;
                _captureCam.targetTexture = prevTarget;
                rt.Release();
                Destroy(rt);
                RestoreCanvas();
                try { close?.Invoke(); } catch (Exception e) { LogWarn($"{name}: close failed — {e.Message}"); }
            }
        }

        private void TryWriteSecondary(string name, byte[] png)
        {
            if (_outDir2 == null) return;
            try { File.WriteAllBytes(Path.Combine(_outDir2, name + ".png"), png); }
            catch (Exception e) { LogWarn($"{name}: secondary write to {_outDir2} failed — {e.Message}"); }
        }

        // --- generic canvas-on-camera capture (MV-444) ------------------------------------------
        // ScreenSpaceOverlay draws straight to the backbuffer, which does not exist in a batchmode
        // session with no attached display. Flipping a canvas to ScreenSpace-Camera composites it into
        // a RenderTexture render instead — the same idiom PressKitDirector.ShowHud uses for the HUD,
        // generalised here to any self-installing screen's canvas (WeaponsScreen, HudController).

        private Canvas _activeCaptureCanvas;
        private RenderMode _activeCaptureCanvasPrevMode;
        private Camera _activeCaptureCanvasPrevWorldCam;
        private float _activeCaptureCanvasPrevPlaneDistance;
        private int _activeCaptureCanvasPrevSortingOrder;
        private float _activeCaptureCanvasPrevScaleFactor;
        private int _activeCaptureCanvasPrevLayer;
        private CanvasScaler _activeCaptureScaler;
        private bool _activeCaptureScalerWasEnabled;

        /// <summary>Composite <paramref name="canvas"/> into <paramref name="cam"/>'s render at exactly
        /// the UI scale a <paramref name="w"/>x<paramref name="h"/> screen would produce.
        ///
        /// CanvasScaler's own "Scale With Screen Size" always reads the ambient <c>Screen.width</c>/
        /// <c>Screen.height</c>, not this capture's actual target resolution. Left enabled, CanvasScaler
        /// would size the UI for the wrong frame entirely. So: disable it for the duration and set
        /// <c>canvas.scaleFactor</c> ourselves with <see cref="ComputeScaleFactor"/>, which reimplements
        /// CanvasScaler's own match-width-or-height formula against the explicit (w, h) this shot is
        /// actually rendering at.
        ///
        /// Snapshots render mode, world camera, plane distance, sorting order (MV-444 AC3) and layer
        /// even though sorting order is not actually changed here — restoring all five defensively is
        /// what keeps a capture from being able to leave a screen mis-parented, the MV-440 failure
        /// shape.</summary>
        private void ShowCanvasOnCamera(Canvas canvas, Camera cam, int w, int h)
        {
            if (canvas == null || cam == null) return;

            _activeCaptureCanvas = canvas;
            _activeCaptureCanvasPrevMode = canvas.renderMode;
            _activeCaptureCanvasPrevWorldCam = canvas.worldCamera;
            _activeCaptureCanvasPrevPlaneDistance = canvas.planeDistance;
            _activeCaptureCanvasPrevSortingOrder = canvas.sortingOrder;
            _activeCaptureCanvasPrevScaleFactor = canvas.scaleFactor;
            _activeCaptureCanvasPrevLayer = canvas.gameObject.layer;

            // MV-444: this capture camera sits at the scene's default transform (world origin), inside
            // the 3D scene — it must render ONLY the canvas, nothing else, or nearby 3D geometry
            // Z-fights with (and shows through) the canvas's own opaque backdrop. Culling by the
            // canvas's EXISTING layer isn't enough: neither WeaponsScreen's nor HudController's canvas
            // is ever put on a dedicated layer (no GameObject in either Build() sets .layer), so they
            // sit on layer 0 ("Default") along with the entire 3D world — culling to that layer renders
            // the world right along with the UI (caught live running this exact ticket: probe6 sampled
            // #5E442D, real ground/prop colour, instead of the board's own near-black base). Moving the
            // canvas onto the pre-existing, otherwise-unused "UI" layer (TagManager.asset — never
            // referenced by anything else in this codebase, confirmed by grep) and culling to exactly
            // that layer is what actually isolates it. Restored in RestoreCanvas like every other
            // snapshot here.
            int ui = LayerMask.NameToLayer("UI");
            if (ui >= 0)
            {
                canvas.gameObject.layer = ui;
                cam.cullingMask |= (1 << ui);
            }
            else
            {
                // No "UI" layer in this project's TagManager — fall back to whatever layer the canvas
                // is already on. Not isolated from the 3D scene if that layer is shared, but still
                // better than rendering nothing.
                LogWarn("ShowCanvasOnCamera: no 'UI' layer in TagManager — falling back to the canvas's own layer, capture may show the 3D scene behind it");
                cam.cullingMask |= (1 << canvas.gameObject.layer);
            }

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;

            _activeCaptureScaler = canvas.GetComponent<CanvasScaler>();
            if (_activeCaptureScaler != null)
            {
                _activeCaptureScalerWasEnabled = _activeCaptureScaler.enabled;
                _activeCaptureScaler.enabled = false;
                canvas.scaleFactor = ComputeScaleFactor(_activeCaptureScaler, w, h);
            }
        }

        /// <summary>Undo <see cref="ShowCanvasOnCamera"/> — safe to call even if nothing is showing.</summary>
        private void RestoreCanvas()
        {
            if (_activeCaptureCanvas == null) return;
            _activeCaptureCanvas.renderMode = _activeCaptureCanvasPrevMode;
            _activeCaptureCanvas.worldCamera = _activeCaptureCanvasPrevWorldCam;
            _activeCaptureCanvas.planeDistance = _activeCaptureCanvasPrevPlaneDistance;
            _activeCaptureCanvas.sortingOrder = _activeCaptureCanvasPrevSortingOrder;
            _activeCaptureCanvas.scaleFactor = _activeCaptureCanvasPrevScaleFactor;
            _activeCaptureCanvas.gameObject.layer = _activeCaptureCanvasPrevLayer;
            if (_activeCaptureScaler != null) _activeCaptureScaler.enabled = _activeCaptureScalerWasEnabled;
            _activeCaptureCanvas = null;
            _activeCaptureScaler = null;
        }

        /// <summary>Reimplements <c>CanvasScaler</c>'s "Scale With Screen Size" / match-width-or-height
        /// math (Unity's own documented log-lerp formula) against an explicit (w, h) instead of
        /// <c>Screen.width</c>/<c>Screen.height</c>. Public and pure so it's pinned by an EditMode test
        /// without building a canvas. Expand/Shrink match modes aren't used by any screen today; they
        /// fall back to the tighter/looser axis respectively, same as CanvasScaler itself.</summary>
        public static float ComputeScaleFactor(CanvasScaler scaler, int w, int h)
        {
            if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return 1f;
            Vector2 refRes = scaler.referenceResolution;
            if (scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand)
                return Mathf.Min(w / refRes.x, h / refRes.y);
            if (scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Shrink)
                return Mathf.Max(w / refRes.x, h / refRes.y);
            float logW = Mathf.Log(w / refRes.x, 2f);
            float logH = Mathf.Log(h / refRes.y, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(logW, logH, scaler.matchWidthOrHeight));
        }

        /// <summary>Render <paramref name="cam"/> (already pointed at <paramref name="rt"/>, sized
        /// <paramref name="w"/>x<paramref name="h"/>) and read it back into an RGB PNG-ready texture — a
        /// RenderTexture render, never the back buffer, so it works with no attached display (MV-444).
        /// Caller owns both the returned texture and <paramref name="rt"/> itself (assigning it early,
        /// before <see cref="CaptureFixtureScreen"/>'s settle-frame yields, is what makes the canvas
        /// actually size itself for this render target — see the call site).</summary>
        private static Texture2D ReadCameraRenderTexture(Camera cam, RenderTexture rt, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var prevActive = RenderTexture.active;
            try
            {
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                return tex;
            }
            finally { RenderTexture.active = prevActive; }
        }

        /// <summary>Asserts exactly <paramref name="expectedCount"/> <c>ScreenSpaceOverlay</c> canvases
        /// above the HUD's own sortingOrder=100 are actually rendering something — the generalised form
        /// of the HomeScreen check MV-441 asked for, so a future screen with a higher sorting order
        /// breaks this job loudly instead of silently poisoning the evidence the same way again.
        ///
        /// "Rendering something" (<see cref="HasVisibleContent"/>), not merely "GameObject active", is
        /// the right test: WeaponsScreen/UpgradeScreen/SettingsPanel each self-install their Canvas once
        /// at boot and represent Close() by <c>SetActive(false)</c> on an inner content root, not by
        /// deactivating the Canvas GameObject itself — so their Canvas reads <c>isActiveAndEnabled</c>
        /// forever, open or closed, and a naive active-canvas count found 3 "active" canvases on every
        /// single shot including the ones that open nothing (caught live running this fix — see the
        /// MV-441 fix comment). An empty, contentless canvas draws nothing and isn't an occlusion risk;
        /// only a canvas with an active, non-transparent <see cref="Graphic"/> under it actually paints
        /// over the frame.
        ///
        /// Returns null on success, a named error (every offending canvas + its sortingOrder) on
        /// failure.</summary>
        private static string CheckSingleActiveOverlay(int expectedCount)
        {
            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var offenders = new List<string>();
            foreach (var c in canvases)
            {
                if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                if (c.sortingOrder <= 100) continue;
                if (!c.isActiveAndEnabled) continue;
                if (!HasVisibleContent(c)) continue;
                offenders.Add($"{c.name}(order={c.sortingOrder})");
            }
            if (offenders.Count == expectedCount) return null;
            return $"expected {expectedCount} visibly-rendering ScreenSpaceOverlay canvas(es) with sortingOrder>100, found {offenders.Count} [{string.Join(", ", offenders)}]";
        }

        /// <summary>A canvas above sortingOrder 100 is only an occlusion risk if something on it
        /// actually covers a meaningful share of the frame — a full-screen scrim/panel, the HomeScreen
        /// defect's own shape. <c>SettingsPanel</c> keeps a permanent 96x96 gear button on its own
        /// sortingOrder=200 canvas (0.4% of a 1920x1080 frame) so it's reachable from any screen; that
        /// is real, intentional always-on chrome, the same category as the HUD's own excluded
        /// sortingOrder=100 canvas, not a defect — flagging it was a real false-positive caught live
        /// running this fix (see the MV-441 fix comment). <c>GetComponentsInChildren</c> with
        /// <c>includeInactive: false</c> already skips every <see cref="Graphic"/> under an inactive
        /// ancestor, which is exactly how each self-installing screen represents "closed" — no
        /// reflection into any screen's private root needed.</summary>
        private const float OcclusionAreaFraction = 0.10f;   // a modal panel/scrim clears this easily; an icon/badge never does

        private static bool HasVisibleContent(Canvas c)
        {
            var canvasRect = c.transform as RectTransform;
            float canvasArea = canvasRect != null ? canvasRect.rect.width * canvasRect.rect.height : 0f;
            if (canvasArea <= 0f) return false;

            foreach (var g in c.GetComponentsInChildren<Graphic>(false))
            {
                if (!g.enabled || g.color.a <= 0.001f) continue;
                Rect r = g.rectTransform.rect;
                if (r.width * r.height >= canvasArea * OcclusionAreaFraction) return true;
            }
            return false;
        }

        /// <summary>MV-421 pixel probe 6, fixed by MV-441 (it never ran — this is its first
        /// implementation): a point that sits left of the region rect's own <c>padX</c> margin and
        /// clear of every category/ability node must equal <see cref="WeaponsScreen.Background"/>'s
        /// colours.base <em>as actually composited with <see cref="WeaponsScreen.ScreenScrim"/></em> — not
        /// raw colours.base, which is what MV-433 shipped before MV-433 itself added that scrim over the
        /// whole root. Reading raw colours.base here is exactly the kind of check this probe exists to
        /// replace: a hand-written expectation that quietly stopped matching what actually renders,
        /// caught live running this exact ticket (rig-16x9's real corner pixel is #000000 — the scrim's
        /// own colour at 97% alpha over a near-black backdrop rounds an 8-bit channel straight to 0 — not
        /// the #07080B this probe demanded before the fix). Runs only against <c>rig-16x9</c>, the one
        /// shot whose 1920x1080 texture maps 1:1 onto rig_board.json's own canvas coordinates with no
        /// letterbox/scale to account for.</summary>
        private void RunOutsideBackgroundProbe(Texture2D tex)
        {
            const int probeJsonX = 20, probeJsonY = 400;   // rig_board.json coords (y measured from the top)
            int texX = probeJsonX;
            int texY = tex.height - probeJsonY;             // Texture2D is bottom-left origin
            if (texX < 0 || texX >= tex.width || texY < 0 || texY >= tex.height)
            {
                string skip = "probe6 (outside-background==composited-base): SKIPPED — coordinates fall outside the captured texture";
                LogWarn(skip);
                _manifest.AppendLine(skip);
                return;
            }

            Color expected = ComputeCompositedBackground(FindFirstObjectByType<WeaponsScreen>());
            Color actual = tex.GetPixel(texX, texY);
            bool pass = ColorsMatch(actual, expected);
            string msg = $"probe6 (outside-background==composited-base): {(pass ? "PASS" : "FAIL")} — expected {ColorHex(expected)}, got {ColorHex(actual)} at tex({texX},{texY})";
            _manifest.AppendLine(msg);
            if (pass) { Log(msg); }
            else { LogWarn(msg); _failures.Add(msg); }
        }

        private static bool ColorsMatch(Color a, Color b, float tolerance = 2f / 255f)
        {
            return Mathf.Abs(a.r - b.r) <= tolerance
                && Mathf.Abs(a.g - b.g) <= tolerance
                && Mathf.Abs(a.b - b.b) <= tolerance;
        }

        private static string ColorHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        /// <summary>colours.base, composited with <see cref="WeaponsScreen.ScreenScrim"/> the same way
        /// <see cref="RunOutsideBackgroundProbe"/> always has — factored out so
        /// <see cref="RunConformanceChecks"/> can use the same "what does empty board actually look
        /// like" reference instead of re-deriving it. Only correct OUTSIDE any category's own region
        /// panel (which is exactly where probe 6 samples) — see <see cref="SampleCategoryBackground"/>
        /// for the reference every other check (which samples INSIDE a column) actually needs.</summary>
        private static Color ComputeCompositedBackground(WeaponsScreen weapons)
        {
            Color expected = RigBoardLayout.Colour("base");
            if (weapons != null && weapons.ScreenScrim != null)
            {
                Color scrim = weapons.ScreenScrim.color;
                expected = Color.Lerp(expected, new Color(scrim.r, scrim.g, scrim.b, 1f), scrim.a);
            }
            return expected;
        }

        /// <summary>MV-481: every node-shaped check below samples WELL inside its own category's panel,
        /// which <see cref="WeaponsScreen.RefreshCategoryNode"/> paints with its own family-tinted wash
        /// (<c>regionRect.opacityLit</c>/<c>opacityDark</c>) on top of colours.base+scrim — a real,
        /// visible tint (confirmed live: a clear point inside a lit panel reads (10,5,21), not the
        /// (0,0,0) probe 6 measures just outside every panel), and omitting it from the "expected
        /// background" was the actual root cause behind MV-481's hex-orientation and glow-containment
        /// FAILs: every ray/annulus sample landing inside a lit column read as "ink" relative to a
        /// reference that never accounted for the column's own tint, so a search bounded at a modest px
        /// cap could never find genuine background and either hit its own bound (hex-orientation) or
        /// scored near 100% ink (glow-containment) — regardless of how far the glow itself actually
        /// reached.
        ///
        /// This samples the real answer straight from the just-captured texture instead of predicting
        /// it: a first attempt that recomposited the wash via <c>Color.Lerp</c> (matching how
        /// <see cref="ComputeCompositedBackground"/> already handles the scrim) produced the exact same
        /// FAIL numbers as before, byte for byte — this project's Linear-space rendering + the capture
        /// texture's own sRGB read/write round-trip (see <c>rig_board.json</c>'s own regionRect comment,
        /// and <see cref="CheckCategoryColour"/>'s doc comment for why brightness prediction already
        /// lost to this once) makes a predicted composite unreliable here too. 110px right of the node's
        /// own centre is comfortably past its own ink (glow+outer ring never reach past ~86px at
        /// RadiusCategory=72) and short of the next column (categories sit >=360px apart); at the SAME y
        /// as the node itself, before any connector curve starts (they only begin ~88px below a category
        /// — see <c>connector.startOffsetCategory</c>), so this point is clear of every node and every
        /// connector for every fixture, by construction of the board's own fixed layout.</summary>
        private static Color SampleCategoryBackground(Texture2D tex, IReadOnlyList<RigCategoryLayout> categories, string categoryId,
            RigBoardConformance.BoardPixelTransform transform)
        {
            foreach (var cat in categories)
                if (cat.Id == categoryId) return RigBoardConformance.GetJsonPixel(tex, cat.X + 110f, cat.Y, transform);
            return RigBoardLayout.Colour("base");
        }

        // MV-499: inset from the raw column edge before clamping — the panel itself carries a visible
        // border stroke (regionRect.borderAlphaLit, several times stronger than the fill wash) right at
        // that edge, which is real, permanent, non-glow ink that belongs to the column, not a glow
        // bleeding out of it. Landing the clamp exactly on the edge still let that border count as
        // "ink" for whichever node's own checked radius happened to reach it (confirmed live: u_dmg
        // dropped from 50.8% to 25.9% once the neighbouring-column crossing was clamped out, but stayed
        // just over 25% until this margin also cleared its own column's border). 20px clears the border
        // and its antialiasing with room to spare, and is safe for every column: the narrowest possible
        // column (a single childless ability) still has ColumnHalfWidth >= RadiusCategory (72).
        private const float ColumnEdgeMargin = 20f;

        /// <summary>MV-499: a category's own column bounds, inset by <see cref="ColumnEdgeMargin"/> —
        /// the glow-containment check clamps its annulus sampling to this range so it never crosses into
        /// a neighbouring family's own column (see <see cref="RigBoardConformance.AnnulusInkFraction"/>'s
        /// own doc comment for why that crossing reads as false ink, not real bleed). Falls back to the
        /// whole canvas if the category can't be found, matching every other unbounded caller.</summary>
        private static void ColumnBounds(IReadOnlyList<RigCategoryLayout> categories, string categoryId, out float xMin, out float xMax)
        {
            foreach (var cat in categories)
                if (cat.Id == categoryId)
                {
                    xMin = cat.X - cat.ColumnHalfWidth + ColumnEdgeMargin;
                    xMax = cat.X + cat.ColumnHalfWidth - ColumnEdgeMargin;
                    return;
                }
            xMin = float.NegativeInfinity;
            xMax = float.PositiveInfinity;
        }

        // --- MV-463 Part 2 / MV-480: conformance pass --------------------------------------------
        // Reads rig_board.json (via RigBoardLayout) and asserts a just-captured texture actually
        // matches it — the harness's own eyes, not just its camera. MV-480: runs against all three
        // of rig-16x9, rig-phone and rig-ipad-mini (rig-16x10 and the mv472/lowcells/freshrun/noparts
        // fixture variants are not in that list — see the call site). Every check below is expressed
        // purely in rig_board.json's own (unscaled) coordinate space; RigBoardConformance.BoardPixelTransform
        // is what maps a json coordinate onto THIS capture's actual pixel, so nothing here needs to
        // know whether it's looking at an identity 1:1 frame (rig-16x9) or a scaled/cropped/offset one.

        private const float InkTolerance = 0.03f;       // RigBoardConformance.ColorDistance units (sum of |dr|+|dg|+|db|) — the colour-probe hue check (below) found real, hue-distinguishable fill several pixels out that a 0.05 magnitude floor was missing
        private const float HueProbeTolerance = 0.18f;   // RigBoardConformance.HueDistance units — hue direction, not brightness (see that method's own doc comment)

        // MV-480: BuildPhoneScrollViewport's own two constants (WeaponsScreen.cs, private there) —
        // duplicated here because the harness has no reach into that private method, same idiom the
        // geometry section of this ticket's own brief already uses. The VIEWPORT (what a static,
        // scrolled-to-top screenshot can actually show) is shorter than the scrollable CONTENT
        // (RigBoardLayout.PhoneContentHeight) — see PhoneFoldLimit's own doc comment.
        private const float PhoneViewportTop = 140f, PhoneViewportBottom = 1050f;

        /// <summary>MV-480: content-local y beyond which a phone-mode node is genuinely below the fold
        /// of a single scrolled-to-top screenshot — real, RectMask2D-clipped, not rendered in THIS
        /// capture at all (the player reaches it by scrolling; a static shot never will). The five
        /// PIXEL-sampling checks (node-position, hex-orientation, glow-containment, colour-probes, and
        /// glyph-height) must skip a node this affects rather than report a false FAIL for content that
        /// was never a rendering defect — see each check's own use of this. The geometry-only
        /// y-bounds-containment check (feature 3) deliberately does NOT use this: it checks against the
        /// full scrollable content instead, per this ticket's own phone exception.</summary>
        private const float PhoneFoldLimit = PhoneViewportBottom - PhoneViewportTop;   // 910

        /// <summary>MV-480: builds the json-coordinate-space -> pixel-space mapping this capture's own
        /// aspect/mode needs (see <see cref="RigBoardConformance.BoardPixelTransform"/>'s own doc
        /// comment for the affine math) — standard mode (rig-16x9, rig-ipad-mini) scales around the
        /// board's own centre (960, 540) by <see cref="WeaponsScreen.ComputeBoardScale"/> and crops by
        /// <see cref="WeaponsScreen.VisibleRefXWindow"/>; phone mode never scales (verified by
        /// <c>RigBoardChromeTests.EveryTappableHexClearsFortyFourPointsAndEveryGlyphClearsElevenPointsAtThePhoneAspect</c>'s
        /// own fixture assumption) but offsets y by the scroll viewport's own top
        /// (<see cref="PhoneViewportTop"/> — see <c>WeaponsScreen.BuildPhoneScrollViewport</c>'s own doc
        /// comment for why content-local y=0 lands at board-frame y=140, not 0).</summary>
        private static RigBoardConformance.BoardPixelTransform BuildTransform(float aspect, bool phoneMode, int h)
        {
            float pxPerRefUnit = h / 1080f;
            float scale = phoneMode ? 1f : WeaponsScreen.ComputeBoardScale(aspect);
            float offsetX = phoneMode ? 0f : 960f * (1f - scale);
            float offsetY = phoneMode ? PhoneViewportTop : 540f * (1f - scale);
            float windowMinX = WeaponsScreen.VisibleRefXWindow(aspect).MinX;
            return new RigBoardConformance.BoardPixelTransform(scale, offsetX, offsetY, windowMinX, pxPerRefUnit);
        }

        private void RunConformanceChecks(Texture2D tex, string aspectName, int w, int h)
        {
            float aspect = (float)w / h;
            bool phoneMode = WeaponsScreen.IsPhoneLayout(aspect);
            var transform = BuildTransform(aspect, phoneMode, h);

            var categories = phoneMode ? RigBoardLayout.PhoneCategories : RigBoardLayout.Categories;
            var abilities = phoneMode ? RigBoardLayout.PhoneAbilities : RigBoardLayout.Abilities;
            var fusions = phoneMode ? RigBoardLayout.PhoneFusions : RigBoardLayout.Fusions;
            float radiusCategory = phoneMode ? RigBoardLayout.RadiusCategoryPhone : RigBoardLayout.RadiusCategory;
            float radiusAbility = phoneMode ? RigBoardLayout.RadiusAbilityPhone : RigBoardLayout.RadiusAbility;
            float radiusFusion = phoneMode ? RigBoardLayout.RadiusFusionPhone : RigBoardLayout.RadiusFusion;
            float categoryLabelFontSize = phoneMode ? RigBoardLayout.CategoryLabelFontSizePhone : RigBoardLayout.CategoryLabelFontSize;

            var weapons = FindFirstObjectByType<WeaponsScreen>();
            Color background = ComputeCompositedBackground(weapons);
            var lines = new List<string>();
            int passCount = 0, checkCount = 0;

            void Emit(string name, bool pass, string detail)
            {
                checkCount++;
                lines.Add(RigBoardConformance.PassFailLine(name, pass, detail));
                if (pass) passCount++;
                else _failures.Add($"conformance/{aspectName}/{name}: {detail}");
            }

            // 1. Node position — every category/ability node's own json (x, y) must not read as background.
            var missing = new List<string>();
            int totalNodes = 0, positionSkipped = 0;
            foreach (var cat in categories)
                CheckNodePresent(tex, SampleCategoryBackground(tex, categories, cat.Id, transform), cat.Id, cat.X, cat.Y, missing, ref totalNodes, transform, skip: false, ref positionSkipped);
            foreach (var ab in abilities)
                CheckNodePresent(tex, SampleCategoryBackground(tex, categories, ab.Category, transform), ab.Id, ab.X, ab.Y, missing, ref totalNodes, transform,
                    skip: phoneMode && (ab.Y + radiusAbility) > PhoneFoldLimit, ref positionSkipped);
            string firstFive = missing.Count == 0 ? "" : string.Join(", ", missing.GetRange(0, Mathf.Min(5, missing.Count)));
            string foldNote = positionSkipped > 0 ? $" ({positionSkipped} below the fold, skipped)" : "";
            Emit("node-position", missing.Count == 0,
                (missing.Count == 0 ? $"{totalNodes}/{totalNodes} nodes present at their json coordinate"
                                    : $"{missing.Count}/{totalNodes} missing — first 5: {firstFive}") + foldNote);

            // 2. Hexagon orientation — categories only; see CheckHexOrientation's own doc comment for
            // why ability nodes (most of which have an incoming connector arriving from directly above)
            // aren't a trustworthy ray-march target the same way. Categories sit at the top of every
            // mode's own row schedule, so the phone fold never actually excludes one — no skip needed.
            var ratioFails = new List<string>();
            int hexChecked = 0;
            foreach (var cat in categories)
            { hexChecked++; CheckHexOrientation(tex, SampleCategoryBackground(tex, categories, cat.Id, transform), cat.Id, cat.X, cat.Y, radiusCategory, ratioFails, transform); }
            Emit("hex-orientation", ratioFails.Count == 0,
                ratioFails.Count == 0 ? $"{hexChecked}/{hexChecked} nodes at width/height ratio 0.866 +/-0.05"
                                       : $"{ratioFails.Count}/{hexChecked} off-ratio — {string.Join("; ", ratioFails)}");

            // 3. Family contrast — mean luminance of a lit category's own column band vs an unlit one.
            CheckFamilyContrast(tex, categories, abilities, transform, out float contrastRatio, out float litMean, out float unlitMean);
            Emit("family-contrast", contrastRatio >= 1.5f,
                $"lit={RigBoardConformance.Fmt(litMean)} unlit={RigBoardConformance.Fmt(unlitMean)} ratio={RigBoardConformance.Fmt(contrastRatio)} (need >=1.5)");

            // 4. Glow containment — every currently-lit/owned node's halo must fade out before 1.95r.
            var glowFails = new List<string>();
            int glowChecked = 0, glowSkipped = 0;
            foreach (var cat in categories)
            {
                bool lit = false;
                foreach (var ab in abilities) if (ab.Category == cat.Id && RigState.IsOwned(ab.Id)) { lit = true; break; }
                if (!lit) continue;
                if (phoneMode && (cat.Y + radiusCategory * 1.95f) > PhoneFoldLimit) { glowSkipped++; continue; }
                glowChecked++;
                Color catBackground = SampleCategoryBackground(tex, categories, cat.Id, transform);
                ColumnBounds(categories, cat.Id, out float catColumnMin, out float catColumnMax);
                float frac = RigBoardConformance.AnnulusInkFraction(tex, cat.X, cat.Y, radiusCategory * 1.25f, radiusCategory * 1.95f, catBackground, InkTolerance,
                    xMin: catColumnMin, xMax: catColumnMax, transform: transform);
                if (frac > 0.25f) glowFails.Add($"{cat.Id} {RigBoardConformance.Fmt(frac * 100f)}%");
            }
            foreach (var ab in abilities)
            {
                if (!RigState.IsOwned(ab.Id)) continue;
                if (phoneMode && (ab.Y + radiusAbility * 1.95f) > PhoneFoldLimit) { glowSkipped++; continue; }
                glowChecked++;
                Color abBackground = SampleCategoryBackground(tex, categories, ab.Category, transform);
                ColumnBounds(categories, ab.Category, out float abColumnMin, out float abColumnMax);
                float frac = RigBoardConformance.AnnulusInkFraction(tex, ab.X, ab.Y, radiusAbility * 1.25f, radiusAbility * 1.95f, abBackground, InkTolerance,
                    xMin: abColumnMin, xMax: abColumnMax, transform: transform);
                if (frac > 0.25f) glowFails.Add($"{ab.Id} {RigBoardConformance.Fmt(frac * 100f)}%");
            }
            string glowFoldNote = glowSkipped > 0 ? $" ({glowSkipped} below the fold, skipped)" : "";
            Emit("glow-containment", glowFails.Count == 0,
                (glowChecked == 0 ? "no lit/owned node to measure"
                : glowFails.Count == 0 ? $"{glowChecked}/{glowChecked} owned nodes under 25% annulus ink"
                                        : $"{glowFails.Count}/{glowChecked} over 25% — {string.Join("; ", glowFails)}") + glowFoldNote);

            // 5. Named colour probes — each family's category fill, the CELLS chip border, the PARTS tray border.
            var colourFails = new List<string>();
            int colourChecked = 0;
            foreach (var cat in categories)
            {
                colourChecked++;
                CheckCategoryColour(tex, background, cat, radiusCategory, colourFails, transform);
            }
            if (weapons != null)
            {
                colourChecked += 2;
                CheckChipBorderColour(tex, weapons.CellsBorder, "CELLS border", RigBoardLayout.Colour("sec"), colourFails);
                CheckChipBorderColour(tex, weapons.PartsBorder, "PARTS border", RigBoardLayout.Colour("part"), colourFails);
            }
            Emit("colour-probes", colourFails.Count == 0,
                colourFails.Count == 0 ? $"{colourChecked}/{colourChecked} sampled points match rig_board.json"
                                        : $"{colourFails.Count}/{colourChecked} off — {string.Join("; ", colourFails)}");

            // 6. MV-480: minimum rendered glyph height — every category label's own measured vertical
            // ink extent against a floor of half its authored font size at this capture's own pixel
            // scale. See CheckGlyphHeight's own doc comment for the measurement method and why 50%.
            CheckGlyphHeight(tex, categories, radiusCategory, categoryLabelFontSize, transform, out int glyphChecked, out var glyphFails);
            Emit("glyph-height", glyphFails.Count == 0,
                glyphFails.Count == 0 ? $"{glyphChecked}/{glyphChecked} category labels clear {RigBoardConformance.Fmt(categoryLabelFontSize * 0.5f)}px-at-ref floor"
                                       : $"{glyphFails.Count}/{glyphChecked} under floor — {string.Join("; ", glyphFails)}");

            // 7. MV-480: Y-axis bounds containment — every node (and, unlike the other checks above,
            // fusions too) must sit within the frame a given mode can actually show: standard mode has
            // no scroll cushion worth trusting past the visible [0, 1080] frame, so overrun there is
            // genuine clipping; phone mode's own scrollable CONTENT rect
            // (RigBoardLayout.PhoneContentHeight) is the right bound instead of the shorter viewport,
            // since scrolled-out content is reachable, not clipped — the same exception
            // RigBoardChromeTests' own Y term now encodes. Pure geometry, no pixel sampling — mirrors
            // that EditMode test's own math for this one capture's aspect instead of every tested one.
            CheckYBoundsContainment(phoneMode, transform.Scale, categories, abilities, fusions, radiusCategory, radiusAbility, radiusFusion,
                out int yChecked, out var yFails);
            float yMax = phoneMode ? RigBoardLayout.PhoneContentHeight : 1080f;
            Emit("y-bounds-containment", yFails.Count == 0,
                yFails.Count == 0 ? $"{yChecked}/{yChecked} nodes within [0,{RigBoardConformance.Fmt(yMax)}] {(phoneMode ? "scroll content" : "visible frame")}"
                                   : $"{yFails.Count}/{yChecked} overrun — {string.Join("; ", yFails)}");

            Log($"conformance/{aspectName}: {passCount}/{checkCount} check families passed");
            _manifest.AppendLine($"-- conformance: {aspectName} ({w}x{h}) --");
            foreach (var l in lines) _manifest.AppendLine(l);
            WriteConformanceReport(aspectName, w, h, lines);
        }

        private const int NodePresentHalfBlock = 8;   // a 17x17 neighbourhood — a single centre pixel (or even a 9x9 block) routinely lands in a gap in the icon's own sparse stroke art

        private static void CheckNodePresent(Texture2D tex, Color background, string id, float x, float y,
            List<string> missing, ref int total, RigBoardConformance.BoardPixelTransform transform, bool skip, ref int skipped)
        {
            if (skip) { skipped++; return; }
            total++;
            if (!RigBoardConformance.BlockHasInk(tex, x, y, NodePresentHalfBlock, background, InkTolerance, transform))
                missing.Add(id);
        }

        /// <summary>Categories only — every category's UP/LEFT/RIGHT rays are clean of any tree
        /// connector (connectors only ever run out of a category downward, toward its own tier1
        /// children), so the ratio measured here is trustworthy. Deliberately does NOT extend to
        /// ability nodes: most owned ability nodes have an INCOMING connector arriving from directly
        /// above (their own parent), which sits squarely in the UP ray's path and inflates the
        /// measured height — confirmed live running this exact check before it was scoped down to
        /// categories (several owned abilities read back a suspicious ratio of exactly 1.000, capped at
        /// this method's own maxDist in every direction). The hex rotation constant is shared by every
        /// node kind, so 5 categories is full coverage of the actual defect surface, not a sampling
        /// compromise.</summary>
        private static void CheckHexOrientation(Texture2D tex, Color background, string id, float cx, float cy, float r,
            List<string> fails, RigBoardConformance.BoardPixelTransform transform)
        {
            // Stays under geometry.connector.startOffsetCategory (88 at r=72) so an inward ray search
            // can never mistake a connector's own start pixel for the node's own hex/glow edge.
            float maxDist = Mathf.Min(r * 1.3f, r + RigBoardLayout.GlowBlurOwned);
            float top = RigBoardConformance.RayInkDistance(tex, cx, cy, 0f, -1f, maxDist, background, InkTolerance, transform);
            float left = RigBoardConformance.RayInkDistance(tex, cx, cy, -1f, 0f, maxDist, background, InkTolerance, transform);
            float right = RigBoardConformance.RayInkDistance(tex, cx, cy, 1f, 0f, maxDist, background, InkTolerance, transform);
            if (top <= 0f || left <= 0f || right <= 0f) { fails.Add($"{id} (no ink found within {RigBoardConformance.Fmt(maxDist)}px)"); return; }

            float height = 2f * top, width = left + right;
            float ratio = width / height;
            if (Mathf.Abs(ratio - 0.866f) > 0.05f) fails.Add($"{id} ratio={RigBoardConformance.Fmt(ratio)} (w={RigBoardConformance.Fmt(width)} h={RigBoardConformance.Fmt(height)})");
        }

        private static void CheckFamilyContrast(Texture2D tex, IReadOnlyList<RigCategoryLayout> categories, IReadOnlyList<RigAbilityLayout> abilities,
            RigBoardConformance.BoardPixelTransform transform, out float ratio, out float litMean, out float unlitMean)
        {
            int n = categories.Count;
            float yMin = RigBoardLayout.RegionRectY, yMax = yMin + RigBoardLayout.RegionRectH;
            var litVals = new List<float>();
            var unlitVals = new List<float>();

            // MV-472: a column's own half-width now varies with its content (RigBoardLayout.ColumnHalfWidth)
            // instead of a uniform 1/5 share, so SUPPORT genuinely carries more clear background around
            // its own nodes than a 2-node family like MOVE does — sampling a lit column's FULL width would
            // dilute its mean luminance by exactly how much extra room its own content earned it, which
            // is the fix working, not a contrast regression. Cap the sample band so every family reads a
            // comparably content-dense window regardless of its column's actual width.
            const float MaxSampleHalfWidth = 170f;

            for (int i = 0; i < n; i++)
            {
                float columnLeft = i == 0 ? categories[i].X - categories[i].ColumnHalfWidth : (categories[i - 1].X + categories[i].X) * 0.5f;
                float columnRight = i == n - 1 ? categories[i].X + categories[i].ColumnHalfWidth : (categories[i].X + categories[i + 1].X) * 0.5f;
                float left = Mathf.Max(columnLeft, categories[i].X - MaxSampleHalfWidth);
                float right = Mathf.Min(columnRight, categories[i].X + MaxSampleHalfWidth);

                bool lit = false;
                foreach (var ab in abilities) if (ab.Category == categories[i].Id && RigState.IsOwned(ab.Id)) { lit = true; break; }

                float mean = RigBoardConformance.MeanLuminance(tex, left, right, yMin, yMax, transform: transform);
                (lit ? litVals : unlitVals).Add(mean);
            }

            litMean = Average(litVals);
            unlitMean = Average(unlitVals);
            ratio = unlitMean > 0.0001f ? litMean / unlitMean : (litMean > 0.0001f ? 999f : 0f);
        }

        private static float Average(List<float> vals)
        {
            if (vals.Count == 0) return 0f;
            float sum = 0f;
            foreach (var v in vals) sum += v;
            return sum / vals.Count;
        }

        /// <summary>Samples a point r*0.7 either side of the category's own centre — inside the fill for
        /// ANY hex rotation (0.7r sits inside the 0.866r apothem every regular-hexagon rotation
        /// guarantees as its closest edge distance) and outside the icon's own bounding square
        /// (iconScaleCategory=1.2 -> half-width 0.6r), so this is deliberately robust to the exact
        /// pointy-top/flat-top defect MV-462 fixes rather than assuming it's already fixed.
        ///
        /// Compares by HUE direction (<see cref="RigBoardConformance.HueDistance"/>), not by predicting
        /// the exact composited brightness a low-alpha fill produces — this project's Linear colour
        /// space makes that composited brightness come out several times brighter than a naive
        /// <see cref="Color.Lerp"/> against colours.base predicts (see that method's own doc comment;
        /// confirmed live running this exact check against the real capture before switching off
        /// brightness matching).</summary>
        private static void CheckCategoryColour(Texture2D tex, Color background, RigCategoryLayout cat, float radiusCategory,
            List<string> fails, RigBoardConformance.BoardPixelTransform transform)
        {
            Color family = RigBoardLayout.Colour(cat.Family);
            float dx = radiusCategory * 0.7f;
            Color a1 = RigBoardConformance.GetJsonPixel(tex, cat.X + dx, cat.Y, transform);
            Color a2 = RigBoardConformance.GetJsonPixel(tex, cat.X - dx, cat.Y, transform);
            bool pass = RigBoardConformance.HueDistance(a1, family) <= HueProbeTolerance
                     || RigBoardConformance.HueDistance(a2, family) <= HueProbeTolerance;
            if (!pass) fails.Add($"{cat.Id} fill expected hue ~{RigBoardConformance.ColorHex(family)} got {RigBoardConformance.ColorHex(a1)}");
        }

        /// <summary>Samples the middle of a chip border's own left edge (guaranteed to land on the
        /// stroke, not the hollow centre a naive rect-centre sample would hit) via the same
        /// world-to-screen-point path every board node's json coordinate is deliberately NOT using —
        /// the CELLS/PARTS chips have no json (x, y) of their own, so this is the one probe that reads
        /// the built screen's actual RectTransform instead — already correct at every aspect/mode with
        /// no transform of its own needed, since <c>WorldToScreenPoint</c> reads wherever the chip is
        /// ACTUALLY built this capture, not a predicted json coordinate. Hue comparison, same reasoning
        /// as <see cref="CheckCategoryColour"/>.</summary>
        private void CheckChipBorderColour(Texture2D tex, Image border, string label, Color expectedNamed,
            List<string> fails)
        {
            if (border == null || _captureCam == null) { fails.Add($"{label} (no border image to sample)"); return; }
            Vector3 world = border.rectTransform.TransformPoint(new Vector3(-border.rectTransform.rect.width * 0.5f, 0f, 0f));
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(_captureCam, world);
            int x = Mathf.Clamp(Mathf.RoundToInt(screenPoint.x), 0, tex.width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(screenPoint.y), 0, tex.height - 1);
            Color actual = tex.GetPixel(x, y);
            if (RigBoardConformance.HueDistance(actual, expectedNamed) > HueProbeTolerance)
                fails.Add($"{label} expected hue ~{RigBoardConformance.ColorHex(expectedNamed)} got {RigBoardConformance.ColorHex(actual)}");
        }

        /// <summary>MV-480 feature 2: the rendered vertical ink extent of a category label — scans a
        /// horizontal strip <paramref name="halfWidth"/> either side of the label's own x, row by row
        /// through <paramref name="maxDistY"/> above/below its own y, and takes the span between the
        /// first and last row that found any pixel whose HUE matches the label's own known colour
        /// (<see cref="RigBoardLayout.Colour"/>'s <c>"ink"</c>, MV-480's <paramref name="inkColour"/>).
        /// Deliberately a bounding-box measurement, not <see cref="RigBoardConformance.RayInkDistance"/>'s
        /// outside-in single-ray walk — that primitive assumes one contiguous blob (right for a hex halo,
        /// wrong for text: a single x column routinely lands in the gap between two letters, or inside a
        /// hollow letter like "O", so a single ray can under-measure a real glyph's own height).
        ///
        /// Matching HUE against "differs from background" was tried first and rejected: a category label
        /// sits directly above where its own tree connector curves down toward its tier1 children (MV-443's
        /// own startOffsetCategory=88, well inside this scan's own maxDistY reach), and that connector runs
        /// vertically out of the SAME x the label is centred on — confirmed live running this exact
        /// ticket's own AC5 demo: shrinking the label's rendered font to 3px still measured a "PASS" glyph
        /// height, because the connector's own crossing satisfied a bare "any ink in this row" test
        /// regardless of whether the label itself rendered at all. A connector's ink is always the family
        /// colour (<see cref="RigBoardLayout.Colour(string)"/> for the category's own family) at low alpha;
        /// a label's ink is always the "ink" cream/white — hue-matching against the label's OWN known
        /// colour is what actually isolates it from anything else sharing its vertical band (connector,
        /// glow, panel tint), the same idiom <see cref="CheckCategoryColour"/> already uses for the fill
        /// itself.</summary>
        private static float MeasureVerticalInkExtentPx(Texture2D tex, float centerX, float centerY, float halfWidth, float maxDistY,
            Color inkColour, float hueTolerance, RigBoardConformance.BoardPixelTransform transform, int stepY = 2, int stepX = 4)
        {
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            for (float y = centerY - maxDistY; y <= centerY + maxDistY; y += stepY)
            {
                bool rowHasGlyphInk = false;
                for (float x = centerX - halfWidth; x <= centerX + halfWidth; x += stepX)
                {
                    Color px = RigBoardConformance.GetJsonPixel(tex, x, y, transform);
                    if (RigBoardConformance.HueDistance(px, inkColour) <= hueTolerance) { rowHasGlyphInk = true; break; }
                }
                if (rowHasGlyphInk)
                {
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (float.IsPositiveInfinity(minY)) return 0f;
            return (maxY - minY) * transform.Scale * transform.PixelsPerRefUnit;
        }

        /// <summary>MV-480 feature 2: every category label (5 — a representative, well-known-position
        /// sample; see this ticket's own brief for why category labels alone are enough) against a floor
        /// of 50% of its own authored font size at this capture's pixel scale — conservative enough to
        /// tolerate antialiasing/line-height without being a rubber floor, and must actually pass at the
        /// current (correct) rendering, a regression floor rather than a threshold hunt. Phone-fold-skips
        /// like the other pixel checks, though in practice every category sits at the top of both row
        /// schedules and never triggers it.</summary>
        private static void CheckGlyphHeight(Texture2D tex, IReadOnlyList<RigCategoryLayout> categories, float radiusCategory,
            float categoryLabelFontSize, RigBoardConformance.BoardPixelTransform transform, out int checkedCount, out List<string> fails)
        {
            fails = new List<string>();
            checkedCount = 0;
            float floorPx = categoryLabelFontSize * 0.5f * transform.Scale * transform.PixelsPerRefUnit;
            Color inkColour = RigBoardLayout.Colour("ink");
            foreach (var cat in categories)
            {
                float labelY = cat.Y + RigBoardLayout.CategoryLabelOffsetY(radiusCategory);
                checkedCount++;
                float halfWidth = Mathf.Min(cat.ColumnHalfWidth - 10f, 140f);
                float maxDistY = categoryLabelFontSize * 1.2f;
                float glyphHeightPx = MeasureVerticalInkExtentPx(tex, cat.X, labelY, halfWidth, maxDistY, inkColour, HueProbeTolerance, transform);
                if (glyphHeightPx < floorPx)
                    fails.Add($"{cat.Id} {RigBoardConformance.Fmt(glyphHeightPx)}px < {RigBoardConformance.Fmt(floorPx)}px floor");
            }
        }

        /// <summary>MV-480 feature 3: the harness's own copy of the Y-axis containment
        /// <c>RigBoardChromeTests.EveryNodeAndRegionPanelFitsInsideTheVisibleWindowAtEveryTestedAspect</c>'s
        /// own Y term now asserts — deliberately duplicated (not shared code) since one measures resolved
        /// layout values pre-render (EditMode) and this one is part of the rendered-pixel harness; see
        /// this ticket's own brief for why both exist. Standard mode's bound is the visible [0, 1080]
        /// frame; phone mode's is the scrollable CONTENT rect (<see cref="RigBoardLayout.PhoneContentHeight"/>),
        /// not the shorter viewport — scrolled-out content is reachable, not clipped.</summary>
        private static void CheckYBoundsContainment(bool phoneMode, float scale,
            IReadOnlyList<RigCategoryLayout> categories, IReadOnlyList<RigAbilityLayout> abilities, IReadOnlyList<RigFusionLayout> fusions,
            float radiusCategory, float radiusAbility, float radiusFusion, out int checkedCount, out List<string> fails)
        {
            const float boardCentreY = 540f;
            float yMax = phoneMode ? RigBoardLayout.PhoneContentHeight : 1080f;
            var localFails = new List<string>();
            int localChecked = 0;

            void Check(string id, float rawY, float halfExtent)
            {
                localChecked++;
                float top, bottom;
                if (phoneMode) { top = rawY - halfExtent; bottom = rawY + halfExtent; }
                else
                {
                    float scaledY = boardCentreY + (rawY - boardCentreY) * scale;
                    float scaledHalf = halfExtent * scale;
                    top = scaledY - scaledHalf; bottom = scaledY + scaledHalf;
                }
                if (top < -0.5f || bottom > yMax + 0.5f)
                    localFails.Add($"{id} top={RigBoardConformance.Fmt(top)} bottom={RigBoardConformance.Fmt(bottom)}");
            }

            foreach (var cat in categories) Check(cat.Id, cat.Y, radiusCategory);
            foreach (var ab in abilities) Check(ab.Id, ab.Y, radiusAbility);
            foreach (var fu in fusions) Check(fu.Id, fu.Y, radiusFusion);

            checkedCount = localChecked;
            fails = localFails;
        }

        /// <summary>MV-480: one growing report file across all three measured aspects instead of the
        /// old single-aspect overwrite — the first aspect measured this run starts the file fresh, every
        /// aspect after that appends its own labelled section, so <c>_uiscreens_report.txt</c> shows all
        /// three per AC1 instead of only ever the last one written.</summary>
        private readonly HashSet<string> _reportedAspects = new HashSet<string>();

        private void WriteConformanceReport(string aspectName, int w, int h, List<string> lines)
        {
            bool firstAspectThisRun = _reportedAspects.Count == 0;
            _reportedAspects.Add(aspectName);
            string section = $"== {aspectName} ({w}x{h}) ==\n" + string.Join("\n", lines) + "\n";
            const string reportFile = "_uiscreens_report.txt";
            WriteOrAppend(_outDir, reportFile, section, overwrite: firstAspectThisRun);
            if (_outDir2 != null) WriteOrAppend(_outDir2, reportFile, section, overwrite: firstAspectThisRun);
        }

        private void WriteOrAppend(string dir, string fileName, string section, bool overwrite)
        {
            string path = Path.Combine(dir, fileName);
            try
            {
                if (overwrite) File.WriteAllText(path, section, Encoding.UTF8);
                else File.AppendAllText(path, section, Encoding.UTF8);
            }
            catch (Exception e) { LogWarn($"conformance report write to {dir} failed — {e.Message}"); }
        }

        // --- MV-463 Part 3: design-vs-build contact sheet ----------------------------------------
        // "Assertions cannot judge whether an icon is well drawn." One image pairing every category
        // and ability node's own crop from MV-423.png against the same crop from the fresh rig-16x9
        // capture — the fixture is built to match MV-423.png's own state exactly (28/30 cells, 4
        // parts), so the two are directly comparable node-for-node.

        private const int ContactCropSize = 260;
        private const int ContactPairGap = 6;
        private const int ContactLabelH = 28;
        private const int ContactPad = 10;
        private const int ContactCols = 7;

        private static readonly string DesignImagePath = Path.Combine(@"C:\Dev\MaxVsTheWorlds-Images", "MV-423.png");

        private void BuildContactSheet(Texture2D buildTex)
        {
            Texture2D designTex = LoadDesignImage();
            if (designTex == null)
            {
                LogWarn($"contact sheet: MV-423.png not found/unreadable at {DesignImagePath} — skipped");
                return;
            }

            var nodes = new System.Collections.Generic.List<(string Id, float X, float Y)>();
            foreach (var cat in RigBoardLayout.Categories) nodes.Add((cat.Id, cat.X, cat.Y));
            foreach (var ab in RigBoardLayout.Abilities) nodes.Add((ab.Id, ab.X, ab.Y));

            int cols = ContactCols;
            int rows = Mathf.CeilToInt(nodes.Count / (float)cols);
            int cellW = ContactCropSize * 2 + ContactPairGap;
            int cellH = ContactCropSize + ContactLabelH;
            int sheetW = cols * (cellW + ContactPad) + ContactPad;
            int sheetH = rows * (cellH + ContactPad) + ContactPad;

            Color32[] designPixels = designTex.GetPixels32();
            Color32[] buildPixels = buildTex.GetPixels32();
            var sheet = new Color32[sheetW * sheetH];
            var sheetBg = new Color32(12, 13, 16, 255);
            for (int i = 0; i < sheet.Length; i++) sheet[i] = sheetBg;

            for (int i = 0; i < nodes.Count; i++)
            {
                int col = i % cols, row = i / cols;
                int cellX = ContactPad + col * (cellW + ContactPad);
                int cellY = ContactPad + row * (cellH + ContactPad);
                var node = nodes[i];
                int texCx = Mathf.RoundToInt(node.X);   // same json x for both images — canvases are the same 1920x1080 frame

                int designCy = designTex.height - Mathf.RoundToInt(node.Y);
                PasteCrop(sheet, sheetW, cellX, cellY + ContactLabelH, designPixels, designTex.width, designTex.height, texCx, designCy, ContactCropSize);

                int buildCy = buildTex.height - Mathf.RoundToInt(node.Y);
                PasteCrop(sheet, sheetW, cellX + ContactCropSize + ContactPairGap, cellY + ContactLabelH, buildPixels, buildTex.width, buildTex.height, texCx, buildCy, ContactCropSize);
            }

            var sheetTex = new Texture2D(sheetW, sheetH, TextureFormat.RGB24, false);
            sheetTex.SetPixels32(sheet);

            OverlayContactLabels(sheetTex, nodes, cols, cellW, cellH);

            try
            {
                byte[] png = sheetTex.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(_outDir, "rig-contact-sheet.png"), png);
                TryWriteSecondary("rig-contact-sheet", png);
                string line = $"rig-contact-sheet.png ({sheetW}x{sheetH}, {nodes.Count} node pairs)";
                Log($"wrote {line}");
                _manifest.AppendLine(line);
            }
            catch (Exception e) { LogWarn($"contact sheet: encode/write failed — {e.Message}"); }
            finally
            {
                Destroy(sheetTex);
                Destroy(designTex);
            }
        }

        private static Texture2D LoadDesignImage()
        {
            try
            {
                if (!File.Exists(DesignImagePath)) return null;
                byte[] bytes = File.ReadAllBytes(DesignImagePath);
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!tex.LoadImage(bytes)) { Destroy(tex); return null; }
                return tex;
            }
            catch (Exception e) { LogWarn($"contact sheet: failed to load {DesignImagePath} — {e.Message}"); return null; }
        }

        private static void PasteCrop(Color32[] dst, int dstW, int dstX, int dstY, Color32[] src, int srcW, int srcH,
            int centerX, int centerY, int size)
        {
            int half = size / 2;
            for (int y = 0; y < size; y++)
            {
                int sy = centerY - half + y;
                if (sy < 0 || sy >= srcH) continue;
                int dy = dstY + y;
                for (int x = 0; x < size; x++)
                {
                    int sx = centerX - half + x;
                    if (sx < 0 || sx >= srcW) continue;
                    dst[dy * dstW + (dstX + x)] = src[sy * srcW + sx];
                }
            }
        }

        /// <summary>Stamps each node's id above its crop pair. A throwaway canvas built and destroyed
        /// entirely within this method — deliberately NOT routed through <see cref="ShowCanvasOnCamera"/>/
        /// <see cref="RestoreCanvas"/> (those track exactly one active canvas in instance fields, and
        /// this runs WHILE the main board capture's own canvas is still active on the same camera,
        /// mid-<see cref="CaptureFixtureScreen"/> — sharing that single-slot state here would stomp the
        /// snapshot <see cref="CaptureFixtureScreen"/>'s own <c>finally</c> still needs to restore the
        /// board canvas afterward). Saves/restores exactly what it touches (culling mask, target
        /// texture, aspect) by hand instead.</summary>
        private void OverlayContactLabels(Texture2D sheetTex,
            System.Collections.Generic.List<(string Id, float X, float Y)> nodes, int cols, int cellW, int cellH)
        {
            if (_captureCam == null) return;
            int sheetW = sheetTex.width, sheetH = sheetTex.height;
            int uiLayer = LayerMask.NameToLayer("UI");

            var canvasGo = new GameObject("ContactSheetLabels");
            if (uiLayer >= 0) canvasGo.layer = uiLayer;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _captureCam;
            canvas.planeDistance = 1f;

            for (int i = 0; i < nodes.Count; i++)
            {
                int col = i % cols, row = i / cols;
                int cellX = ContactPad + col * (cellW + ContactPad);
                int cellY = ContactPad + row * (cellH + ContactPad);

                var textGo = new GameObject(nodes[i].Id, typeof(RectTransform), typeof(Text));
                if (uiLayer >= 0) textGo.layer = uiLayer;
                textGo.transform.SetParent(canvasGo.transform, false);
                var rt = (RectTransform)textGo.transform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(cellW, ContactLabelH);
                rt.anchoredPosition = new Vector2(cellX, -cellY);

                var text = textGo.GetComponent<Text>();
                text.font = HudFont.Get();
                text.text = nodes[i].Id;
                text.fontSize = 20;
                text.fontStyle = FontStyle.Bold;
                text.color = Color.white;
                text.alignment = TextAnchor.MiddleLeft;
                text.raycastTarget = false;
            }

            var rt2 = new RenderTexture(sheetW, sheetH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            int prevMask = _captureCam.cullingMask;
            var prevTarget = _captureCam.targetTexture;
            float prevAspect = _captureCam.aspect;
            _captureCam.targetTexture = rt2;
            _captureCam.aspect = (float)sheetW / sheetH;
            _captureCam.backgroundColor = Color.black;
            if (uiLayer >= 0) _captureCam.cullingMask |= (1 << uiLayer);

            // MV-444's own rule for a ScreenSpaceCamera canvas: geometry isn't trustworthy until
            // Canvas.SendWillRenderCanvases rebuilds it at the new target size. That method's own fix
            // yielded two frames to let Unity's own update loop get there; this call site can't yield
            // (it runs synchronously, deep inside CaptureFixtureScreen's own try/catch — CS1626), so it
            // forces the same rebuild immediately instead of waiting for one.
            Canvas.ForceUpdateCanvases();

            Texture2D labelsTex = ReadCameraRenderTexture(_captureCam, rt2, sheetW, sheetH);

            _captureCam.cullingMask = prevMask;
            _captureCam.targetTexture = prevTarget;
            _captureCam.aspect = prevAspect;
            rt2.Release();
            Destroy(rt2);
            Destroy(canvasGo);

            Color32[] labelPixels = labelsTex.GetPixels32();
            Color32[] sheetPixels = sheetTex.GetPixels32();
            int n = Mathf.Min(sheetPixels.Length, labelPixels.Length);
            for (int i = 0; i < n; i++)
            {
                Color32 lp = labelPixels[i];
                if (lp.r > 24 || lp.g > 24 || lp.b > 24) sheetPixels[i] = lp;   // label text over the sheet's own dark background
            }
            sheetTex.SetPixels32(sheetPixels);
            sheetTex.Apply();
            Destroy(labelsTex);
        }

        // --- lifecycle / reporting ----------------------------------------------------------------

        private void Finish()
        {
            // "ok" means every expected screenshot landed AND no assertion — the canvas-overlay check
            // or probe 6 — failed. A shot that "succeeded" over an occluding HomeScreen is exactly the
            // false green MV-441 exists to close off; a partial run (e.g. rig-16x9 skipped) is exactly
            // as false a green and must not read "ok" either (MV-444: caught live when a WeaponsScreen
            // build-order race dropped all 3 rig shots but the marker still said "ok" on the 4 that did
            // land).
            string status;
            if (_failures.Count > 0)
                status = $"fail: {_failures.Count} assertion(s) failed — " + string.Join(" | ", _failures);
            else if (_shotsWritten < ExpectedShotCount)
                status = $"fail: {_shotsWritten} of {ExpectedShotCount} screenshots captured";
            else
                status = "ok";

            string doneText = status + "\n" + _manifest.ToString();
            File.WriteAllText(Path.Combine(_outDir, DoneMarker), doneText, Encoding.UTF8);
            if (_outDir2 != null)
            {
                try { File.WriteAllText(Path.Combine(_outDir2, DoneMarker), doneText, Encoding.UTF8); }
                catch (Exception e) { LogWarn($"secondary done-marker write to {_outDir2} failed — {e.Message}"); }
            }
            Log($"ui-screens capture complete ({_shotsWritten}/{ExpectedShotCount} shots, {_failures.Count} failure(s))");
        }

        // MV-463 Part 1: THE RIG's own shot count is data-driven (RigBoardLayout.CaptureAspects) plus
        // the fixed noparts variant; WEAPONS button stays at its 4 fixed alert-state shots (MV-425)
        // plus MV-510's own 977x458 human-check shot.
        private static int ExpectedShotCount => RigBoardLayout.CaptureAspects.Count + 1 + 4 + 1;

        private static void Log(string m) => Debug.Log("[UiScreens] " + m);
        private static void LogWarn(string m) => Debug.LogWarning("[UiScreens] " + m);
    }
}
