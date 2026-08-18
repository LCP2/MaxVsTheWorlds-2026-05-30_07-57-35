using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.UI;
using MaxWorlds.Core;
using MaxWorlds.CameraRig;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.UI;
using MaxWorlds.Bosses;
using MaxWorlds.VFX;
using MaxWorlds.Upgrades;
using MaxWorlds.Weapons;
using MaxWorlds.Pickups;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// Renders the press-kit screenshots in play mode and writes them to disk (YT-97).
    ///
    /// The whole game is code-driven: the yard, Max's model, the boss's model, the HUD and every VFX
    /// are built at runtime by ~17 self-installing systems, so there is nothing to screenshot until the
    /// game is actually PLAYING. This director therefore lives in the runtime assembly and runs as a
    /// coroutine in play mode, staging each shot against the live scene.
    ///
    /// It is INERT in a normal session. It installs only when the process was launched to film — either
    /// the <c>-presskit</c> command-line flag (the automated editor run, see PressKitCapture) or a
    /// <c>Temp/presskit.arm</c> marker file (the in-editor menu item). With neither, Install() returns
    /// and the class never touches the game.
    ///
    /// Capture technique: it repositions <see cref="Camera.main"/> itself (with the CinemachineBrain
    /// disabled so the rig stops fighting it) and renders that camera into a RenderTexture. A manual
    /// Camera.Render() deliberately does NOT draw IMGUI (the FPS readout, the dev-mode box, the
    /// blaster's debug line) nor a ScreenSpaceOverlay canvas — so the hero shots come out clean, in the
    /// player build's look, for free. The one shot that WANTS the HUD flips the HUD canvas to
    /// ScreenSpace-Camera for the duration so it composites into the same render.
    /// </summary>
    public sealed class PressKitDirector : MonoBehaviour
    {
        // --- capture config -------------------------------------------------------------------
        private const int DefaultOutW = 2560;
        private const int DefaultOutH = 1440;
        private const int SuperSample = 2;             // render at 2x then downscale — clean AA regardless of URP MSAA
        private const float FramePitch = 72f;          // the game's fixed top-down angle
        private const string DoneMarker = "_done.txt";

        // --- ui-screens job config (MV-421) -----------------------------------------------------
        // The two reference frames a UI ticket needs evidence against: the canvas's own 1920x1080
        // design frame, and 1728x1080 (1.6:1) — the narrowest aspect a desktop browser realistically
        // presents, where a canvas using matchWidthOrHeight=1 (match by height) crops furthest.
        private static readonly (int w, int h, string suffix)[] UiScreenSizes =
        {
            (1920, 1080, "16x9"),
            (1728, 1080, "16x10"),
        };

        private string _outDir;
        private readonly StringBuilder _manifest = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Armed() && !UiScreensArmed()) return;
            if (FindFirstObjectByType<PressKitDirector>() != null) return;
            new GameObject("PressKitDirector").AddComponent<PressKitDirector>();
        }

        /// <summary>Only film when the process was explicitly launched to. A normal player or CI run
        /// trips neither of these and this whole system stays asleep. Public so the Home screen
        /// (YT-151) can skip its pick-a-slot modal during a capture run — filming can't click through it.</summary>
        public static bool Armed()
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "-presskit", StringComparison.OrdinalIgnoreCase)) return true;
            try { return File.Exists(Path.Combine("Temp", "presskit.arm")); }
            catch { return false; }
        }

        /// <summary>Only run the ui-screens job (MV-421) when the process was explicitly launched to —
        /// the <c>-uiscreens</c> flag or a <c>Temp/uiscreens.arm</c> marker, same idiom as
        /// <see cref="Armed"/> uses for the gameplay press-kit. Deliberately separate from
        /// <see cref="Armed"/>: this job wants to actually see the Home screen's pick-a-slot modal and
        /// the paused Rig board, not skip past them the way a gameplay film run does.</summary>
        public static bool UiScreensArmed()
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

        private void Start() => StartCoroutine(UiScreensArmed() ? RunUiScreens() : Run());

        private IEnumerator Run()
        {
            _outDir = Arg("-presskitOut") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press"));
            Directory.CreateDirectory(_outDir);
            Log($"press-kit capture starting → {_outDir}  ({DefaultOutW}x{DefaultOutH}, {SuperSample}x SSAA)");

            // Filming powers: keep Max alive through the boss and combat stages, and let the blaster
            // fire hands-free. The dev-mode OVERLAY is IMGUI, so it never lands in a Camera.Render() —
            // enabling dev mode here does not dirty a single shot.
            DevMode.Enabled = true;
            DevMode.Invincible = true;
            DevMode.InfiniteEnergy = true;
            DevMode.AutoFire = false;
            DevMode.PauseSpawns = false;

            var cam = Camera.main;
            if (cam == null) { Fail("no Camera.main in the scene"); yield break; }
            if (cam.TryGetComponent<CinemachineBrain>(out var brain)) brain.enabled = false;
            cam.aspect = (float)DefaultOutW / DefaultOutH;

            // Let the self-installing systems dress the world (materials, props, Max, HUD, lighting).
            for (int i = 0; i < 4; i++) yield return null;

            var max = GameObject.FindGameObjectWithTag("Player");
            if (max == null) { Fail("no Player-tagged Max in the scene"); yield break; }

            // 1 — the dressed arena, wide.
            yield return Frame(() =>
            {
                HideHud();
                Bounds arena = PlayAreaBounds(max.transform.position);
                PlaceOrbit(cam, arena.center, 66f, 0f, FitDistance(cam, Radius(arena) + 6f));
            }, cam, "01_arena_wide");

            // 2 — Max, close.
            yield return Frame(() =>
            {
                HideHud();
                Vector3 f = max.transform.position; f.y = 1.0f;
                PlaceOrbit(cam, f, FramePitch, 18f, 6.2f);   // a touch of yaw so he isn't dead-flat to camera
            }, cam, "02_max_closeup");

            // Let a few robots stream out of the factories so the map shot has life in it.
            yield return PopulateEnemies(6, waitFrames: 12);

            // 3 — the HUD + minimap, in context, over a live gameplay frame.
            yield return Frame(() =>
            {
                ShowHud(cam);
                PlaceOrbit(cam, max.transform.position, FramePitch, 0f, 25.1f); // the shipped gameplay framing
            }, cam, "03_hud_minimap");
            HideHud();

            // 4 — a combat moment: a knot of robots in front of Max, the blaster spraying them.
            yield return StageCombat(max.transform);
            yield return Frame(() =>
            {
                HideHud();
                Vector3 focus = max.transform.position + max.transform.forward * 3.5f;
                focus.y = 1f;
                PlaceOrbit(cam, focus, 62f, 0f, 15f);
            }, cam, "04_combat");
            DevMode.AutoFire = false;

            // 5 — Big Bermuda. Bring the boss out by clearing the factories, then frame it.
            yield return EngageBoss();
            var boss = FindFirstObjectByType<BigBermudaBoss>();
            yield return Frame(() =>
            {
                HideHud();
                Vector3 bp = boss != null ? boss.transform.position : max.transform.position;
                bp.y = 1f;
                PlaceOrbit(cam, bp, 60f, 0f, 16f);
            }, cam, "05_big_bermuda");

            // 6 — the upgrade-screen weapon render (YT-140), captured straight off its own stage camera.
            yield return CaptureUpgradeWeapon();

            Finish();
        }

        /// <summary>Render the upgrade-screen hero weapon (YT-140) — the base sprayer with a couple of
        /// parts already installed and a new one seated on — straight from <see cref="UpgradeWeaponStage"/>'s
        /// own RenderTexture, so the 3D piece can be eyeballed without compositing the overlay canvas.</summary>
        private IEnumerator CaptureUpgradeWeapon()
        {
            UpgradeState.Reset();
            UpgradeState.Install(PartKind.AugmentationHarness);
            UpgradeState.Install(PartKind.AccelerationEngine);

            var stage = UpgradeWeaponStage.Create(null);
            stage.Show(PartKind.PowerNozzle);
            for (int i = 0; i < 10; i++) { stage.Tick(1.3f, 0.45f, 0.45f); yield return null; }   // seat the new part

            WriteRenderTexture(stage.Texture, "06_upgrade_weapon");
            Destroy(stage.gameObject);
            UpgradeState.Reset();
        }

        private void WriteRenderTexture(RenderTexture rt, string name)
        {
            if (rt == null) return;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false);
            var prev = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(_outDir, name + ".png"), tex.EncodeToPNG());
                _manifest.AppendLine(name + ".png");
                Log($"wrote {name}.png");
            }
            finally { RenderTexture.active = prev; Destroy(tex); }
        }

        // --- staging helpers ------------------------------------------------------------------

        /// <summary>Force a handful of robots onto the field immediately (the spawner only trickles
        /// them). Reflection because SpawnOne is private — this is a filming tool, not gameplay.</summary>
        private IEnumerator PopulateEnemies(int count, int waitFrames)
        {
            var spawners = FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None);
            var spawnOne = typeof(EnemySpawner).GetMethod("SpawnOne", BindingFlags.NonPublic | BindingFlags.Instance);
            if (spawners.Length > 0 && spawnOne != null)
            {
                for (int i = 0; i < count; i++)
                    spawnOne.Invoke(spawners[i % spawners.Length], null);
            }
            for (int i = 0; i < waitFrames; i++) yield return null;
        }

        /// <summary>Pose a cluster of robots just in front of Max, freeze them so they hold the pose,
        /// point Max at them and open fire — the spray VFX and splashes make the action read.</summary>
        private IEnumerator StageCombat(Transform max)
        {
            yield return PopulateEnemies(7, waitFrames: 6);

            Vector3 fwd = max.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            var robots = FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None);
            int n = Mathf.Min(robots.Length, 6);
            for (int i = 0; i < n; i++)
            {
                var e = robots[i];
                e.enabled = false;                          // hold position — don't let them walk off/into Max
                float lane = (i - (n - 1) * 0.5f);
                Vector3 pos = max.position + fwd * (3.2f + 0.35f * Mathf.Abs(lane)) + right * lane * 1.25f;
                pos.y = e.transform.position.y;
                e.transform.position = pos;
                e.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up); // facing Max
            }
            Physics.SyncTransforms();

            DevMode.AutoFire = true;                          // WaterBlaster reads DevMode.IsAutoFiring
            for (int i = 0; i < 8; i++) yield return null;    // let the stream + splashes build up
        }

        /// <summary>Wake Big Bermuda the way the game does — destroy every factory, which fires
        /// FactoryCensus.Cleared and flips the boss out of Dormant. Max is invincible, so the boss
        /// engaging is safe to film.</summary>
        private IEnumerator EngageBoss()
        {
            foreach (var hutch in FindObjectsByType<MowerHutch>(FindObjectsSortMode.None))
            {
                var d = hutch as IDamageable;
                if (d != null && d.IsAlive)
                    d.TakeDamage(new DamageInfo(1_000_000f, hutch.transform.position, Vector3.forward, Team.Player));
            }
            // Let the boss run its intro and its rig light up and follow into place.
            for (int i = 0; i < 90; i++) yield return null;
        }

        // --- HUD -------------------------------------------------------------------------------

        private HudController _hud;

        private HudController Hud => _hud != null ? _hud : (_hud = FindFirstObjectByType<HudController>());

        private void HideHud()
        {
            RestoreCanvas();
            var hud = Hud;
            if (hud != null) hud.gameObject.SetActive(false);
        }

        /// <summary>Make the HUD render INTO the capture camera, at the gameplay press-kit's own
        /// resolution. Thin wrapper over <see cref="ShowCanvasOnCamera"/> — kept so the six existing
        /// gameplay shots don't have to know their own output size.</summary>
        private void ShowHud(Camera cam)
        {
            var hud = Hud;
            if (hud == null) return;
            hud.gameObject.SetActive(true);
            var canvas = hud.GetComponentInChildren<Canvas>(true);
            ShowCanvasOnCamera(canvas, cam, DefaultOutW, DefaultOutH);
        }

        // --- generic canvas capture (MV-421) ----------------------------------------------------
        // ScreenSpaceOverlay draws straight to the backbuffer and never appears in a Camera.Render();
        // flipping a canvas to ScreenSpace-Camera composites it into our RenderTexture instead. This
        // generalises the old HUD-only ShowHud to ANY self-installing screen's canvas — WeaponsScreen,
        // HomeScreen, ResultScreen and HudController are each a separate GameObject with their own
        // canvas, so a single "the HUD" lookup can't reach them.

        private Canvas _activeCaptureCanvas;
        private RenderMode _activeCaptureCanvasPrevMode;
        private Camera _activeCaptureCanvasPrevWorldCam;
        private float _activeCaptureCanvasPrevScaleFactor;
        private CanvasScaler _activeCaptureScaler;
        private bool _activeCaptureScalerWasEnabled;

        /// <summary>Composite <paramref name="canvas"/> into <paramref name="cam"/>'s render at exactly
        /// the UI scale a <paramref name="w"/>x<paramref name="h"/> screen would produce.
        ///
        /// CanvasScaler's own "Scale With Screen Size" always reads the ambient <c>Screen.width</c>/
        /// <c>Screen.height</c> — which in Editor Play Mode is the Game View's window size, not this
        /// capture's target resolution, and <c>Screen.SetResolution</c> is a no-op in Play Mode. Left
        /// enabled, CanvasScaler would size the UI for the wrong frame entirely. So: disable it for the
        /// duration and set <c>canvas.scaleFactor</c> ourselves with <see cref="ComputeScaleFactor"/>,
        /// which reimplements CanvasScaler's own match-width-or-height formula against the explicit
        /// (w, h) this shot is actually rendering at. A ScreenSpaceCamera canvas's on-screen EXTENT
        /// already tracks the camera's targetTexture size correctly on its own — only the scale factor
        /// needs this workaround.</summary>
        private void ShowCanvasOnCamera(Canvas canvas, Camera cam, int w, int h)
        {
            if (canvas == null || cam == null) return;
            int ui = LayerMask.NameToLayer("UI");
            if (ui >= 0) cam.cullingMask |= (1 << ui);   // a camera-space canvas only draws if its layer is rendered

            _activeCaptureCanvas = canvas;
            _activeCaptureCanvasPrevMode = canvas.renderMode;
            _activeCaptureCanvasPrevWorldCam = canvas.worldCamera;
            _activeCaptureCanvasPrevScaleFactor = canvas.scaleFactor;
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
            _activeCaptureCanvas.scaleFactor = _activeCaptureCanvasPrevScaleFactor;
            if (_activeCaptureScaler != null) _activeCaptureScaler.enabled = _activeCaptureScalerWasEnabled;
            _activeCaptureCanvas = null;
            _activeCaptureScaler = null;
        }

        /// <summary>Reimplements <c>CanvasScaler</c>'s "Scale With Screen Size" / match-width-or-height
        /// math (Unity's own documented log-lerp formula) against an explicit (w, h) instead of
        /// <c>Screen.width</c>/<c>Screen.height</c>. Public and pure so it's pinned by an EditMode test
        /// without building a canvas — e.g. a 1920x1080-reference canvas with <c>matchWidthOrHeight=1</c>
        /// (match by height) captured at 1728x1080 must resolve to scaleFactor 1, exactly the "visible
        /// reference width collapses to 1728" arithmetic MV-421 was opened to catch. Expand/Shrink match
        /// modes aren't used by any screen today; they fall back to the tighter/looser axis respectively,
        /// same as CanvasScaler itself.</summary>
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

        // --- camera framing --------------------------------------------------------------------

        private static void PlaceOrbit(Camera cam, Vector3 focus, float pitch, float yaw, float distance)
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 fwd = rot * Vector3.forward;
            cam.transform.SetPositionAndRotation(focus - fwd * distance, rot);
        }

        /// <summary>Distance at which a sphere of <paramref name="radius"/> fills the frame, taking the
        /// tighter of the vertical/horizontal FOV so nothing is cropped.</summary>
        private static float FitDistance(Camera cam, float radius)
        {
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * cam.aspect);
            float dv = radius / Mathf.Tan(vHalf);
            float dh = radius / Mathf.Tan(hHalf);
            return Mathf.Max(dv, dh) * 1.08f;
        }

        private static float Radius(Bounds b) => b.extents.magnitude;

        /// <summary>The play area, from the things that matter — Max, the factories and the boss —
        /// rather than every renderer (the distant backdrop hills would blow the bounds out).</summary>
        private static Bounds PlayAreaBounds(Vector3 fallback)
        {
            bool any = false;
            var b = new Bounds(fallback, Vector3.zero);
            void Add(Vector3 p) { if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p); }

            var max = GameObject.FindGameObjectWithTag("Player");
            if (max != null) Add(max.transform.position);
            foreach (var h in FindObjectsByType<MowerHutch>(FindObjectsSortMode.None)) Add(h.transform.position);
            foreach (var boss in FindObjectsByType<BigBermudaBoss>(FindObjectsSortMode.None)) Add(boss.transform.position);

            if (!any) b = new Bounds(fallback, new Vector3(30f, 0f, 30f));
            b.Expand(new Vector3(6f, 0f, 6f));
            return b;
        }

        // --- capture ---------------------------------------------------------------------------

        private IEnumerator Frame(Action stage, Camera cam, string name)
        {
            Exception staged = null;
            try { stage(); } catch (Exception e) { staged = e; }
            if (staged != null) { LogWarn($"{name}: staging failed — {staged.Message}"); yield break; }

            // A couple of frames so any state the staging changed (VFX, HUD rebuild) is on screen.
            yield return null;
            yield return null;

            try { Capture(cam, name); }
            catch (Exception e) { LogWarn($"{name}: capture failed — {e.Message}"); }
        }

        private void Capture(Camera cam, string name) => CaptureSized(cam, name, DefaultOutW, DefaultOutH, SuperSample);

        /// <summary>Render <paramref name="cam"/> into an RGB PNG at exactly <paramref name="w"/>x
        /// <paramref name="h"/>, optionally supersampled <paramref name="superSample"/>x for clean edges
        /// then downscaled. Generalises the old fixed-2560x1440 capture (MV-421) so one path serves both
        /// the gameplay press-kit's resolution and the ui-screens job's two explicit reference sizes.</summary>
        private void CaptureSized(Camera cam, string name, int w, int h, int superSample)
        {
            int rw = w * superSample, rh = h * superSample;
            var rt = new RenderTexture(rw, rh, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var small = superSample > 1
                ? new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                : null;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            float prevAspect = cam.aspect;
            try
            {
                cam.aspect = (float)w / h;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prevTarget;

                if (small != null)
                {
                    Graphics.Blit(rt, small);                 // supersample down for clean edges
                    RenderTexture.active = small;
                }
                else
                {
                    RenderTexture.active = rt;
                }
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();

                string path = Path.Combine(_outDir, name + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                _manifest.AppendLine(name + ".png");
                Log($"wrote {name}.png ({w}x{h})");
            }
            finally
            {
                cam.aspect = prevAspect;
                RenderTexture.active = prevActive;
                cam.targetTexture = prevTarget;
                Destroy(tex);
                rt.Release(); Destroy(rt);
                if (small != null) { small.Release(); Destroy(small); }
            }
        }

        // --- ui-screens job (MV-421) -------------------------------------------------------------
        // A separate capture sequence from the six gameplay press-kit shots above: it opens each
        // full-screen UI panel with fixed fixture state, captures it at both UiScreenSizes, and closes
        // it again — rather than filming a live playthrough.

        private IEnumerator RunUiScreens()
        {
            _outDir = Arg("-uiscreensOut") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "ui-screens"));
            Directory.CreateDirectory(_outDir);
            Log($"ui-screens capture starting → {_outDir}");

            var cam = Camera.main;
            if (cam == null) { Fail("no Camera.main in the scene"); yield break; }
            if (cam.TryGetComponent<CinemachineBrain>(out var brain)) brain.enabled = false;

            // Let the self-installing screens finish building before we go looking for them.
            for (int i = 0; i < 4; i++) yield return null;

            yield return CaptureRigScreens(cam);
            yield return CaptureHudScreens(cam);

            Finish();
        }

        /// <summary>Every screen this shot needs, captured at every size in <paramref name="sizes"/>,
        /// named <c>&lt;baseName&gt;-&lt;suffix&gt;.png</c>. Show/capture/restore per size rather than
        /// once for all sizes so each shot gets its own freshly-computed scale factor.</summary>
        private IEnumerator CaptureCanvasAtSizes(Canvas canvas, Camera cam, string baseName,
            IReadOnlyList<(int w, int h, string suffix)> sizes)
        {
            foreach (var s in sizes)
            {
                ShowCanvasOnCamera(canvas, cam, s.w, s.h);
                yield return null;   // let the canvas rebuild its layout at the new scale factor
                yield return null;
                CaptureSized(cam, $"{baseName}-{s.suffix}", s.w, s.h, superSample: 1);
                RestoreCanvas();
            }
        }

        /// <summary>THE RIG board (MV-421 AC1) — the fixed fixture from the ticket, matching
        /// <c>MV-423.png</c> so the capture is directly comparable to the design reference, plus a
        /// zero-parts variant of the same fixture. Comparing the two is how the amber "+" badge and the
        /// parts tray's empty state get verified.</summary>
        private IEnumerator CaptureRigScreens(Camera cam)
        {
            var weapons = FindFirstObjectByType<WeaponsScreen>();
            if (weapons == null) { LogWarn("ui-screens: no WeaponsScreen in the scene"); yield break; }

            ApplyRigFixture();
            weapons.Open();
            yield return null;
            yield return null;

            var canvas = weapons.GetComponentInChildren<Canvas>(true);
            if (canvas == null) { LogWarn("ui-screens: WeaponsScreen built no canvas"); weapons.Close(); yield break; }

            yield return CaptureCanvasAtSizes(canvas, cam, "rig", UiScreenSizes);
            weapons.Close();

            // Same node fixture, banked parts spent to zero — TrySpendPart only decrements the wallet's
            // count, it never touches a RigState node level (see PickupWallet.TrySpendPart's own doc),
            // so this leaves every node exactly where ApplyRigFixture put it.
            for (int i = 0; i < 4; i++) PickupWallet.TrySpendPart();

            weapons.Open();
            yield return null;
            yield return null;
            yield return CaptureCanvasAtSizes(canvas, cam, "rig-noparts", new[] { UiScreenSizes[0] });
            weapons.Close();
        }

        /// <summary>Drives THE RIG (MV-422/423) to the exact node/currency state MV-421 specifies:
        /// <c>p_dmg</c> 4, <c>p_rng</c> 3, <c>p_flw</c> 2, <c>p_spr</c> 0, <c>p_prc</c> reached-not-owned,
        /// <c>s_bal</c> reached-not-owned, <c>e_ff</c> 2, <c>e_cel</c> 1, <c>e_cd</c> 3, <c>e_mag</c>
        /// reached-not-owned, <c>m_spd</c>/<c>m_tp</c> not reached, <c>u_sen</c> 1, <c>u_dmg</c> 2,
        /// <c>u_rng</c> 1, <c>u_hp</c>/<c>u_mov</c>/<c>u_cst</c> 0, <c>u_slt</c> not reached. Cells 28/30,
        /// parts banked 4. There is no bulk setter on <see cref="RigState"/> by design (a fixture going
        /// through the same spend/acquire API a run uses is also the only way to know it's reachable in
        /// play) — so every node is driven up one level at a time.</summary>
        private void ApplyRigFixture()
        {
            PickupWallet.Reset();   // also resets RigState — see PickupWallet.Reset's own doc

            void RaiseTo(string id, int target)
            {
                while (RigState.Level(id) < target)
                {
                    bool ok = RigBoard.IsCap(id) && RigState.Level(id) == 0
                        ? RigState.AcquireCap(id)
                        : RigState.TrySpendPart(id);
                    if (!ok) { LogWarn($"ui-screens: rig fixture couldn't raise {id} to {target}"); return; }
                }
            }

            RaiseTo("p_dmg", 4);
            RaiseTo("p_rng", 3);
            RaiseTo("p_flw", 2);
            // p_spr stays 0; p_prc is reached (p_flw owned) but left un-acquired — the SHED badge state.
            // s_bal is a root cap, left un-acquired — reached-not-owned.
            RaiseTo("e_ff", 2);
            RaiseTo("e_cel", 1);     // also lifts PickupWallet.Capacity to 30 (20 + 1 * 10)
            RaiseTo("e_cd", 3);
            // e_mag is reached (e_cd owned) but left un-acquired — the SHED badge state.
            // m_spd, m_tp are root caps, left un-acquired — not reached is impossible for a root, so this
            // fixture leaves them at the same reached-not-owned state as every other unacquired root cap.
            RaiseTo("u_sen", 1);
            RaiseTo("u_dmg", 2);
            RaiseTo("u_rng", 1);
            // u_hp stays 0, which keeps its child u_slt genuinely not-reached.

            PickupWallet.SetPowerCells(28);   // Capacity is 30 once e_cel is level 1, above
            for (int i = 0; i < 4; i++) PickupWallet.AddPart();
        }

        /// <summary>In-game HUD (MV-421 AC "priority 2") at the shipped gameplay framing (matches shot
        /// 03_hud_minimap above), captured over the dressed-but-static opening frame — no combat staged,
        /// since the point here is the HUD chrome itself, not a gameplay moment.</summary>
        private IEnumerator CaptureHudScreens(Camera cam)
        {
            var max = GameObject.FindGameObjectWithTag("Player");
            var hud = Hud;
            if (max == null || hud == null)
            {
                LogWarn("ui-screens: no Player/HudController in the scene, skipping hud shots");
                yield break;
            }

            var canvas = hud.GetComponentInChildren<Canvas>(true);
            if (canvas == null) { LogWarn("ui-screens: HudController built no canvas"); yield break; }

            hud.gameObject.SetActive(true);
            PlaceOrbit(cam, max.transform.position, FramePitch, 0f, 25.1f);
            yield return null;

            yield return CaptureCanvasAtSizes(canvas, cam, "hud", UiScreenSizes);
            hud.gameObject.SetActive(false);
        }

        // --- lifecycle / reporting -------------------------------------------------------------

        private void Finish()
        {
            File.WriteAllText(Path.Combine(_outDir, DoneMarker),
                "ok\n" + _manifest.ToString(), Encoding.UTF8);
            Log("press-kit capture complete");
        }

        private void Fail(string why)
        {
            LogWarn("press-kit capture aborted: " + why);
            try { File.WriteAllText(Path.Combine(_outDir ?? ".", DoneMarker), "fail: " + why + "\n"); }
            catch { /* best effort */ }
        }

        private static void Log(string m) => Debug.Log("[PressKit] " + m);
        private static void LogWarn(string m) => Debug.LogWarning("[PressKit] " + m);
    }
}
