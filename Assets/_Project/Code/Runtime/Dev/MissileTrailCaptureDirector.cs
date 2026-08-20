using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Unity.Cinemachine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// One 1920x1080 screenshot of a <see cref="HomingMissile"/> mid-flight, trail visible (MV-508
    /// AC6 — eyes-on, judged by Lee). Same INERT-unless-armed shape as
    /// <see cref="RobotRosterDirector"/>/<see cref="PressKitDirector"/>: installs only behind the
    /// <c>-missiletrail</c> flag or a <c>Temp/missiletrail.arm</c> marker. Capture technique is
    /// <see cref="PressKitDirector"/>'s manual <c>Camera.Render()</c> + supersample-then-blit.
    ///
    /// Fires a real missile at a stationary stand-in target near Max rather than trying to catch one
    /// from a live Launcher — waiting on a Launcher to spawn, notice Max and fire is neither reliable
    /// nor fast in an unattended capture, and the shot only needs to prove the trail reads, not that
    /// the AI decided to fire.
    /// </summary>
    public sealed class MissileTrailCaptureDirector : MonoBehaviour
    {
        private const int OutW = 1920;
        private const int OutH = 1080;
        private const int SuperSample = 2;   // render at 2x then downscale — clean AA regardless of URP MSAA

        // Ticket's own words: "a 1920x1080 capture of a missile in flight at the 60° camera" — the
        // same closer, hero-shot pitch PressKitDirector's combat/boss frames use (60-62°), distinct
        // from the shipped gameplay rig's fixed 72°.
        private const float Pitch = 60f;

        // Tight on purpose: the trail itself is deliberately short (a motion smear, not a ribbon —
        // see HomingMissile.BuildTrail), so a wide establishing shot would leave it too small on a
        // 1920x1080 frame to eyeball. This is a close-up for AC6's judgment call, not the shipped
        // gameplay framing.
        private const float Distance = 3f;

        /// <summary>Metres of travel before the shot — enough for a visible trail (the trail's own
        /// memory window is 0.12s), short enough that even a fast, wide miss on frame count still
        /// leaves the missile inside the open zone rather than off across the map.</summary>
        private const float TravelDistanceForShot = 1.2f;
        private const int MaxSettleFrames = 300;

        private const string DoneMarker = "_missiletrail_done.txt";
        private const string DesignImagesPath = @"C:\Dev\MaxVsTheWorlds-Images\MV-508-missile-trail.png";

        private string _outDir;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Armed()) return;
            if (FindFirstObjectByType<MissileTrailCaptureDirector>() != null) return;
            new GameObject("MissileTrailCaptureDirector").AddComponent<MissileTrailCaptureDirector>();
        }

        public static bool Armed()
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "-missiletrail", StringComparison.OrdinalIgnoreCase)) return true;
            try { return File.Exists(Path.Combine("Temp", "missiletrail.arm")); }
            catch { return false; }
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            _outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press", "missile-trail"));
            Directory.CreateDirectory(_outDir);
            Log($"missile-trail capture starting → {_outDir}");

            var cam = Camera.main;
            if (cam == null) { Fail("no Camera.main in the scene"); yield break; }
            if (cam.TryGetComponent<CinemachineBrain>(out var brain)) brain.enabled = false;
            cam.aspect = (float)OutW / OutH;

            // Let the self-installing systems dress the world first (materials, lighting, Max).
            for (int i = 0; i < 4; i++) yield return null;

            // The largest Open zone on the map (same helper RobotRosterDirector uses) rather than
            // Max's raw spawn point — the entry room is dressed with hedges/flower beds right up to
            // the doorway, which buried the first cut of this shot in scenery with no missile visible.
            var max = GameObject.FindGameObjectWithTag("Player");
            Vector3 focus = OpenZoneCenter()
                ?? (max != null ? max.transform.position + max.transform.forward * 6f : Vector3.forward * 6f);

            Vector3 origin = focus + new Vector3(-4f, 0f, 0f);
            // Far past any reasonable frame so a huge single headless-editor frame spike (this
            // project's own perf gate has logged single frames over 2s of real time) can't tunnel the
            // missile clean through its contact radius and self-destroy before the shot is framed.
            Vector3 targetPos = focus + new Vector3(500f, 0f, 0f);
            var fakeTarget = new GameObject("MissileTrailCapture_FakeTarget");
            fakeTarget.transform.position = targetPos;

            HomingMissile missile = HomingMissile.Fire(origin, fakeTarget.transform, speed: 4.5f, damage: 1f,
                splashRadius: 1f);

            // Wait for a fixed TRAVEL DISTANCE rather than a fixed frame count: a real-time headless
            // Update can spike well past a normal 16ms frame (see the comment above), so counting
            // frames doesn't bound how far the missile has actually flown by the time of the shot.
            int frame = 0;
            while (missile != null && frame < MaxSettleFrames &&
                   Vector3.Distance(missile.transform.position, origin) < TravelDistanceForShot)
            {
                yield return null;
                frame++;
            }

            if (missile == null)
            {
                Fail("the missile detonated (geometry or contact) before it had travelled far enough to frame");
                Destroy(fakeTarget);
                yield break;
            }

            // Frame and render on the SAME tick the distance check passed — any further yield here
            // would let the missile move again before Capture()'s manual cam.Render() actually fires,
            // reintroducing the exact staleness this rewrite exists to remove.
            var rot = Quaternion.Euler(Pitch, 0f, 0f);
            Vector3 mp = missile.transform.position;
            cam.transform.SetPositionAndRotation(mp - rot * Vector3.forward * Distance, rot);

            // The trail's own memory window (TrailTime, 0.12s) is exactly right for real 60fps
            // gameplay but is at the mercy of this headless capture process's real-time frame pacing
            // — this project's own perf gate has logged single batchmode frames spiking past 2
            // real seconds, which is long enough for the trail to age itself out to nothing between
            // one Update and the next. Seed it explicitly along the flight path immediately before the
            // shot — same TrailRenderer, same width/colour/material the live one builds, just placed
            // deterministically so the screenshot isn't at the mercy of that variance.
            var trail = missile.GetComponent<TrailRenderer>();
            if (trail != null)
            {
                trail.Clear();
                Vector3 back = -missile.transform.forward;
                for (int i = 6; i >= 0; i--) trail.AddPosition(mp + back * (0.08f * i));
            }

            Capture(cam, "missile-in-flight");

            if (missile != null) Destroy(missile.gameObject);
            Destroy(fakeTarget);

            Finish();
        }

        /// <summary>The middle of the largest <see cref="ZoneKind.Open"/> room on the loaded map, or
        /// null if no map is loaded — same helper and same reasoning as
        /// <see cref="RobotRosterDirector"/>'s own copy: an Entry room is dressed with hedges/flower
        /// beds right up to the doorway, exactly the decoration this shot needs to avoid.</summary>
        private static Vector3? OpenZoneCenter()
        {
            var path = FindFirstObjectByType<MaxWorlds.Arena.BackyardPath>();
            if (path == null || path.Map == null || path.Map.zones == null) return null;

            MaxWorlds.Arena.MapZone best = null;
            foreach (var z in path.Map.zones)
            {
                if (z == null || z.Kind != MaxWorlds.Arena.ZoneKind.Open) continue;
                if (best == null || z.width * z.depth > best.width * best.depth) best = z;
            }
            return best?.Center;
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
                File.WriteAllBytes(Path.Combine(_outDir, name + ".png"), png);
                Log($"wrote {name}.png");

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(DesignImagesPath));
                    File.WriteAllBytes(DesignImagesPath, png);
                    Log($"wrote {DesignImagesPath}");
                }
                catch (Exception e) { LogWarn($"couldn't write {DesignImagesPath}: {e.Message}"); }
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
            File.WriteAllText(Path.Combine(_outDir, DoneMarker), "ok\n");
            Log("missile-trail capture complete");
        }

        private void Fail(string why)
        {
            LogWarn("missile-trail capture aborted: " + why);
            try { File.WriteAllText(Path.Combine(_outDir ?? ".", DoneMarker), "fail: " + why + "\n"); }
            catch { /* best effort */ }
        }

        private static void Log(string m) => Debug.Log("[MissileTrailCapture] " + m);
        private static void LogWarn(string m) => Debug.LogWarning("[MissileTrailCapture] " + m);
    }
}
