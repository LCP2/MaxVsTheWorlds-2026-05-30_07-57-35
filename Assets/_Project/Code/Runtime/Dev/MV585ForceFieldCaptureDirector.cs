using System;
using System.Collections;
using System.IO;
using UnityEngine;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// MV-585 AC6 eyes-on evidence: an iPhone-landscape capture of the Force Field HUD button with the
    /// bubble raised, so Lee can judge the enlarged percentage label directly rather than take the
    /// EditMode geometry tests' word for it. Same INERT-unless-armed shape as
    /// <see cref="WaterGroundTrailCaptureDirector"/> — installs only behind the <c>-mv585shot</c> flag
    /// or a <c>Temp/mv585.arm</c> marker, otherwise never touches the game.
    ///
    /// The Force Field button's visibility is baked once in <see cref="HudController"/>'s own Awake
    /// (<c>root.gameObject.SetActive(WeaponSystemState.IsAcquired(AbilityKind.ForceField))</c>), so the
    /// RIG ownership has to be forced at <c>BeforeSceneLoad</c> — before that Awake runs — same fixture
    /// idiom <c>UiScreensDirector.ResetRunForFixture</c>/<c>SpendRigFixtureLevels</c> already uses
    /// (<c>RigState.UnlockCategory</c> then <c>RigState.AcquireCap("e_ff")</c>). Raising the bubble
    /// itself uses <see cref="MaxWorlds.Weapons.PlayerAbilities.ForceActivateForceFieldForTuning"/>, the
    /// same dev-tuning affordance (MV-455) the Settings panel's "Force field hold" toggle calls — never
    /// gameplay, a filming/tuning hook only.
    /// </summary>
    public sealed class MV585ForceFieldCaptureDirector : MonoBehaviour
    {
        // 852x393 — this codebase's own established "iPhone landscape" capture size (see
        // UiScreensDirector's rig-mv472-852x393 shot), matching the 393pt reference height
        // MV585ForceFieldLabelFontSizeTests already builds its own legibility floor against.
        private const int OutW = 852;
        private const int OutH = 393;
        private const int SuperSample = 2;

        private const string DoneMarker = "_mv585_done.txt";
        private static readonly string OutDir = @"C:\Dev\MaxVsTheWorlds-Images";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void GrantForceFieldBeforeAwake()
        {
            if (!Armed()) return;
            RigState.Reset();
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
            RigState.AcquireCap("e_ff");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Armed()) return;
            if (FindFirstObjectByType<MV585ForceFieldCaptureDirector>() != null) return;
            new GameObject("MV585ForceFieldCaptureDirector").AddComponent<MV585ForceFieldCaptureDirector>();
        }

        public static bool Armed()
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "-mv585shot", StringComparison.OrdinalIgnoreCase)) return true;
            try { return File.Exists(Path.Combine("Temp", "mv585.arm")); }
            catch { return false; }
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            Directory.CreateDirectory(OutDir);
            Log($"MV-585 capture starting -> {OutDir}");

            var cam = Camera.main;
            if (cam == null) { Fail("no Camera.main in the scene"); yield break; }
            cam.aspect = (float)OutW / OutH;

            var maxGo = GameObject.FindGameObjectWithTag("Player");
            if (maxGo == null) { Fail("no Player-tagged Max in the scene"); yield break; }
            var abilities = maxGo.GetComponent<PlayerAbilities>();
            if (abilities == null) { Fail("Max has no PlayerAbilities"); yield break; }

            var hud = FindFirstObjectByType<HudController>();
            if (hud == null) { Fail("no HudController in the scene"); yield break; }

            abilities.ForceActivateForceFieldForTuning();

            // Let HudController's own Update tick the label from the freshly-raised bubble.
            for (int i = 0; i < 3; i++) yield return null;

            ShowHud(hud, cam);
            yield return null;

            Capture(cam, "MV-585");
            Finish();
        }

        private Canvas _hudCanvas;
        private RenderMode _hudMode;

        private void ShowHud(HudController hud, Camera cam)
        {
            hud.gameObject.SetActive(true);
            int ui = LayerMask.NameToLayer("UI");
            if (ui >= 0) cam.cullingMask |= (1 << ui);
            _hudCanvas = hud.GetComponentInChildren<Canvas>(true);
            if (_hudCanvas == null) return;
            _hudMode = _hudCanvas.renderMode;
            _hudCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            _hudCanvas.worldCamera = cam;
            _hudCanvas.planeDistance = 1f;
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

                Graphics.Blit(rt, small);
                RenderTexture.active = small;
                tex.ReadPixels(new Rect(0, 0, OutW, OutH), 0, 0);
                tex.Apply();

                byte[] png = tex.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), png);
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

        private void Finish()
        {
            File.WriteAllText(Path.Combine(OutDir, DoneMarker), "ok\n");
            Log("MV-585 capture complete");
        }

        private void Fail(string why)
        {
            LogWarn("MV-585 capture aborted: " + why);
            try { File.WriteAllText(Path.Combine(OutDir, DoneMarker), "fail: " + why + "\n"); }
            catch { /* best effort */ }
        }

        private static void Log(string m) => Debug.Log("[MV585Capture] " + m);
        private static void LogWarn(string m) => Debug.LogWarning("[MV585Capture] " + m);
    }
}
