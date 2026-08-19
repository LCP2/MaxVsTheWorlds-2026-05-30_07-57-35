using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using Unity.Cinemachine;
using MaxWorlds.Player;
using MaxWorlds.VFX;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// Renders one 1920x1080 screenshot of Max per compass yaw (MV-453) at the review framing Lee
    /// dialled in with the MV-450 dev panel — pitch 64.88° / distance 15.81 m, approved 2026-08-19
    /// (see MV-468, which bakes it as the shipped default; not yet landed, so this director sets the
    /// numbers itself, the same call <see cref="RobotRosterDirector"/> makes and for the same reason).
    ///
    /// Same INERT-unless-armed shape as <see cref="RobotRosterDirector"/>/<see cref="PressKitDirector"/>,
    /// its own marker so it never collides with either of those on the same run.
    ///
    /// <see cref="MaxBody"/>'s gadget geometry is a fused static mesh (MV-451) — there is no live
    /// aim/hip lerp left to pose (see that class's own doc comment), so there is exactly one gadget
    /// pose to review, not two. What varies from shot to shot is which way MAX FACES: the camera never
    /// orbits (project rule — the rig is fixed top-down, no free-look), so "every camera yaw" means
    /// turning Max on the spot in front of a still camera, the same view a player gets as he turns
    /// while the fixed-angle camera holds still above him.
    /// </summary>
    public sealed class MaxDetailDirector : MonoBehaviour
    {
        private const int OutW = 1920;
        private const int OutH = 1080;
        private const int SuperSample = 2;   // render at 2x then downscale — clean AA regardless of URP MSAA

        private const float Pitch = 64.88f;
        private const float Distance = 15.81f;

        /// <summary>Eight yaws, 45° apart — enough to catch every side of an asymmetric rig (the
        /// gadget and both arms sit off-centre) without turning a tight-slice ticket into a full
        /// 360° frame-by-frame sweep.</summary>
        private static readonly float[] Yaws = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };

        private const string DoneMarker = "_maxdetail_done.txt";
        private const string DesignImagesScreensDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens\max-detail";

        private string _outDir;
        private string _outDir2;
        private readonly StringBuilder _manifest = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Armed()) return;
            if (FindFirstObjectByType<MaxDetailDirector>() != null) return;
            new GameObject("MaxDetailDirector").AddComponent<MaxDetailDirector>();
        }

        public static bool Armed()
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "-maxdetail", StringComparison.OrdinalIgnoreCase)) return true;
            try { return File.Exists(Path.Combine("Temp", "maxdetail.arm")); }
            catch { return false; }
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            _outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press", "max-detail"));
            Directory.CreateDirectory(_outDir);
            try { Directory.CreateDirectory(DesignImagesScreensDir); _outDir2 = DesignImagesScreensDir; }
            catch (Exception e) { LogWarn($"secondary output dir unavailable ({DesignImagesScreensDir}): {e.Message}"); _outDir2 = null; }

            Log($"max-detail capture starting → {_outDir}" + (_outDir2 != null ? $" (+ {_outDir2})" : ""));

            var cam = Camera.main;
            if (cam == null) { Fail("no Camera.main in the scene"); yield break; }
            if (cam.TryGetComponent<CinemachineBrain>(out var brain)) brain.enabled = false;
            cam.aspect = (float)OutW / OutH;

            var maxGo = GameObject.FindGameObjectWithTag("Player");
            if (maxGo == null) { Fail("no Player in the scene"); yield break; }
            var controller = maxGo.GetComponent<PlayerController>();
            var facingField = typeof(PlayerController).GetField("_facing",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (controller == null || facingField == null) { Fail("PlayerController._facing not found"); yield break; }

            // Let the self-installing systems (materials, the map, MaxRig) settle first.
            for (int i = 0; i < 4; i++) yield return null;

            foreach (float yaw in Yaws)
            {
                Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                facingField.SetValue(controller, forward);
                maxGo.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

                for (int i = 0; i < 6; i++) yield return null;   // let the rig's LateUpdate settle on the new facing

                Vector3 focus = maxGo.transform.position + Vector3.up * 0.9f;   // roughly chest height
                var rot = Quaternion.Euler(Pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * Distance, rot);

                yield return null;
                yield return null;
                Capture(cam, $"max-yaw{yaw:000}");
            }

            Finish();
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
                string path = Path.Combine(_outDir, name + ".png");
                File.WriteAllBytes(path, png);
                if (_outDir2 != null)
                {
                    try { File.WriteAllBytes(Path.Combine(_outDir2, name + ".png"), png); }
                    catch (Exception e) { LogWarn($"secondary write failed for {name}: {e.Message}"); }
                }
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

        private void Finish()
        {
            File.WriteAllText(Path.Combine(_outDir, DoneMarker), "ok\n" + _manifest.ToString(), Encoding.UTF8);
            Log("max-detail capture complete");
        }

        private void Fail(string why)
        {
            LogWarn("max-detail capture aborted: " + why);
            try { File.WriteAllText(Path.Combine(_outDir ?? ".", DoneMarker), "fail: " + why + "\n"); }
            catch { /* best effort */ }
        }

        private static void Log(string m) => Debug.Log("[MaxDetail] " + m);
        private static void LogWarn(string m) => Debug.LogWarning("[MaxDetail] " + m);
    }
}
