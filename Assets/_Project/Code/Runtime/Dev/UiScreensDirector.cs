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

        /// <summary>Three shots: the 16:9 reference frame (1:1 comparable to MV-423.png), the 1.6:1
        /// narrowest-desktop-browser frame (does SUPPORT clip?), and a parts=0 variant of the 16:9
        /// frame (does the amber '+' badge / empty tray render correctly?).</summary>
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

            yield return CaptureFixtureScreen("rig-16x9", 1920, 1080, ApplyRigFixture, weapons.Open, weapons.Close, canvas);
            yield return CaptureFixtureScreen("rig-16x10", 1728, 1080, ApplyRigFixture, weapons.Open, weapons.Close, canvas);
            yield return CaptureFixtureScreen("rig-noparts-16x9", 1920, 1080, ApplyRigFixtureNoParts, weapons.Open, weapons.Close, canvas);
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

        private static void ResetRunForFixture()
        {
            RigState.Reset();
            PickupWallet.Reset();
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
                if (!RigState.TrySpendPart(id))
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

            ApplyWeaponsButtonIdleFixture();   // leave the scene in a clean state once the pass is done
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
        /// so a scaled wait would never elapse.</summary>
        private IEnumerator CaptureFixtureScreen(string name, int w, int h, Action applyFixture, Action open, Action close, Canvas canvas)
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

                    if (name == "rig-16x9") RunOutsideBackgroundProbe(tex);
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

            Color expected = RigBoardLayout.Colour("base");
            var weapons = FindFirstObjectByType<WeaponsScreen>();
            if (weapons != null && weapons.ScreenScrim != null)
            {
                Color scrim = weapons.ScreenScrim.color;
                expected = Color.Lerp(expected, new Color(scrim.r, scrim.g, scrim.b, 1f), scrim.a);
            }

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

        private const int ExpectedShotCount = 7;   // 3 THE RIG (MV-421) + 4 WEAPONS button states (MV-425)

        private static void Log(string m) => Debug.Log("[UiScreens] " + m);
        private static void LogWarn(string m) => Debug.LogWarning("[UiScreens] " + m);
    }
}
