using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Unity.Cinemachine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// MV-507's AC5 eyes-on evidence: one screenshot of a robot against grass, at NATIVE resolution —
    /// deliberately NOT supersampled. <see cref="RobotRosterDirector"/> and <see cref="PressKitDirector"/>
    /// both render at 2x and downscale specifically to give "clean AA regardless of URP MSAA" (their own
    /// doc comments) — exactly the thing this ticket needs to avoid, since the whole point is to see
    /// MSAA's actual effect on a hard silhouette edge. Reuses <see cref="RobotRosterDirector.BuildRobot"/>
    /// and <see cref="RobotRosterDirector.OpenZoneCenter"/> so the framing and the body match MV-452's
    /// roster exactly; only the capture step differs. Names the output from a <c>-msaaLabel &lt;name&gt;</c>
    /// command-line argument (falls back to "capture") rather than resolving the pipeline asset itself —
    /// <c>MaxWorlds.Gameplay</c> (this assembly) has no URP reference, unlike <c>MaxWorlds.Rendering</c>,
    /// and adding one for a one-off diagnostic capture isn't worth the footprint.
    /// </summary>
    public sealed class MsaaComparisonDirector : MonoBehaviour
    {
        private const int OutW = 1920;
        private const int OutH = 1080;
        private const float Pitch = 64.88f;
        private const float Distance = 15.81f;

        private const string DoneMarker = "_msaacompare_done.txt";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Armed()) return;
            if (FindFirstObjectByType<MsaaComparisonDirector>() != null) return;
            new GameObject("MsaaComparisonDirector").AddComponent<MsaaComparisonDirector>();
        }

        public static bool Armed()
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "-msaacompare", StringComparison.OrdinalIgnoreCase)) return true;
            try { return File.Exists(Path.Combine("Temp", "msaacompare.arm")); }
            catch { return false; }
        }

        private string _outDir;

        private static string Arg(string flag, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return fallback;
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            _outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press", "msaa-comparison"));
            Directory.CreateDirectory(_outDir);

            var cam = Camera.main;
            if (cam == null) { Fail("no Camera.main in the scene"); yield break; }
            if (cam.TryGetComponent<CinemachineBrain>(out var brain)) brain.enabled = false;
            cam.aspect = (float)OutW / OutH;

            Vector3 focusBase = RobotRosterDirector.OpenZoneCenter() ?? Vector3.forward * 6f;

            // Let the self-installing systems dress the world first (materials, lighting, Max).
            for (int i = 0; i < 4; i++) yield return null;

            GameObject robot = RobotRosterDirector.BuildRobot(EnemyKind.Rusher, focusBase);
            for (int i = 0; i < 3; i++) yield return null;   // let the rig's build settle on screen

            var rot = Quaternion.Euler(Pitch, 0f, 0f);
            Vector3 focus = robot.transform.position;
            cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * Distance, rot);

            yield return null;
            yield return null;

            int.TryParse(Arg("-msaaSamples", "1"), out int msaaSamples);
            Capture(cam, Arg("-msaaLabel", "capture"), msaaSamples);

            Destroy(robot);
            Finish();
        }

        /// <summary>Native resolution, no supersampling — the actual pixels the active MSAA setting
        /// produces, not a smoothed-over approximation. <paramref name="msaaSamples"/> must be set
        /// explicitly: a Camera rendering into a manually-created target <see cref="RenderTexture"/>
        /// takes its multisample count from THAT texture's own <c>antiAliasing</c> field, not from the
        /// active <c>UniversalRenderPipelineAsset.msaaSampleCount</c> — confirmed the hard way, the first
        /// version of this method left it at the RenderTexture default (1) and produced byte-identical
        /// PNGs for the MSAA-off and MSAA-4x runs.</summary>
        private void Capture(Camera cam, string name, int msaaSamples)
        {
            int aa = msaaSamples switch { 2 => 2, 4 => 4, 8 => 8, _ => 1 };
            var rt = new RenderTexture(OutW, OutH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = aa
            };
            var tex = new Texture2D(OutW, OutH, TextureFormat.RGB24, false);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prevTarget;

                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, OutW, OutH), 0, 0);
                tex.Apply();

                byte[] png = tex.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(_outDir, name + ".png"), png);
                Log($"wrote {name}.png");
            }
            finally
            {
                RenderTexture.active = prevActive;
                cam.targetTexture = prevTarget;
                Destroy(tex);
                rt.Release(); Destroy(rt);
            }
        }

        private void Finish()
        {
            File.WriteAllText(Path.Combine(_outDir, DoneMarker), "ok\n");
            Log("msaa-comparison capture complete");
        }

        private void Fail(string why)
        {
            LogWarn("msaa-comparison capture aborted: " + why);
            try { File.WriteAllText(Path.Combine(_outDir ?? ".", DoneMarker), "fail: " + why + "\n"); }
            catch { /* best effort */ }
        }

        private static void Log(string m) => Debug.Log("[MsaaComparison] " + m);
        private static void LogWarn(string m) => Debug.LogWarning("[MsaaComparison] " + m);
    }
}
