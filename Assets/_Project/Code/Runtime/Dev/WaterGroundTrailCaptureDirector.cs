using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using Unity.Cinemachine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.UI;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// AC7 eyes-on evidence for MV-555: Max firing due LEFT and due RIGHT, at 1133x744, so Lee can
    /// judge whether the jet now reads as belonging to the aim wedge. Same INERT-unless-armed,
    /// manual-Camera.Render() shape as <see cref="MissileTrailCaptureDirector"/>/
    /// <see cref="PressKitDirector"/> — installs only behind the <c>-mv555shots</c> flag or a
    /// <c>Temp/mv555.arm</c> marker, otherwise never touches the game.
    ///
    /// Setting <see cref="MaxWorlds.Player.PlayerController"/>'s facing directly (via reflection —
    /// this is a filming tool, not gameplay, same justification as <see cref="PressKitDirector"/>'s
    /// own reflective <c>SpawnOne</c> call) is required because <see cref="WaterBlaster"/> re-derives
    /// its own transform's rotation from <c>aimSource.Facing</c> every frame while an aim source is
    /// bound (<c>PlayerController._facing</c> defaults to <c>Vector3.forward</c>, never zero) — just
    /// writing Max's transform.rotation externally would be stomped back the very next frame.
    /// </summary>
    public sealed class WaterGroundTrailCaptureDirector : MonoBehaviour
    {
        private const int OutW = 1133;
        private const int OutH = 744;
        private const int SuperSample = 2;   // render at 2x then downscale — clean AA regardless of URP MSAA
        private const float Pitch = 60f;     // FixedAngleCameraRig's shipped pitch — the actual gameplay angle the report is about
        private const int MaxSettleFrames = 90;

        private const string DoneMarker = "_mv555_done.txt";
        private static readonly string OutDir = @"C:\Dev\MaxVsTheWorlds-Images";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Armed()) return;
            if (FindFirstObjectByType<WaterGroundTrailCaptureDirector>() != null) return;
            new GameObject("WaterGroundTrailCaptureDirector").AddComponent<WaterGroundTrailCaptureDirector>();
        }

        public static bool Armed()
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "-mv555shots", StringComparison.OrdinalIgnoreCase)) return true;
            try { return File.Exists(Path.Combine("Temp", "mv555.arm")); }
            catch { return false; }
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            Directory.CreateDirectory(OutDir);
            Log($"MV-555 capture starting -> {OutDir}");

            DevMode.Enabled = true;
            DevMode.Invincible = true;
            DevMode.InfiniteEnergy = true;
            DevMode.AutoFire = true;

            var cam = Camera.main;
            if (cam == null) { Fail("no Camera.main in the scene"); yield break; }
            if (cam.TryGetComponent<CinemachineBrain>(out var brain)) brain.enabled = false;
            cam.aspect = (float)OutW / OutH;

            // Let the self-installing systems dress the world first (materials, lighting, Max).
            for (int i = 0; i < 4; i++) yield return null;

            var maxGo = GameObject.FindGameObjectWithTag("Player");
            if (maxGo == null) { Fail("no Player-tagged Max in the scene"); yield break; }
            var facingField = typeof(PlayerController).GetField("_facing", BindingFlags.NonPublic | BindingFlags.Instance);
            var player = maxGo.GetComponent<PlayerController>();
            var blaster = maxGo.GetComponent<WaterBlaster>();

            var hud = FindFirstObjectByType<HudController>();
            if (hud != null) hud.gameObject.SetActive(false);

            yield return AimFireAndCapture(cam, maxGo.transform, player, facingField, blaster, Vector3.left, "MV-555-left");
            yield return AimFireAndCapture(cam, maxGo.transform, player, facingField, blaster, Vector3.right, "MV-555-right");

            Finish();
        }

        private IEnumerator AimFireAndCapture(Camera cam, Transform max, PlayerController player,
            FieldInfo facingField, WaterBlaster blaster, Vector3 dir, string name)
        {
            if (player != null && facingField != null) facingField.SetValue(player, dir);

            int frame = 0;
            while (frame < MaxSettleFrames && Vector3.Angle(max.forward, dir) > 1f)
            {
                yield return null;
                frame++;
            }
            for (int i = 0; i < 10; i++) yield return null;   // let the stream + ground trail build up

            float range = blaster != null ? blaster.Range : WaterBlaster.DefaultRange;
            Vector3 focus = max.position; focus.y = 1f;
            var rot = Quaternion.Euler(Pitch, 0f, 0f);
            float distance = Mathf.Max(8f, range * 1.8f);
            cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);

            yield return null;
            Capture(cam, name);
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
            Log("MV-555 capture complete");
        }

        private void Fail(string why)
        {
            LogWarn("MV-555 capture aborted: " + why);
            try { File.WriteAllText(Path.Combine(OutDir, DoneMarker), "fail: " + why + "\n"); }
            catch { /* best effort */ }
        }

        private static void Log(string m) => Debug.Log("[MV555Capture] " + m);
        private static void LogWarn(string m) => Debug.LogWarning("[MV555Capture] " + m);
    }
}
