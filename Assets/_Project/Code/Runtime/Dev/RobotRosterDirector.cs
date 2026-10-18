using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Unity.Cinemachine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
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
    ///
    /// MV-1217 AC2 adds one extra shot at the end of the same run (<see cref="CaptureDreadnoughtPair"/>):
    /// a Dreadnought next to a Bruiser, on the Reef's own floor, at the game's real play-camera pitch —
    /// written to <c>dreadnought_pair.png</c>, reusing this director's scene/camera/writer rather than
    /// standing up a second capture harness for one reference image.
    ///
    /// MV-1224 AC3 adds a second extra shot the same way (<see cref="CaptureSkitterPack"/>): a loose
    /// pack of five Skitters, two of them caught genuinely mid-hop — written to
    /// <c>skitter_pack.png</c>.
    /// </summary>
    public sealed class RobotRosterDirector : MonoBehaviour
    {
        private const int OutW = 1920;
        private const int OutH = 1080;
        private const int SuperSample = 2;   // render at 2x then downscale — clean AA regardless of URP MSAA

        private const float Pitch = 64.88f;
        private const float Distance = 15.81f;

        /// <summary>MV-1217 AC2's own "play-camera framing" — the game's real fixed top-down angle
        /// (same constant/reasoning as <see cref="PressKitDirector"/>'s own <c>FramePitch</c>), not this
        /// director's closer single-robot review angle above.</summary>
        private const float DreadnoughtPairPitch = 72f;
        private const float DreadnoughtPairDistance = 12f;
        private const float DreadnoughtPairSpacing = 3.4f;   // metres between the two robots' centres

        /// <summary>MV-1224 AC3's own framing — much closer than the roster's single-robot review angle
        /// above, since the Skitter's whole body is only 0.4 m and a pack of five needs room to read.</summary>
        private const float SkitterPackPitch = 55f;
        private const float SkitterPackDistance = 5.5f;

        private const string DoneMarker = "_robotroster_done.txt";
        private const string DesignImagesScreensDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens\robot-roster";

        /// <summary>Ticket order (MV-452's description), not the enum's declaration order — cosmetic,
        /// but it makes the fix comment's screenshot list read in the same order Lee wrote the AC.</summary>
        private static readonly EnemyKind[] Roster =
        {
            EnemyKind.Rusher, EnemyKind.Launcher, EnemyKind.Blinker, EnemyKind.Gunner, EnemyKind.Bolter,
            EnemyKind.Sludger, EnemyKind.Charger, EnemyKind.Bruiser, EnemyKind.Heavy, EnemyKind.Brute,
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

            yield return CaptureDreadnoughtPair(cam, focusBase);
            yield return CaptureSkitterPack(cam, focusBase);

            Finish();
        }

        /// <summary>MV-1217 AC2 — the Dreadnought next to a Bruiser, on the Reef's own floor, at
        /// play-camera framing: one extra shot appended to this director's existing run rather than a
        /// whole second capture harness, since it needs nothing this one doesn't already have (a loaded
        /// scene, a camera, the same <see cref="BuildRobot"/> greybox-then-rig recipe, the same PNG
        /// writer).
        ///
        /// "The Reef floor" is a small plane dropped in under just these two robots, wearing
        /// <see cref="WorldMaterials.M_ShipFloor"/> directly — the exact deck material World 3 itself
        /// renders with. Deliberately NOT done by swapping the whole scene's <see cref="MaterialLibrary.Palette"/>
        /// to <see cref="BiomePalette.Reef"/>: tried first, and it rendered this whole shot MAGENTA — the
        /// scene's own ground/props already built their materials against the loaded Backyard palette by
        /// the time this director's Start() runs several frames in, and <see cref="MaterialLibrary.Palette"/>'s
        /// setter unconditionally DESTROYS every cached material on a change, including the ones those
        /// still-live renderers point at (exactly the failure that class's own doc comment warns about —
        /// "a material that is destroyed while a renderer is still pointing at it renders MAGENTA").
        /// <see cref="WorldMaterials.M_ShipFloor"/> is a different, independent cache
        /// (<c>WorldMaterials.s_reefCache</c>) that never touches <see cref="MaterialLibrary"/>'s own, so
        /// dropping one plane in wearing it is safe regardless of what the rest of the loaded scene has
        /// already built.</summary>
        private IEnumerator CaptureDreadnoughtPair(Camera cam, Vector3 focusBase)
        {
            GameObject bruiser = null, dreadnought = null, floor = null;
            try
            {
                Vector3 bruiserPos = focusBase + Vector3.left * (DreadnoughtPairSpacing * 0.5f);
                Vector3 dreadnoughtPos = focusBase + Vector3.right * (DreadnoughtPairSpacing * 0.5f);

                floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "MV1217 Reef Floor";
                // Default Unity plane is 10x10 world metres — comfortably covers both robots plus
                // framing margin with no scaling needed. A hair above the scene's own ground (y=0) so
                // it draws cleanly on top rather than z-fighting whatever is already there.
                floor.transform.position = new Vector3(focusBase.x, 0.01f, focusBase.z);
                Material shipFloor = WorldMaterials.M_ShipFloor;
                if (shipFloor != null) floor.GetComponent<MeshRenderer>().sharedMaterial = shipFloor;
                var floorCollider = floor.GetComponent<Collider>();
                if (floorCollider != null) Destroy(floorCollider);   // cosmetic only — no physics needed

                bruiser = BuildRobot(EnemyKind.Bruiser, bruiserPos);
                dreadnought = BuildRobot(EnemyKind.Dreadnought, dreadnoughtPos);
                for (int i = 0; i < 3; i++) yield return null;   // let both rigs' build settle on screen

                var rot = Quaternion.Euler(DreadnoughtPairPitch, 0f, 0f);
                Vector3 focus = (bruiserPos + dreadnoughtPos) * 0.5f;
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * DreadnoughtPairDistance, rot);

                yield return null;
                yield return null;
                Capture(cam, "dreadnought_pair");
            }
            finally
            {
                if (bruiser != null) Destroy(bruiser);
                if (dreadnought != null) Destroy(dreadnought);
                if (floor != null) Destroy(floor);
            }
        }

        /// <summary>MV-1224 AC3 — a loose pack of five Skitters (the ticket's own "packs of 4-6"),
        /// scattered rather than in formation (a hop pack never arrives tidy), with two of them caught
        /// GENUINELY mid-hop rather than hand-posed: each is repositioned to its own "landing" spot and
        /// then fired through the exact same <see cref="HudSignals.BlinkerTeleported"/> beat a live
        /// Skitter hop emits (<c>RobotEnemy.TickTeleport</c>'s own "the reposition is instant with
        /// nothing to see — HudSignals carries both points so the VFX layer can play the beat without
        /// this state machine knowing or caring what it looks like"), the same reuse the ticket's own
        /// "the Blinker already teleports; reuse its pattern" asks for. Capturing two frames later
        /// catches <see cref="RobotRig"/>'s own expand-from-zero body scale and
        /// <see cref="MaxWorlds.VFX.CombatVfx"/>'s surge/flash beat still live — the shot is of the hop
        /// actually happening, not a still life.</summary>
        private IEnumerator CaptureSkitterPack(Camera cam, Vector3 focusBase)
        {
            var pack = new List<GameObject>(6);
            try
            {
                Vector2[] offsets =
                {
                    new Vector2(-1.4f, -0.6f), new Vector2(-0.6f, 0.4f), new Vector2(0.2f, -0.3f),
                    new Vector2(0.9f, 0.6f), new Vector2(1.6f, -0.2f),
                };

                foreach (Vector2 off in offsets)
                {
                    Vector3 pos = focusBase + new Vector3(off.x, 0f, off.y);
                    pack.Add(BuildRobot(EnemyKind.Skitter, pos));
                }
                for (int i = 0; i < 3; i++) yield return null;   // let every rig's build settle on screen

                for (int i = 0; i < pack.Count; i += 2)
                {
                    Transform t = pack[i].transform;
                    Vector3 from = t.position;
                    Vector3 to = from + new Vector3(0.6f, 0f, 0.6f);
                    t.position = to;
                    HudSignals.EmitBlinkerTeleported(from, to);
                }

                yield return null;
                yield return null;

                var rot = Quaternion.Euler(SkitterPackPitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focusBase - rot * Vector3.forward * SkitterPackDistance, rot);

                yield return null;
                Capture(cam, "skitter_pack");
            }
            finally
            {
                foreach (GameObject go in pack) if (go != null) Destroy(go);
            }
        }

        /// <summary>The middle of the largest <see cref="ZoneKind.Open"/> room on the loaded map, or
        /// null if no map is loaded (falls back to a spot near Max). That is deliberately "the fight
        /// room" (<see cref="MapZone"/>'s own doc comment) rather than any zone at all — an Entry room
        /// is exactly the dressed, decoration-heavy one this method exists to avoid. Internal so
        /// <see cref="MsaaComparisonDirector"/> (MV-507) can frame on the same spot instead of
        /// duplicating the zone search.</summary>
        internal static Vector3? OpenZoneCenter()
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
        /// one-shot capture doesn't need. Internal static so <see cref="MsaaComparisonDirector"/>
        /// (MV-507) builds the exact same body rather than a hand-simplified stand-in.</summary>
        internal static GameObject BuildRobot(EnemyKind kind, Vector3 at)
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
            e.SetCutsceneFrozen(true);   // MV-1081: hold the pose — no player in this shot for it to chase

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
