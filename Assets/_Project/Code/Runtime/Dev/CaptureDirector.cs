using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Cinemachine;
using UnityEngine;
using static UnityEngine.Object;
using MaxWorlds.Arena;
using MaxWorlds.CameraRig;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Dev
{
    /// <summary>Thrown by a <see cref="CapturePreset"/>'s <c>Prepare</c>/shot setup to abort the run
    /// with a reason, in place of the old per-director "Fail(why); yield break;" idiom.</summary>
    public sealed class CaptureAbortException : Exception
    {
        public CaptureAbortException(string reason) : base(reason) { }
    }

    /// <summary>One screenshot to take once a preset's <c>Prepare</c> has run: a name, a callback that
    /// positions the camera / mutates world state for exactly this shot, and optional extra absolute
    /// paths to mirror the PNG to verbatim (for the odd fixed-filename design-review copy).</summary>
    public sealed class CaptureShot
    {
        public readonly string Name;
        public readonly Func<Camera, IEnumerator> Setup;
        public readonly string[] MirrorPaths;

        public CaptureShot(string name, Func<Camera, IEnumerator> setup, string[] mirrorPaths = null)
        {
            Name = name;
            Setup = setup;
            MirrorPaths = mirrorPaths;
        }
    }

    /// <summary>Everything one capture invocation needs: the flag/marker that arms it, where it
    /// writes, how big, which scene, and the shot(s) to take. A visual ticket supplies one of these
    /// to <see cref="CapturePresets"/> instead of writing a new director/entry-point pair (MV-592) —
    /// see CC_AUTONOMY.md's guardrail on new files under Runtime/Dev or Editor.</summary>
    public sealed class CapturePreset
    {
        public string Key;
        public string LogTag;
        public string Flag;
        public string ArmFile;
        public string HeadlessMarker;
        public string DoneFileName;
        public string Scene = "Assets/_Project/Scenes/Backyard_Slice.unity";
        public int Width;
        public int Height;
        public int SuperSample = 2;
        public bool DisableBrain = true;
        public string[] OutputDirs;
        public double TimeoutSeconds = 90;

        /// <summary>Runs once, before scene load — for state (like RigState unlocks) that has to be in
        /// place before some other system's own Awake bakes a decision from it.</summary>
        public Action BeforeSceneLoad;

        /// <summary>Runs once after the camera/aspect/brain are set up, before the shot loop.</summary>
        public Func<Camera, IEnumerator> Prepare;

        public List<CaptureShot> Shots;

        /// <summary>Best-effort teardown after every shot has been captured.</summary>
        public Action Cleanup;

        /// <summary>Extra lines appended to the done-marker report (diagnostics only — nothing parses
        /// past the leading "ok"/"fail" token, so this is log fidelity, not behaviour).</summary>
        public Func<string> ExtraReport;
    }

    /// <summary>The single reusable capture director (MV-592) that supersedes the per-ticket
    /// director/entry-point pairs that used to accumulate one pair per human-check screenshot AC.
    /// Self-installs only when a <see cref="CapturePresets"/> entry is armed (its command-line flag or
    /// <c>Temp/*.arm</c> marker is present), otherwise never touches the game — same INERT-unless-armed
    /// contract every one of the old directors had. Positions <c>Camera.main</c>, renders to a
    /// supersampled <see cref="RenderTexture"/>, and writes PNG(s), driven entirely by the armed
    /// preset's data and callbacks.</summary>
    public sealed class CaptureDirector : MonoBehaviour
    {
        private CapturePreset _preset;
        private string _failReason;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RunBeforeSceneLoadHooks()
        {
            foreach (var preset in CapturePresets.All.Values)
                if (preset.BeforeSceneLoad != null && IsArmed(preset)) preset.BeforeSceneLoad();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var preset = ArmedPreset();
            if (preset == null) return;
            if (FindFirstObjectByType<CaptureDirector>() != null) return;
            var director = new GameObject("CaptureDirector:" + preset.Key).AddComponent<CaptureDirector>();
            director._preset = preset;
        }

        private static CapturePreset ArmedPreset()
        {
            foreach (var preset in CapturePresets.All.Values)
                if (IsArmed(preset)) return preset;
            return null;
        }

        private static bool IsArmed(CapturePreset preset)
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, preset.Flag, StringComparison.OrdinalIgnoreCase)) return true;
            try { return File.Exists(preset.ArmFile); } catch { return false; }
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            var preset = _preset;
            var liveDirs = new List<string>();
            foreach (var dir in preset.OutputDirs)
            {
                try { Directory.CreateDirectory(dir); liveDirs.Add(dir); }
                catch (Exception e)
                {
                    if (liveDirs.Count == 0) { Fail(preset, liveDirs, $"couldn't create output dir {dir}: {e.Message}"); yield break; }
                    LogWarn(preset, $"secondary output dir unavailable ({dir}): {e.Message}");
                }
            }
            Log(preset, $"{preset.Key} capture starting -> {liveDirs[0]}" + (liveDirs.Count > 1 ? $" (+{liveDirs.Count - 1} more)" : ""));

            var cam = Camera.main;
            if (cam == null) { Fail(preset, liveDirs, "no Camera.main in the scene"); yield break; }
            if (preset.DisableBrain && cam.TryGetComponent<CinemachineBrain>(out var brain)) brain.enabled = false;
            cam.aspect = (float)preset.Width / preset.Height;

            if (preset.Prepare != null)
            {
                yield return Drive(preset.Prepare(cam));
                if (_failReason != null) { Fail(preset, liveDirs, _failReason); yield break; }
            }

            foreach (var shot in preset.Shots)
            {
                yield return Drive(shot.Setup(cam));
                if (_failReason != null) { Fail(preset, liveDirs, _failReason); yield break; }
                Capture(preset, liveDirs, cam, shot);
            }

            preset.Cleanup?.Invoke();
            Finish(preset, liveDirs);
        }

        /// <summary>Pumps a nested setup/prepare IEnumerator, catching a <see cref="CaptureAbortException"/>
        /// into <see cref="_failReason"/> instead of letting it fault the whole coroutine — a plain
        /// try/catch can't wrap a yield, so the MoveNext() driving happens inside the try and the yield
        /// happens outside it.</summary>
        private IEnumerator Drive(IEnumerator inner)
        {
            while (true)
            {
                bool more;
                try { more = inner.MoveNext(); }
                catch (CaptureAbortException ex) { _failReason = ex.Message; yield break; }
                if (!more) yield break;
                yield return inner.Current;
            }
        }

        private void Capture(CapturePreset preset, List<string> liveDirs, Camera cam, CaptureShot shot)
        {
            int rw = preset.Width * preset.SuperSample, rh = preset.Height * preset.SuperSample;
            var rt = new RenderTexture(rw, rh, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var small = new RenderTexture(preset.Width, preset.Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(preset.Width, preset.Height, TextureFormat.RGB24, false);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prevTarget;

                Graphics.Blit(rt, small);
                RenderTexture.active = small;
                tex.ReadPixels(new Rect(0, 0, preset.Width, preset.Height), 0, 0);
                tex.Apply();

                byte[] png = tex.EncodeToPNG();
                for (int i = 0; i < liveDirs.Count; i++)
                {
                    string path = Path.Combine(liveDirs[i], shot.Name + ".png");
                    try
                    {
                        File.WriteAllBytes(path, png);
                        if (i == 0) Log(preset, $"wrote {shot.Name}.png");
                    }
                    catch (Exception e)
                    {
                        if (i == 0) throw;
                        LogWarn(preset, $"secondary write failed for {shot.Name}: {e.Message}");
                    }
                }

                if (shot.MirrorPaths != null)
                {
                    foreach (var mp in shot.MirrorPaths)
                    {
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(mp));
                            File.WriteAllBytes(mp, png);
                            Log(preset, $"wrote {mp}");
                        }
                        catch (Exception e) { LogWarn(preset, $"couldn't write {mp}: {e.Message}"); }
                    }
                }
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

        private void Finish(CapturePreset preset, List<string> liveDirs)
        {
            var manifest = new System.Text.StringBuilder();
            foreach (var shot in preset.Shots) manifest.Append(shot.Name).Append(".png\n");
            string report = "ok\n" + manifest + (preset.ExtraReport?.Invoke() ?? "");
            File.WriteAllText(Path.Combine(liveDirs[0], preset.DoneFileName), report);
            Log(preset, preset.Key + " capture complete. " + report);
        }

        private void Fail(CapturePreset preset, List<string> liveDirs, string why)
        {
            LogWarn(preset, preset.Key + " capture aborted: " + why);
            try
            {
                string dir = liveDirs.Count > 0 ? liveDirs[0] : ".";
                File.WriteAllText(Path.Combine(dir, preset.DoneFileName), "fail: " + why + "\n");
            }
            catch { /* best effort */ }
        }

        private static void Log(CapturePreset preset, string m) => Debug.Log($"{preset.LogTag} {m}");
        private static void LogWarn(CapturePreset preset, string m) => Debug.LogWarning($"{preset.LogTag} {m}");

        /// <summary>The middle of the largest <see cref="ZoneKind.Open"/> room on the loaded map, or
        /// null if no map is loaded. Shared by any preset that wants to frame a shot away from the
        /// Entry room's hedges/flower beds (previously duplicated per-director).</summary>
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
    }

    /// <summary>Registry of every armable capture preset, keyed by <see cref="CapturePreset.Key"/>.
    /// Migrated from four of the nine former per-ticket director/entry-point pairs (MV-592) —
    /// HealthBarCluster, MissileTrail (MV-508), WaterGroundTrail (MV-555), MV585ForceField — chosen
    /// because none of them are referenced by any .bat or by another capture director, so migrating
    /// them carries no risk to the cc-verify.bat gate or to RobotRosterDirector/MsaaComparisonDirector's
    /// existing coupling. See MV-592's hand-off comment for which pairs were deliberately left bespoke
    /// and why.</summary>
    public static class CapturePresets
    {
        public static readonly Dictionary<string, CapturePreset> All = Build();

        /// <summary>A shot whose framing was already finished by the preset's own Prepare — nothing
        /// left to do before Capture() fires.</summary>
        private static IEnumerator NoSetup(Camera cam) { yield break; }

        private static Dictionary<string, CapturePreset> Build()
        {
            var d = new Dictionary<string, CapturePreset>(StringComparer.OrdinalIgnoreCase);
            void Add(CapturePreset p) => d[p.Key] = p;

            Add(BuildHealthBarCluster());
            Add(BuildMissileTrail());
            Add(BuildWaterGroundTrail());
            Add(BuildMv585ForceField());
            Add(BuildMv606Hud("mv6061920", "-mv6061920shot", "Temp/mv6061920.arm", "Temp/mv6061920.headless",
                "_mv6061920_done.txt", "MV-606-1920", 1920, 1080));
            Add(BuildMv606Hud("mv606phone", "-mv606phoneshot", "Temp/mv606phone.arm", "Temp/mv606phone.headless",
                "_mv606phone_done.txt", "MV-606-phone", 852, 393)); // 852x393: the project's own iPhone-landscape convention (see UiScreensDirector)
            Add(BuildMv617WaterReach());
            return d;
        }

        // ---- HealthBarCluster (MV-473) -------------------------------------------------------

        private static Vector3 HealthBarClusterOffset(int i) => i switch
        {
            0 => new Vector3(-0.6f, 0f, 0.4f),
            1 => new Vector3(0.6f, 0f, 0.4f),
            2 => new Vector3(0f, 0f, -0.5f),
            _ => new Vector3(0f, 0f, 0.6f),   // the Bruiser, front and centre — largest silhouette closest to camera
        };

        /// <summary>Same greybox recipe RobotRosterDirector's own BuildRobot uses — the shipped
        /// no-prefab spawn path, minus pooling this one-shot capture doesn't need.</summary>
        private static GameObject BuildClusterRobot(EnemyKind kind, Vector3 at)
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

        private static CapturePreset BuildHealthBarCluster()
        {
            var clusterMix = new[] { EnemyKind.Rusher, EnemyKind.Rusher, EnemyKind.Rusher, EnemyKind.Bruiser };
            const float clusterSpacing = 1.1f;
            string primary = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press", "health-bar-cluster"));
            const string secondary = @"C:\Dev\MaxVsTheWorlds-Images\_screens\health-bar-cluster";

            GameObject[] cluster = null;
            GameObject[] offscreen = null;
            float pitch = 0f, distance = 0f;
            int liveCount = 0;
            double resolveMicros = 0;

            IEnumerator Prepare(Camera cam)
            {
                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                pitch = rig != null ? rig.Pitch : 60f;
                distance = rig != null ? rig.Distance : 24.284037f;

                var max = GameObject.FindGameObjectWithTag("Player");
                Vector3 focusBase = CaptureDirector.OpenZoneCenter()
                    ?? (max != null ? max.transform.position + max.transform.forward * 6f : Vector3.forward * 6f);

                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                cluster = new GameObject[clusterMix.Length];
                for (int i = 0; i < clusterMix.Length; i++)
                {
                    Vector3 at = focusBase + HealthBarClusterOffset(i) * clusterSpacing;
                    cluster[i] = BuildClusterRobot(clusterMix[i], at);
                }

                int scattered = Mathf.Max(0, EnemySpawner.GlobalMaxLiveEnemies - clusterMix.Length);
                offscreen = new GameObject[scattered];
                for (int i = 0; i < scattered; i++)
                {
                    Vector3 at = focusBase + new Vector3(60f + i * 2f, 0f, 60f);
                    offscreen[i] = BuildClusterRobot(i % 4 == 0 ? EnemyKind.Bruiser : EnemyKind.Rusher, at);
                }

                for (int i = 0; i < 3; i++) yield return null;   // let the rigs build and the declutter pass settle

                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focusBase - rot * Vector3.forward * distance, rot);

                yield return null;
                yield return null;

                resolveMicros = WorldHealthBarDeclutter.LastResolveMicroseconds;
                liveCount = WorldHealthBar.LastShowingCount;
            }

            return new CapturePreset
            {
                Key = "healthbarcluster",
                LogTag = "[HealthBarCluster]",
                Flag = "-healthbarcluster",
                ArmFile = "Temp/healthbarcluster.arm",
                HeadlessMarker = "Temp/healthbarcluster.headless",
                DoneFileName = "_healthbarcluster_done.txt",
                Width = 1920,
                Height = 1080,
                OutputDirs = new[] { primary, secondary },
                TimeoutSeconds = 120,
                Prepare = Prepare,
                Shots = new List<CaptureShot> { new CaptureShot("cluster", NoSetup) },
                Cleanup = () =>
                {
                    if (cluster != null) foreach (var go in cluster) if (go != null) Destroy(go);
                    if (offscreen != null) foreach (var go in offscreen) if (go != null) Destroy(go);
                },
                ExtraReport = () => $"pitch={pitch:F2}deg distance={distance:F3}m\n" +
                                     $"declutter pass at {liveCount} live robots: {resolveMicros:F1} microseconds\n",
            };
        }

        // ---- MissileTrail (MV-508 AC6) --------------------------------------------------------

        private static CapturePreset BuildMissileTrail()
        {
            const float pitch = 60f;
            const float distance = 3f;
            const float travelDistanceForShot = 1.2f;
            const int maxSettleFrames = 300;

            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press", "missile-trail"));
            const string mirrorPath = @"C:\Dev\MaxVsTheWorlds-Images\MV-508-missile-trail.png";

            HomingMissile missile = null;
            GameObject fakeTarget = null;

            IEnumerator Setup(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                var max = GameObject.FindGameObjectWithTag("Player");
                Vector3 focus = CaptureDirector.OpenZoneCenter()
                    ?? (max != null ? max.transform.position + max.transform.forward * 6f : Vector3.forward * 6f);

                Vector3 origin = focus + new Vector3(-4f, 0f, 0f);
                Vector3 targetPos = focus + new Vector3(500f, 0f, 0f);
                fakeTarget = new GameObject("MissileTrailCapture_FakeTarget");
                fakeTarget.transform.position = targetPos;

                missile = HomingMissile.Fire(origin, fakeTarget.transform, speed: 4.5f, damage: 1f, splashRadius: 1f);

                int frame = 0;
                while (missile != null && frame < maxSettleFrames &&
                       Vector3.Distance(missile.transform.position, origin) < travelDistanceForShot)
                {
                    yield return null;
                    frame++;
                }

                if (missile == null)
                {
                    Destroy(fakeTarget);
                    throw new CaptureAbortException("the missile detonated (geometry or contact) before it had travelled far enough to frame");
                }

                // Frame and render on the SAME tick the distance check passed — any further yield here
                // would let the missile move again before Capture()'s manual cam.Render() actually fires.
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                Vector3 mp = missile.transform.position;
                cam.transform.SetPositionAndRotation(mp - rot * Vector3.forward * distance, rot);

                // Reseed the trail deterministically — headless real-time frames can spike past the
                // trail's own 0.12s memory window, ageing it out to nothing between Updates.
                var trail = missile.GetComponent<TrailRenderer>();
                if (trail != null)
                {
                    trail.Clear();
                    Vector3 back = -missile.transform.forward;
                    for (int i = 6; i >= 0; i--) trail.AddPosition(mp + back * (0.08f * i));
                }
            }

            return new CapturePreset
            {
                Key = "missiletrail",
                LogTag = "[MissileTrailCapture]",
                Flag = "-missiletrail",
                ArmFile = "Temp/missiletrail.arm",
                HeadlessMarker = "Temp/missiletrail.headless",
                DoneFileName = "_missiletrail_done.txt",
                Width = 1920,
                Height = 1080,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                Shots = new List<CaptureShot>
                {
                    new CaptureShot("missile-in-flight", Setup, new[] { mirrorPath }),
                },
                Cleanup = () =>
                {
                    if (missile != null) Destroy(missile.gameObject);
                    if (fakeTarget != null) Destroy(fakeTarget);
                },
            };
        }

        // ---- WaterGroundTrail (MV-555) --------------------------------------------------------

        private static CapturePreset BuildWaterGroundTrail()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images";
            const float pitch = 60f;
            const int maxSettleFrames = 90;

            GameObject maxGo = null;
            PlayerController player = null;
            FieldInfo facingField = null;
            WaterBlaster blaster = null;

            IEnumerator Prepare(Camera cam)
            {
                DevMode.Enabled = true;
                DevMode.Invincible = true;
                DevMode.InfiniteEnergy = true;
                DevMode.AutoFire = true;

                for (int i = 0; i < 4; i++) yield return null;

                maxGo = GameObject.FindGameObjectWithTag("Player");
                if (maxGo == null) throw new CaptureAbortException("no Player-tagged Max in the scene");
                facingField = typeof(PlayerController).GetField("_facing", BindingFlags.NonPublic | BindingFlags.Instance);
                player = maxGo.GetComponent<PlayerController>();
                blaster = maxGo.GetComponent<WaterBlaster>();

                var hud = FindFirstObjectByType<HudController>();
                if (hud != null) hud.gameObject.SetActive(false);
            }

            IEnumerator AimFireAndCapture(Camera cam, Vector3 dir)
            {
                if (player != null && facingField != null) facingField.SetValue(player, dir);

                int frame = 0;
                while (frame < maxSettleFrames && Vector3.Angle(maxGo.transform.forward, dir) > 1f)
                {
                    yield return null;
                    frame++;
                }
                for (int i = 0; i < 10; i++) yield return null;   // let the stream + ground trail build up

                float range = blaster != null ? blaster.Range : WaterBlaster.DefaultRange;
                Vector3 focus = maxGo.transform.position; focus.y = 1f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                float distance = Mathf.Max(8f, range * 1.8f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);

                yield return null;
            }

            return new CapturePreset
            {
                Key = "watergroundtrail",
                LogTag = "[MV555Capture]",
                Flag = "-mv555shots",
                ArmFile = "Temp/mv555.arm",
                HeadlessMarker = "Temp/mv555.headless",
                DoneFileName = "_mv555_done.txt",
                Width = 1133,
                Height = 744,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                Prepare = Prepare,
                Shots = new List<CaptureShot>
                {
                    new CaptureShot("MV-555-left", cam => AimFireAndCapture(cam, Vector3.left)),
                    new CaptureShot("MV-555-right", cam => AimFireAndCapture(cam, Vector3.right)),
                },
            };
        }

        // ---- MV585ForceField (MV-585 AC6) -----------------------------------------------------

        private static CapturePreset BuildMv585ForceField()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images";

            IEnumerator Setup(Camera cam)
            {
                var maxGo = GameObject.FindGameObjectWithTag("Player");
                if (maxGo == null) throw new CaptureAbortException("no Player-tagged Max in the scene");
                var abilities = maxGo.GetComponent<PlayerAbilities>();
                if (abilities == null) throw new CaptureAbortException("Max has no PlayerAbilities");

                var hud = FindFirstObjectByType<HudController>();
                if (hud == null) throw new CaptureAbortException("no HudController in the scene");

                abilities.ForceActivateForceFieldForTuning();

                // Let HudController's own Update tick the label from the freshly-raised bubble.
                for (int i = 0; i < 3; i++) yield return null;

                hud.gameObject.SetActive(true);
                int ui = LayerMask.NameToLayer("UI");
                if (ui >= 0) cam.cullingMask |= (1 << ui);
                var hudCanvas = hud.GetComponentInChildren<Canvas>(true);
                if (hudCanvas != null)
                {
                    hudCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                    hudCanvas.worldCamera = cam;
                    hudCanvas.planeDistance = 1f;
                }

                yield return null;
            }

            return new CapturePreset
            {
                Key = "mv585forcefield",
                LogTag = "[MV585Capture]",
                Flag = "-mv585shot",
                ArmFile = "Temp/mv585.arm",
                HeadlessMarker = "Temp/mv585.headless",
                DoneFileName = "_mv585_done.txt",
                Width = 852,
                Height = 393,
                DisableBrain = false,   // the shot never repositions the camera — Cinemachine keeps driving it
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // HudController.Awake bakes the Force Field button's visibility from RigState once,
                    // before AfterSceneLoad — the unlock has to land before that Awake runs.
                    RigState.Reset();
                    foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
                    RigState.AcquireCap("e_ff");
                },
                // MV-602 reuses this same armed run to also drop its own hand-off PNG (the field's
                // still raised on Max from the identical Setup) rather than adding a second preset.
                Shots = new List<CaptureShot> { new CaptureShot("MV-585", Setup), new CaptureShot("MV-602", Setup) },
            };
        }

        // ---- MV606Hud (MV-606 AC8) -----------------------------------------------------------

        /// <summary>The reshuffled HUD only reads as intended once Force Field, Teleport and Water
        /// Balloon are all on screen at once alongside the RIG's own move — one shared preset builder,
        /// called twice below for the desktop (1920x1080) and iPhone-landscape (852x393) hand-off
        /// shots the ticket's human-check AC asks for.</summary>
        private static CapturePreset BuildMv606Hud(string key, string flag, string armFile, string headlessMarker,
            string doneFileName, string shotName, int width, int height)
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images";

            IEnumerator Setup(Camera cam)
            {
                var hud = FindFirstObjectByType<HudController>();
                if (hud == null) throw new CaptureAbortException("no HudController in the scene");

                hud.gameObject.SetActive(true);
                int ui = LayerMask.NameToLayer("UI");
                if (ui >= 0) cam.cullingMask |= (1 << ui);
                var hudCanvas = hud.GetComponentInChildren<Canvas>(true);
                if (hudCanvas != null)
                {
                    hudCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                    hudCanvas.worldCamera = cam;
                    hudCanvas.planeDistance = 1f;
                }

                yield return null;
            }

            return new CapturePreset
            {
                Key = key,
                LogTag = "[MV606Capture]",
                Flag = flag,
                ArmFile = armFile,
                HeadlessMarker = headlessMarker,
                DoneFileName = doneFileName,
                Width = width,
                Height = height,
                DisableBrain = false,   // the shot never repositions the camera — Cinemachine keeps driving it
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // HudController.Awake bakes each control's visibility from RigState once, before
                    // AfterSceneLoad — the unlocks have to land before that Awake runs.
                    RigState.Reset();
                    foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
                    RigState.AcquireCap("e_ff");  // Force Field
                    RigState.AcquireCap("m_tp");  // Teleport
                    RigState.AcquireCap("s_bal"); // Water Balloon
                },
                Shots = new List<CaptureShot> { new CaptureShot(shotName, Setup) },
            };
        }

        // ---- MV617WaterReach (MV-617 AC5) -----------------------------------------------------

        /// <summary>Fires the primary dead ahead, auto-firing (DevMode), long enough for the stream,
        /// the ground outline and its splashes to settle, then frames wide enough to show the whole
        /// reach — proof the stream's visible tip, the outline, and the splash all land at the same
        /// distance, the thing MV-617 fixed.</summary>
        private static CapturePreset BuildMv617WaterReach()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images";
            const float pitch = 60f;
            const int maxSettleFrames = 90;

            GameObject maxGo = null;
            WaterBlaster blaster = null;

            IEnumerator Prepare(Camera cam)
            {
                DevMode.Enabled = true;
                DevMode.Invincible = true;
                DevMode.InfiniteEnergy = true;
                DevMode.AutoFire = true;

                for (int i = 0; i < 4; i++) yield return null;

                maxGo = GameObject.FindGameObjectWithTag("Player");
                if (maxGo == null) throw new CaptureAbortException("no Player-tagged Max in the scene");
                var facingField = typeof(PlayerController).GetField("_facing", BindingFlags.NonPublic | BindingFlags.Instance);
                var player = maxGo.GetComponent<PlayerController>();
                blaster = maxGo.GetComponent<WaterBlaster>();
                // Same firing direction MV-555's capture settled on: clear of the Entry room's
                // fences/hedges that sit in front of Max's spawn facing.
                if (player != null && facingField != null) facingField.SetValue(player, Vector3.left);

                var hud = FindFirstObjectByType<HudController>();
                if (hud != null) hud.gameObject.SetActive(false);

                int frame = 0;
                while (frame < maxSettleFrames && Vector3.Angle(maxGo.transform.forward, Vector3.left) > 1f)
                {
                    yield return null;
                    frame++;
                }
                for (int i = 0; i < 12; i++) yield return null;   // let the stream, outline and splashes settle

                float range = blaster != null ? blaster.Range : WaterBlaster.DefaultRange;
                Vector3 focus = maxGo.transform.position; focus.y = 1f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                float distance = Mathf.Max(8f, range * 1.8f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);

                yield return null;
            }

            return new CapturePreset
            {
                Key = "mv617waterreach",
                LogTag = "[MV617Capture]",
                Flag = "-mv617shot",
                ArmFile = "Temp/mv617.arm",
                HeadlessMarker = "Temp/mv617.headless",
                DoneFileName = "_mv617_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                Prepare = Prepare,
                Shots = new List<CaptureShot> { new CaptureShot("MV-617", NoSetup) },
            };
        }
    }
}
