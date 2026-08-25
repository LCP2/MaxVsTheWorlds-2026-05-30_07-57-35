using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// MV-545 diagnostic-only capture: repeatedly aims the Water Balloon and Teleport landing circles
    /// and records, for each attempt, exactly the readings the ticket's AC1 asks for — the circle
    /// GameObject's active state, its mesh vertex count, its world position, and its own
    /// <see cref="Renderer.isVisible"/> — plus a screenshot centred on the landing point. NOT a
    /// PlayMode NUnit test (<c>CC_AUTONOMY.md</c> bans those outright, they hang in batch mode): this
    /// is the same bounded, self-exiting play-mode-capture idiom already proven safe in this repo by
    /// <c>PressKitDirector</c> / <c>UiScreensDirector</c> — enters play, runs a fixed number of cycles
    /// or a deadline, writes a done-marker, and its Editor-side caller exits the process. Nothing here
    /// asserts pass/fail; it only reports what it saw.
    ///
    /// Deliberately stages the worst case for the ticket's own H1 (render-sort) hypothesis: after
    /// every aim it drops a real ground decal (<see cref="HudSignals.EmitEnemyKilled"/>) exactly where
    /// the circle just sat, so the NEXT aim at the same spot is contending with fresh, real, same
    /// y-height, same-render-queue ground art — not a hypothetical pile of clutter, one concrete
    /// decal per cycle, same as a player who just splashed something there.
    ///
    /// INERT in a normal session — installs only behind the <c>-aimcirclediag</c> flag or a
    /// <c>Temp/aimcirclediag.arm</c> marker, the same arm idiom every other director here uses.
    /// </summary>
    public sealed class AimCircleDiagnosticsDirector : MonoBehaviour
    {
        private const string DoneMarker = "_aimcirclediag_done.txt";
        private const int Cycles = 5;
        private const int ShotSize = 720;

        /// <summary>MV-441's own reachable folder — same directory <c>CC_AUTONOMY.md</c> already grants
        /// this worker read/write on. Best-effort: a box with no such drive must not fail the run.</summary>
        private const string DesignImagesScreensDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

        private string _outDir;
        private string _outDir2;
        private readonly StringBuilder _log = new StringBuilder();
        private Camera _captureCam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Armed()) return;
            if (FindFirstObjectByType<AimCircleDiagnosticsDirector>() != null) return;
            new GameObject("AimCircleDiagnosticsDirector").AddComponent<AimCircleDiagnosticsDirector>();
        }

        public static bool Armed()
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "-aimcirclediag", StringComparison.OrdinalIgnoreCase)) return true;
            try { return File.Exists(Path.Combine("Temp", "aimcirclediag.arm")); }
            catch { return false; }
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            _outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press"));
            Directory.CreateDirectory(_outDir);
            try { Directory.CreateDirectory(DesignImagesScreensDir); _outDir2 = DesignImagesScreensDir; }
            catch (Exception e) { LogWarn($"secondary output dir unavailable ({DesignImagesScreensDir}): {e.Message}"); _outDir2 = null; }

            Log("MV-545 aim-circle diagnostic starting");

            WeaponSystemState.Reset();
            PickupWallet.Reset();
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
            PickupWallet.SetPowerCells(10);
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            WeaponSystemState.Acquire(AbilityKind.Teleport);

            // HudController rebuilds/activates both joysticks off its own WeaponSystemState.Changed
            // subscription — give it a few frames to react before we go looking for them.
            for (int i = 0; i < 6; i++) yield return null;

            if (GameObject.FindGameObjectWithTag("Player") == null) { Fail("no Player-tagged Max in the scene"); yield break; }

            var balloon = FindFirstObjectByType<WaterBalloonJoystickControl>();
            var teleport = FindFirstObjectByType<TeleportJoystickControl>();
            if (balloon == null) { Fail("no WaterBalloonJoystickControl in the scene"); yield break; }
            if (teleport == null) { Fail("no TeleportJoystickControl in the scene"); yield break; }

            _captureCam = CreateCaptureCamera();

            yield return RunControl("Balloon", balloon,
                () => balloon.LandingCircleVisible, () => balloon.LandingCircleVertexCount,
                () => balloon.LandingCircleWorldPosition, () => balloon.LandingCircleRendererIsVisible);

            yield return RunControl("Teleport", teleport,
                () => teleport.LandingCircleVisible, () => teleport.LandingCircleVertexCount,
                () => teleport.LandingCircleWorldPosition, () => teleport.LandingCircleRendererIsVisible);

            if (_captureCam != null) { Destroy(_captureCam.gameObject); _captureCam = null; }
            Finish();
        }

        /// <summary>Aim/read/release, <see cref="Cycles"/> times, for one control. Drags out to full
        /// deflection (armed, landing circle at the ability's real max distance) then drags back to
        /// centre before releasing — MV-372's own arm/disarm abort — so this never actually fires,
        /// never spends a cooldown or a cell, and never moves Max (a live Teleport blink would drift
        /// the origin every cycle and make "same spot" mean something different each time).</summary>
        private IEnumerator RunControl(string name, AbilityJoystickControlBase control,
            Func<bool> visible, Func<int> vertexCount, Func<Vector3> position, Func<bool> rendererVisible)
        {
            for (int i = 1; i <= Cycles; i++)
            {
                control.OnPointerDown(At(Vector2.zero));
                control.OnDrag(At(new Vector2(AbilityJoystickControlBase.DragRadiusPixels, 0f)));   // full deflection, armed

                yield return null;   // let RebuildAimVisual's mesh assignment land and one frame render

                bool active = visible();
                int vcount = vertexCount();
                Vector3 pos = position();
                bool rvis = rendererVisible();

                Log($"[{name}] cycle {i}: active={active} vertexCount={vcount} pos={pos:F2} renderer.isVisible={rvis}");
                yield return CaptureAround($"aimcircle-{name.ToLowerInvariant()}-cycle{i}", pos);

                control.OnDrag(At(Vector2.zero));      // back to centre — disarms
                control.OnPointerUp(At(Vector2.zero)); // release while disarmed: abort, no fire, no cost (MV-372)

                // Stage the worst case for H1: a real ground decal exactly where the circle just sat.
                HudSignals.EmitEnemyKilled(pos);
                yield return null;
            }
        }

        private static PointerEventData At(Vector2 pos) => new PointerEventData(EventSystem.current) { position = pos };

        // --- capture -----------------------------------------------------------------------------

        private static Camera CreateCaptureCamera()
        {
            var go = new GameObject("AimCircleDiagCam");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 500f;
            cam.enabled = false;   // rendered manually via Camera.Render()
            return cam;
        }

        private IEnumerator CaptureAround(string name, Vector3 pos)
        {
            if (_captureCam == null) yield break;
            Vector3 focus = new Vector3(pos.x, 0.5f, pos.z);
            PlaceOrbit(_captureCam, focus, 72f, 0f, 5f);   // the game's own fixed top-down pitch
            yield return null;
            Capture(_captureCam, name);
        }

        private static void PlaceOrbit(Camera cam, Vector3 focus, float pitch, float yaw, float distance)
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 fwd = rot * Vector3.forward;
            cam.transform.SetPositionAndRotation(focus - fwd * distance, rot);
        }

        private void Capture(Camera cam, string name)
        {
            var rt = new RenderTexture(ShotSize, ShotSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(ShotSize, ShotSize, TextureFormat.RGB24, false);
            var prevActive = RenderTexture.active;
            var prevTarget = cam.targetTexture;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, ShotSize, ShotSize), 0, 0);
                tex.Apply();
                byte[] png = tex.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(_outDir, name + ".png"), png);
                TryWriteSecondary(name, png);
                Log($"wrote {name}.png");
            }
            catch (Exception e) { LogWarn($"{name}: capture failed — {e.Message}"); }
            finally
            {
                RenderTexture.active = prevActive;
                cam.targetTexture = prevTarget;
                rt.Release(); Destroy(rt);
                Destroy(tex);
            }
        }

        private void TryWriteSecondary(string name, byte[] png)
        {
            if (_outDir2 == null) return;
            try { File.WriteAllBytes(Path.Combine(_outDir2, name + ".png"), png); }
            catch (Exception e) { LogWarn($"{name}: secondary write to {_outDir2} failed — {e.Message}"); }
        }

        // --- lifecycle / reporting -----------------------------------------------------------------

        private void Finish()
        {
            File.WriteAllText(Path.Combine(_outDir, DoneMarker), "ok\n" + _log.ToString(), Encoding.UTF8);
            Log("aim-circle diagnostic complete");
        }

        private void Fail(string why)
        {
            LogWarn("aim-circle diagnostic aborted: " + why);
            try { File.WriteAllText(Path.Combine(_outDir ?? ".", DoneMarker), "fail: " + why + "\n" + _log); }
            catch { /* best effort */ }
        }

        private void Log(string m) { _log.AppendLine(m); Debug.Log("[AimCircleDiag] " + m); }
        private void LogWarn(string m) { _log.AppendLine("WARN: " + m); Debug.LogWarning("[AimCircleDiag] " + m); }
    }
}
