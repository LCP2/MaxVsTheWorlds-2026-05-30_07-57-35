using System;
using System.Collections;
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
        private const int OutW = 2560;
        private const int OutH = 1440;
        private const int SuperSample = 2;             // render at 2x then downscale — clean AA regardless of URP MSAA
        private const float FramePitch = 72f;          // the game's fixed top-down angle
        private const string DoneMarker = "_done.txt";

        private string _outDir;
        private readonly StringBuilder _manifest = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Armed()) return;
            if (FindFirstObjectByType<PressKitDirector>() != null) return;
            new GameObject("PressKitDirector").AddComponent<PressKitDirector>();
        }

        /// <summary>Only film when the process was explicitly launched to. A normal player or CI run
        /// trips neither of these and this whole system stays asleep.</summary>
        private static bool Armed()
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "-presskit", StringComparison.OrdinalIgnoreCase)) return true;
            try { return File.Exists(Path.Combine("Temp", "presskit.arm")); }
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
            _outDir = Arg("-presskitOut") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press"));
            Directory.CreateDirectory(_outDir);
            Log($"press-kit capture starting → {_outDir}  ({OutW}x{OutH}, {SuperSample}x SSAA)");

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
            cam.aspect = (float)OutW / OutH;

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
        private Canvas _hudCanvas;
        private RenderMode _hudMode;

        private HudController Hud => _hud != null ? _hud : (_hud = FindFirstObjectByType<HudController>());

        private void HideHud()
        {
            var hud = Hud;
            if (hud != null) hud.gameObject.SetActive(false);
        }

        /// <summary>Make the HUD render INTO the capture camera. A ScreenSpaceOverlay canvas draws
        /// straight to the backbuffer and never appears in a Camera.Render(); flipping it to
        /// ScreenSpace-Camera composites it into our RenderTexture instead.</summary>
        private void ShowHud(Camera cam)
        {
            var hud = Hud;
            if (hud == null) return;
            hud.gameObject.SetActive(true);
            int ui = LayerMask.NameToLayer("UI");
            if (ui >= 0) cam.cullingMask |= (1 << ui);   // a camera-space canvas only draws if its layer is rendered
            if (_hudCanvas == null) _hudCanvas = hud.GetComponentInChildren<Canvas>(true);
            if (_hudCanvas != null)
            {
                _hudMode = _hudCanvas.renderMode;
                _hudCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                _hudCanvas.worldCamera = cam;
                _hudCanvas.planeDistance = 1f;
            }
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

        private void Capture(Camera cam, string name)
        {
            int rw = OutW * SuperSample, rh = OutH * SuperSample;
            var rt = new RenderTexture(rw, rh, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var small = new RenderTexture(OutW, OutH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(OutW, OutH, TextureFormat.RGB24, false);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prevTarget;

                Graphics.Blit(rt, small);                    // supersample down for clean edges
                RenderTexture.active = small;
                tex.ReadPixels(new Rect(0, 0, OutW, OutH), 0, 0);
                tex.Apply();

                string path = Path.Combine(_outDir, name + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                _manifest.AppendLine(name + ".png");
                Log($"wrote {name}.png");
            }
            finally
            {
                RenderTexture.active = prevActive;
                cam.targetTexture = prevTarget;
                Destroy(tex);
                rt.Release(); Destroy(rt);
                small.Release(); Destroy(small);
            }
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
