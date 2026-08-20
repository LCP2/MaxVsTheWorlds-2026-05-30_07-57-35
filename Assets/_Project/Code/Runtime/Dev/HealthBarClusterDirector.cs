using System;
using System.Collections;
using System.IO;
using Unity.Cinemachine;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.UI;
using MaxWorlds.CameraRig;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// Renders one 1920x1080 evidence shot of a clustered robot pack (MV-473's AC: "a screenshot of a
    /// 4+ robot cluster at 1920x1080") — three Rushers and a Bruiser stood shoulder to shoulder, the
    /// same mix the ticket's own evidence screenshots showed stacking. <c>cc-screens.bat</c> (MV-441)
    /// only films THE RIG board and the WEAPONS button; it has no path to a live gameplay HUD element,
    /// so this is its own small capture, same INERT-unless-armed / self-installing shape as
    /// <see cref="RobotRosterDirector"/> (which this is a trimmed sibling of — one shot, not a roster
    /// loop) and the same manual <c>Camera.Render()</c> + supersample-then-blit technique.
    ///
    /// Reads the shipped framing (pitch/distance) straight off the scene's own <see cref="FixedAngleCameraRig"/>
    /// rather than a second hard-coded copy of those numbers — MV-452's RobotRosterDirector had to
    /// hard-code them because MV-468 hadn't landed yet when it was written; it has now, so there is no
    /// reason this one should drift from whatever the rig actually ships.
    /// </summary>
    public sealed class HealthBarClusterDirector : MonoBehaviour
    {
        private const int OutW = 1920;
        private const int OutH = 1080;
        private const int SuperSample = 2;

        /// <summary>How close together the cluster stands, centre to centre — inside
        /// <see cref="WorldHealthBarDeclutter"/>'s own ClusterRadius, so the shot actually exercises the
        /// de-clutter pass rather than showing four bars that were never going to touch.</summary>
        private const float ClusterSpacing = 1.1f;

        /// <summary>Extra, scattered population spawned off-frame (MV-473 AC: "measured frame time at
        /// peak population reported") — brings the live count up to <see cref="EnemySpawner.GlobalMaxLiveEnemies"/>
        /// so <see cref="WorldHealthBarDeclutter.LastResolveMicroseconds"/> reflects the real worst case,
        /// not just the four robots actually framed.</summary>
        private static readonly EnemyKind[] ClusterMix =
        {
            EnemyKind.Rusher, EnemyKind.Rusher, EnemyKind.Rusher, EnemyKind.Bruiser,
        };

        private const string DoneMarker = "_healthbarcluster_done.txt";
        private const string DesignImagesScreensDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens\health-bar-cluster";

        private string _outDir;
        private string _outDir2;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Armed()) return;
            if (FindFirstObjectByType<HealthBarClusterDirector>() != null) return;
            new GameObject("HealthBarClusterDirector").AddComponent<HealthBarClusterDirector>();
        }

        public static bool Armed()
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "-healthbarcluster", StringComparison.OrdinalIgnoreCase)) return true;
            try { return File.Exists(Path.Combine("Temp", "healthbarcluster.arm")); }
            catch { return false; }
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            _outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press", "health-bar-cluster"));
            Directory.CreateDirectory(_outDir);
            try { Directory.CreateDirectory(DesignImagesScreensDir); _outDir2 = DesignImagesScreensDir; }
            catch (Exception e) { LogWarn($"secondary output dir unavailable ({DesignImagesScreensDir}): {e.Message}"); _outDir2 = null; }

            Log($"health-bar-cluster capture starting → {_outDir}" + (_outDir2 != null ? $" (+ {_outDir2})" : ""));

            var cam = Camera.main;
            if (cam == null) { Fail("no Camera.main in the scene"); yield break; }
            if (cam.TryGetComponent<CinemachineBrain>(out var brain)) brain.enabled = false;
            cam.aspect = (float)OutW / OutH;

            var rig = FindFirstObjectByType<FixedAngleCameraRig>();
            float pitch = rig != null ? rig.Pitch : 60f;
            float distance = rig != null ? rig.Distance : 24.284037f;
            if (rig == null) LogWarn("no FixedAngleCameraRig found — falling back to the shipped default (60deg / 24.284m)");

            var max = GameObject.FindGameObjectWithTag("Player");
            Vector3 focusBase = OpenZoneCenter()
                ?? (max != null ? max.transform.position + max.transform.forward * 6f : Vector3.forward * 6f);

            for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

            // The four framed robots, tight enough to trip the de-clutter pass.
            GameObject[] cluster = new GameObject[ClusterMix.Length];
            for (int i = 0; i < ClusterMix.Length; i++)
            {
                Vector3 at = focusBase + OffsetFor(i) * ClusterSpacing;
                cluster[i] = BuildRobot(ClusterMix[i], at);
            }

            // Scatter the rest of the live cap well outside the frame — present for the timing
            // measurement below, absent from the shot itself.
            int scattered = Mathf.Max(0, EnemySpawner.GlobalMaxLiveEnemies - ClusterMix.Length);
            GameObject[] offscreen = new GameObject[scattered];
            for (int i = 0; i < scattered; i++)
            {
                Vector3 at = focusBase + new Vector3(60f + i * 2f, 0f, 60f);
                offscreen[i] = BuildRobot(i % 4 == 0 ? EnemyKind.Bruiser : EnemyKind.Rusher, at);
            }

            for (int i = 0; i < 3; i++) yield return null;   // let the rigs build and the declutter pass settle

            Vector3 focus = focusBase;
            var rot = Quaternion.Euler(pitch, 0f, 0f);
            cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);

            yield return null;
            yield return null;

            double resolveMicros = WorldHealthBarDeclutter.LastResolveMicroseconds;
            int liveCount = WorldHealthBar.LastShowingCount;

            Capture(cam, "cluster");

            foreach (var go in cluster) if (go != null) Destroy(go);
            foreach (var go in offscreen) if (go != null) Destroy(go);

            Finish(pitch, distance, liveCount, resolveMicros);
        }

        private static Vector3 OffsetFor(int i) => i switch
        {
            0 => new Vector3(-0.6f, 0f, 0.4f),
            1 => new Vector3(0.6f, 0f, 0.4f),
            2 => new Vector3(0f, 0f, -0.5f),
            _ => new Vector3(0f, 0f, 0.6f),   // the Bruiser, front and centre — largest silhouette closest to camera
        };

        private static Vector3? OpenZoneCenter()
        {
            var path = FindFirstObjectByType<BackyardPath>();
            if (path == null || path.Map == null || path.Map.zones == null) return null;

            MapZone best = null;
            foreach (var z in path.Map.zones)
            {
                if (z == null || z.Kind != ZoneKind.Open) continue;
                if (best == null || z.width * z.depth > best.width * best.depth) best = z;
            }
            return best?.Center;
        }

        /// <summary>Same greybox recipe <see cref="RobotRosterDirector"/> uses — the shipped no-prefab
        /// spawn path, minus pooling this one-shot capture doesn't need.</summary>
        private GameObject BuildRobot(EnemyKind kind, Vector3 at)
        {
            EnemyArchetype a = EnemyArchetype.Of(kind);
            var go = GameObject.CreatePrimitive(a.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
            go.name = $"HealthBarCluster {kind}";
            go.transform.SetPositionAndRotation(new Vector3(at.x, a.SpawnHeight, at.z), Quaternion.identity);
            go.transform.localScale = a.BodyScale;

            var cc = go.AddComponent<CharacterController>();
            float lateral = Mathf.Max(a.BodyScale.x, a.BodyScale.z);
            cc.height = a.ColliderHeight / Mathf.Max(a.BodyScale.y, 1e-4f);
            cc.radius = a.ColliderRadius / Mathf.Max(lateral, 1e-4f);
            cc.center = Vector3.zero;

            var e = go.AddComponent<RobotEnemy>();
            e.Apply(a);
            e.enabled = false;   // hold the pose — nothing in this shot for it to chase

            go.AddComponent<MaxWorlds.VFX.RobotRig>();

            return go;
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

        private void Finish(float pitch, float distance, int liveCount, double resolveMicros)
        {
            string report = "ok\ncluster.png\n" +
                $"pitch={pitch:F2}deg distance={distance:F3}m\n" +
                $"declutter pass at {liveCount} live robots: {resolveMicros:F1} microseconds\n";
            File.WriteAllText(Path.Combine(_outDir, DoneMarker), report);
            Log("health-bar-cluster capture complete. " + report);
        }

        private void Fail(string why)
        {
            LogWarn("health-bar-cluster capture aborted: " + why);
            try { File.WriteAllText(Path.Combine(_outDir ?? ".", DoneMarker), "fail: " + why + "\n"); }
            catch { /* best effort */ }
        }

        private static void Log(string m) => Debug.Log("[HealthBarCluster] " + m);
        private static void LogWarn(string m) => Debug.LogWarning("[HealthBarCluster] " + m);
    }
}
