using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using Unity.Cinemachine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// Renders one 1920x1080 screenshot per Backyard robot kind (MV-452) at the review framing Lee
    /// dialled in with the MV-450 dev panel — pitch 64.88° / distance 15.81 m, approved 2026-08-19
    /// (see MV-468, which bakes it as the shipped default; not yet landed, so this director sets the
    /// numbers itself rather than trusting the scene's committed 72°/~24.65 m rig).
    ///
    /// Same INERT-unless-armed shape as <see cref="PressKitDirector"/>/<see cref="UiScreensDirector"/>:
    /// installs only behind the <c>-robotroster</c> flag or a <c>Temp/robotroster.arm</c> marker, and
    /// its own marker keeps it from colliding with either of those on the same run. Capture technique
    /// is <see cref="PressKitDirector"/>'s manual <c>Camera.Render()</c> + supersample-then-blit — these
    /// are 3D world actors, not <see cref="UiScreensDirector"/>'s screen-space canvases.
    ///
    /// Each kind is built the same greybox-then-Apply way <c>EnemySpawner.CreateInstance</c> does (a
    /// primitive sized by <see cref="EnemyArchetype.BodyScale"/>, a matching <see cref="CharacterController"/>,
    /// <see cref="RobotEnemy.Apply"/> for the stats, <see cref="RobotRig"/> for the body — it builds
    /// synchronously in its own Awake) so what's on screen is exactly what the live spawn path produces,
    /// not a hand-simplified stand-in. <see cref="RobotEnemy"/> is disabled immediately after — there is
    /// no Max in this shot for it to chase, and a robot that drifted mid-capture would blur the silhouette
    /// the ticket is judging.
    /// </summary>
    public sealed class RobotRosterDirector : MonoBehaviour
    {
        private const int OutW = 1920;
        private const int OutH = 1080;
        private const int SuperSample = 2;   // render at 2x then downscale — clean AA regardless of URP MSAA

        private const float Pitch = 64.88f;
        private const float Distance = 15.81f;

        private const string DoneMarker = "_robotroster_done.txt";
        private const string DesignImagesScreensDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens\robot-roster";

        /// <summary>Ticket order (MV-452's description), not the enum's declaration order — cosmetic,
        /// but it makes the fix comment's screenshot list read in the same order Lee wrote the AC.</summary>
        private static readonly EnemyKind[] Roster =
        {
            EnemyKind.Rusher, EnemyKind.Launcher, EnemyKind.Blinker, EnemyKind.Gunner,
            EnemyKind.Bruiser, EnemyKind.Heavy, EnemyKind.Brute,
        };

        private string _outDir;
        private string _outDir2;
        private readonly StringBuilder _manifest = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Armed()) return;
            if (FindFirstObjectByType<RobotRosterDirector>() != null) return;
            new GameObject("RobotRosterDirector").AddComponent<RobotRosterDirector>();
        }

        public static bool Armed()
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "-robotroster", StringComparison.OrdinalIgnoreCase)) return true;
            try { return File.Exists(Path.Combine("Temp", "robotroster.arm")); }
            catch { return false; }
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            _outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press", "robot-roster"));
            Directory.CreateDirectory(_outDir);
            try { Directory.CreateDirectory(DesignImagesScreensDir); _outDir2 = DesignImagesScreensDir; }
            catch (Exception e) { LogWarn($"secondary output dir unavailable ({DesignImagesScreensDir}): {e.Message}"); _outDir2 = null; }

            Log($"robot-roster capture starting → {_outDir}" + (_outDir2 != null ? $" (+ {_outDir2})" : ""));

            var cam = Camera.main;
            if (cam == null) { Fail("no Camera.main in the scene"); yield break; }
            if (cam.TryGetComponent<CinemachineBrain>(out var brain)) brain.enabled = false;
            cam.aspect = (float)OutW / OutH;

            // Stand in the middle of the largest OPEN zone — "the fight room" per MapZone's own doc
            // comment — not near wherever Max spawned: the entry room is dressed with hedges/flower
            // beds right up to the doorway (BackyardDressing follows the map's walls), and the first
            // pass of this capture spawned every kind inside that border planting, hiding the whole
            // silhouette behind foliage.
            var max = GameObject.FindGameObjectWithTag("Player");
            Vector3 focusBase = OpenZoneCenter()
                ?? (max != null ? max.transform.position + max.transform.forward * 6f : Vector3.forward * 6f);

            // Let the self-installing systems dress the world first (materials, lighting, Max).
            for (int i = 0; i < 4; i++) yield return null;

            GameObject current = null;
            foreach (var kind in Roster)
            {
                if (current != null) Destroy(current);
                current = BuildRobot(kind, focusBase);
                for (int i = 0; i < 3; i++) yield return null;   // let the rig's build settle on screen

                var rot = Quaternion.Euler(Pitch, 0f, 0f);
                Vector3 focus = current.transform.position;
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * Distance, rot);

                yield return null;
                yield return null;
                Capture(cam, kind.ToString());
            }
            if (current != null) Destroy(current);

            Finish();
        }

        /// <summary>The middle of the largest <see cref="ZoneKind.Open"/> room on the loaded map, or
        /// null if no map is loaded (falls back to a spot near Max). That is deliberately "the fight
        /// room" (<see cref="MapZone"/>'s own doc comment) rather than any zone at all — an Entry room
        /// is exactly the dressed, decoration-heavy one this method exists to avoid.</summary>
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

        /// <summary>Same greybox recipe as <c>EnemySpawner.CreateInstance</c>'s no-prefab path (that IS
        /// the shipped path — <c>prefab</c> is unset until Phase C art), minus the pooling/parenting this
        /// one-shot capture doesn't need.</summary>
        private GameObject BuildRobot(EnemyKind kind, Vector3 at)
        {
            EnemyArchetype a = EnemyArchetype.Of(kind);
            var go = GameObject.CreatePrimitive(a.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
            go.name = $"RobotRoster {kind}";
            go.transform.SetPositionAndRotation(new Vector3(at.x, a.SpawnHeight, at.z), Quaternion.identity);
            go.transform.localScale = a.BodyScale;

            var cc = go.AddComponent<CharacterController>();
            float lateral = Mathf.Max(a.BodyScale.x, a.BodyScale.z);
            cc.height = a.ColliderHeight / Mathf.Max(a.BodyScale.y, 1e-4f);
            cc.radius = a.ColliderRadius / Mathf.Max(lateral, 1e-4f);
            cc.center = Vector3.zero;

            var e = go.AddComponent<RobotEnemy>();
            e.Apply(a);
            e.enabled = false;   // hold the pose — no player in this shot for it to chase

            go.AddComponent<RobotRig>();   // builds the body synchronously, in its own Awake

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
            Log("robot-roster capture complete");
        }

        private void Fail(string why)
        {
            LogWarn("robot-roster capture aborted: " + why);
            try { File.WriteAllText(Path.Combine(_outDir ?? ".", DoneMarker), "fail: " + why + "\n"); }
            catch { /* best effort */ }
        }

        private static void Log(string m) => Debug.Log("[RobotRoster] " + m);
        private static void LogWarn(string m) => Debug.LogWarning("[RobotRoster] " + m);
    }
}
