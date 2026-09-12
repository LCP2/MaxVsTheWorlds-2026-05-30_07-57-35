using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Cinemachine;
using UnityEngine;
using static UnityEngine.Object;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.CameraRig;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.VFX;
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
            Add(BuildMv616SentinelBeam());
            Add(BuildMv674TeleportCrackle());
            Add(BuildMv693Replicator());
            Add(BuildMv702LppeShoulderRack());
            Add(BuildMv699Sludgequeen());
            Add(BuildIntroHandoffFrame());
            Add(BuildMv738SludgeCheck());
            Add(BuildMv769SludgeTreatment());
            Add(BuildMv742StormdrainCheck());
            Add(BuildMv750DressingCheck());
            Add(BuildMv754LightCheck());
            Add(BuildMv755StormdrainDressingCheck());
            Add(BuildMv758LppeSalvo());
            Add(BuildMv759GateDoorsCheck());
            Add(BuildMv770RocketSalvo());
            Add(BuildMv773GrateLurker());
            Add(BuildMv775ReplicatorMachine());
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

        // ---- MV616SentinelBeam (MV-616 AC5) ---------------------------------------------------

        /// <summary>Deploys one sentinel and a stationary target robot, waits for the sentinel's own
        /// auto-fire (no forced/scripted shot — this proves the REAL firing path, the same one
        /// <see cref="Sentinel.Update"/> drives every frame), then frames and captures the instant a
        /// shot is actually live (polls the private <c>_beamTimer</c> the same way
        /// <see cref="MaxWorlds.Tests.EditMode.SentinelBeamVfxTests"/> does, since the beam is only
        /// visible for a sub-0.12s window and a fixed frame delay could easily miss it).</summary>
        private static CapturePreset BuildMv616SentinelBeam()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images";
            const float pitch = 60f;
            const float distance = 9f;
            const int maxWaitFrames = 240;
            const int maxFireAttempts = 8;

            Sentinel sentinel = null;
            GameObject sentinelGo = null;
            GameObject targetGo = null;
            FieldInfo beamTimerField = typeof(Sentinel).GetField("_beamTimer", BindingFlags.NonPublic | BindingFlags.Instance);

            IEnumerator Setup(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                Vector3 focus = CaptureDirector.OpenZoneCenter() ?? Vector3.zero;
                Vector3 sentinelPos = focus + new Vector3(-1.5f, 0f, 0f);
                Vector3 targetPos = focus + new Vector3(2.5f, 0f, 0f);

                sentinelGo = new GameObject("MV616CaptureSentinel");
                sentinel = sentinelGo.AddComponent<Sentinel>();
                sentinel.Init(sentinelPos, maxHp: 60f, range: 8f, fireInterval: 0.5f,
                    moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);

                targetGo = BuildClusterRobot(EnemyKind.Gunner, targetPos);
                Physics.SyncTransforms();

                // Aim the camera at the pair BEFORE waiting for a shot — ParticleSystem's default
                // culling mode only simulates a system while it's visible to a camera, so leaving the
                // camera at its untouched default position during the wait silently starved every
                // particle of simulation (isPlaying stayed true, particleCount stayed 0 the whole time).
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                Vector3 camFocus = Vector3.Lerp(sentinelPos, targetPos, 0.5f); camFocus.y = 1f;
                cam.transform.SetPositionAndRotation(camFocus - rot * Vector3.forward * distance, rot);
                yield return null;

                // Wait for the sentinel's own auto-fire (real path, no scripted shot) to go live, then
                // let several more frames of real particle simulation accumulate before capturing — a
                // render on the very frame Play() fires catches the stream at zero live droplets
                // (emission hasn't been simulated yet), and even one frame later is only a handful.
                // The droplets' own lifetime (WaterVfx.travelTime, 0.32s) far outlives BeamVisibleSeconds
                // (<=0.12s), so waiting past the beam's own active window still shows a live, settled jet.
                bool captured = false;
                int frame = 0;
                for (int attempt = 0; attempt < maxFireAttempts && !captured; attempt++)
                {
                    while (frame < maxWaitFrames && (float)beamTimerField.GetValue(sentinel) <= 0f)
                    {
                        yield return null;
                        frame++;
                    }
                    if ((float)beamTimerField.GetValue(sentinel) <= 0f) break; // ran out of frames entirely

                    // Belt-and-braces against the same starvation the BeforeSceneLoad fix above solves
                    // for the frozen-time case: force every system to simulate regardless of whether
                    // it's currently inside the camera's frustum.
                    var liveOrigin = sentinelGo.transform.Find("BeamOrigin");
                    if (liveOrigin != null)
                    {
                        foreach (var livePs in liveOrigin.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            var m = livePs.main;
                            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                        }
                    }

                    for (int settle = 0; settle < 6; settle++) yield return null;
                    captured = true;
                }
                if (!captured)
                    throw new CaptureAbortException("never caught a live beam frame within the capture window");
            }

            return new CapturePreset
            {
                Key = "mv616sentinelbeam",
                LogTag = "[MV616Capture]",
                Flag = "-mv616shot",
                ArmFile = "Temp/mv616.arm",
                HeadlessMarker = "Temp/mv616.headless",
                DoneFileName = "_mv616_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // HomeScreen's pick-a-slot modal only skips itself for PressKitDirector/
                    // UiScreensDirector/PerfCaptureDirector — it doesn't know about this shared
                    // CapturePresets system (MV-592 postdates it) — so on a clean profile (no slot
                    // picked yet) it opens and freezes Time.timeScale at 0 for the whole session,
                    // which silently starves every ParticleSystem of simulation (isPlaying stays
                    // true, particleCount stays 0 forever). Marking a slot active here trips
                    // HomeScreen's OWN existing "a slot is already live" skip (its first check,
                    // before the capture-director checks), so time is never paused in the first place.
                    SaveSystem.ActiveSlot = 0;
                },
                Shots = new List<CaptureShot> { new CaptureShot("MV-616", Setup) },
                Cleanup = () =>
                {
                    if (sentinelGo != null) Destroy(sentinelGo);
                    if (targetGo != null) Destroy(targetGo);
                },
            };
        }

        // ---- MV674TeleportCrackle (MV-674 AC3) ------------------------------------------------

        /// <summary>Fires Max's own teleport through the real <see cref="PlayerAbilities.TryTeleport"/>
        /// path (not a scripted VFX call) and frames the arrival point once the beat's staggered
        /// arrival burst has fired — long enough for the new electric crackle layer (MV-674) to be
        /// live alongside the existing cyan-violet surge/shockwave, short enough that the crackle's
        /// own ~0.1-0.2s life hasn't faded yet. Waits on <see cref="Time.time"/> rather than a fixed
        /// frame count: an idle headless scene can render far more or fewer frames per real second
        /// than 60, and <see cref="MaxWorlds.VFX.CombatVfx"/>'s own beat coroutine stagger
        /// (<c>TeleportFlashStagger</c>, 0.08s) is itself Time.time-driven, not frame-count-driven.</summary>
        private static CapturePreset BuildMv674TeleportCrackle()
        {
            const float pitch = 60f;
            const float distance = 4f;
            const float settleSeconds = 0.12f; // just past the 0.08s stagger — arrival burst freshly fired, nothing has faded

            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press", "teleport-crackle"));
            const string mirrorPath = @"C:\Dev\MaxVsTheWorlds-Images\MV-674-teleport-crackle.png";

            IEnumerator Setup(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                var maxGo = GameObject.FindGameObjectWithTag("Player");
                if (maxGo == null) throw new CaptureAbortException("no Player-tagged Max in the scene");
                var abilities = maxGo.GetComponent<PlayerAbilities>();
                if (abilities == null) throw new CaptureAbortException("Max has no PlayerAbilities");

                var hud = FindFirstObjectByType<HudController>();
                if (hud != null) hud.gameObject.SetActive(false);

                // Same starvation MV616SentinelBeam's own belt-and-braces fix guards against: a
                // ParticleSystem outside the camera's frustum doesn't simulate (isPlaying stays true,
                // particleCount stays 0), and the camera isn't pointed at the arrival spot until AFTER
                // the teleport below. Force every Max-teleport burst to simulate regardless.
                foreach (string burstName in new[] { "MaxTeleportSurge", "MaxTeleportShockwave", "MaxTeleportFlash", "MaxTeleportCrackle" })
                {
                    var burstGo = GameObject.Find(burstName);
                    if (burstGo != null && burstGo.TryGetComponent<ParticleSystem>(out var burstPs))
                    {
                        var m = burstPs.main;
                        m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                    }
                }

                // Same clear-of-hedges direction MV-555/MV-617's captures already settled on.
                float teleportedAt = Time.time;
                if (!abilities.TryTeleport(Vector3.left))
                    throw new CaptureAbortException("TryTeleport returned false — ability not acquired or on cooldown");

                // transform.position is already the arrival point (TryTeleport sets it synchronously).
                Vector3 focus = maxGo.transform.position; focus.y = 1f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);

                while (Time.time - teleportedAt < settleSeconds) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv674teleportcrackle",
                LogTag = "[MV674Capture]",
                Flag = "-mv674shot",
                ArmFile = "Temp/mv674.arm",
                HeadlessMarker = "Temp/mv674.headless",
                DoneFileName = "_mv674_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // HudController.Awake bakes Teleport's own visibility from RigState once, before
                    // AfterSceneLoad — the unlock has to land before that Awake runs.
                    RigState.Reset();
                    foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
                    RigState.AcquireCap("m_tp"); // Teleport
                    // Same clean-profile guard as MV616SentinelBeam's own preset above: on a fresh
                    // profile (no slot picked yet) HomeScreen's pick-a-slot modal freezes
                    // Time.timeScale at 0, which would starve every ParticleSystem of simulation.
                    SaveSystem.ActiveSlot = 0;
                },
                Shots = new List<CaptureShot> { new CaptureShot("MV-674-teleport-crackle", Setup, new[] { mirrorPath }) },
            };
        }

        // ---- MV693Replicator (MV-693) ---------------------------------------------------------

        /// <summary>Builds a standalone Replicator the same way <c>MapRuntime.BuildReplicator</c>
        /// does (primitive-cube collider host, <c>Configure</c> after <c>AddComponent</c>) and
        /// frames it so the generated hull/hazard band/hatch/valve-wheel body (MV-693) is
        /// visible — proof of the actual result, not a description of one.</summary>
        private static CapturePreset BuildMv693Replicator()
        {
            const float pitch = 60f;
            const float distance = 5f;

            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images";

            GameObject replicatorGo = null;

            IEnumerator Setup(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                Vector3 focus = CaptureDirector.OpenZoneCenter() ?? Vector3.zero;

                replicatorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                replicatorGo.name = "MV693CaptureReplicator";
                replicatorGo.transform.position = focus;
                replicatorGo.transform.localScale = new Vector3(2f, 2f, 1.5f);
                var replicator = replicatorGo.AddComponent<Replicator>();
                replicator.Configure(1);

                for (int i = 0; i < 3; i++) yield return null;   // let the generated body settle

                var rot = Quaternion.Euler(pitch, 0f, 0f);
                Vector3 camFocus = focus + Vector3.up * 1f;
                cam.transform.SetPositionAndRotation(camFocus - rot * Vector3.forward * distance, rot);

                // A throwaway render to the screen backbuffer first: this preset's shot is the
                // FIRST manual cam.Render() call of the whole session, and URP's Render Graph has
                // logged a one-off NullReferenceException on exactly that first call in headless
                // -nographics runs (seen on this exact preset) — warming it up here, before
                // Capture()'s own render-to-texture call, keeps that hiccup off the real shot.
                cam.Render();
                for (int i = 0; i < 3; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv693replicator",
                LogTag = "[MV693Capture]",
                Flag = "-mv693shot",
                ArmFile = "Temp/mv693.arm",
                HeadlessMarker = "Temp/mv693.headless",
                DoneFileName = "_mv693_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same clean-profile guard MV616SentinelBeam/MV674TeleportCrackle's own presets
                    // use: on a fresh profile (no slot picked yet) HomeScreen's pick-a-slot modal
                    // freezes Time.timeScale at 0 and blocks this capture indefinitely.
                    SaveSystem.ActiveSlot = 0;
                },
                Shots = new List<CaptureShot> { new CaptureShot("MV-693-result", Setup) },
                Cleanup = () => { if (replicatorGo != null) Destroy(replicatorGo); },
            };
        }

        // ---- MV702LppeShoulderRack (MV-702) ---------------------------------------------------

        /// <summary>Frames Max with the LPPE gadget live and the Shoulder Rack mount bought (MV-702) —
        /// proof the generated meshes actually show on <c>MaxRig</c>, not a description of one. Reaches
        /// World 2 state the same proven way <c>MV689WeaponCoreMorphTests</c> does (the real
        /// collect-a-core-then-open-THE-RIG morph), rather than hand-setting
        /// <c>WeaponSystemState.ActivePrimary</c>/<c>SecondaryKind</c> directly against a World-1 board
        /// that has no <c>s_rkt</c> node to acquire.</summary>
        private static CapturePreset BuildMv702LppeShoulderRack()
        {
            const float pitch = 60f;
            const float distance = 4f;
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images";

            IEnumerator Setup(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                var maxGo = GameObject.FindGameObjectWithTag("Player");
                if (maxGo == null) throw new CaptureAbortException("no Player-tagged Max in the scene");

                var hud = FindFirstObjectByType<HudController>();
                if (hud != null) hud.gameObject.SetActive(false);

                for (int i = 0; i < 3; i++) yield return null;   // let MaxRig's own Awake read the morphed state

                Vector3 focus = maxGo.transform.position + Vector3.up * 1f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);

                yield return null;
            }

            return new CapturePreset
            {
                Key = "mv702lppeshoulderrack",
                LogTag = "[MV702Capture]",
                Flag = "-mv702shot",
                ArmFile = "Temp/mv702.arm",
                HeadlessMarker = "Temp/mv702.headless",
                DoneFileName = "_mv702_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // The same real morph path MV689WeaponCoreMorphTests proves: collect a Weapon Core,
                    // then THE RIG's next open. Landing on World 2's board this way (rather than poking
                    // ActivePrimary/SecondaryKind directly against a still-World-1 board) is what makes
                    // "s_rkt" a real, acquirable node.
                    WeaponSystemState.Reset();
                    PendingMorphingModule.Reset();
                    PendingMorphingModule.SetWeaponCore();
                    WeaponSystemState.OpenWeaponCoreMorphIfPending(worldIndex: 1);
                    RigState.AcquireCap("s_rkt");   // clears SECONDARY's mystery lock, buys the rack to L1
                    SaveSystem.ActiveSlot = 0;
                },
                Shots = new List<CaptureShot> { new CaptureShot("MV-702-result", Setup) },
            };
        }

        // ---- MV699Sludgequeen (MV-699) ---------------------------------------------------------

        /// <summary>Builds a standalone Sludgequeen and explicitly grows her rig via
        /// <c>SludgequeenRig.CreateFor</c> — the boss doesn't do this itself yet (wiring into
        /// <c>MapRuntime</c>'s <c>bosses[]</c> dispatch is still MV-696's own out-of-scope follow-up) —
        /// then frames it so the generated drum/hatches/valve-wheel-crown/eye body (MV-699) is
        /// visible: proof of the actual result, not a description of one.</summary>
        private static CapturePreset BuildMv699Sludgequeen()
        {
            const float pitch = 60f;
            // 18 m, not MV693Replicator's 5 m: this boss's own footprint (6 m drum, ~4.5 m to the
            // crown's top) is 3x that box's, and a distance that only cleared the smaller Replicator
            // framed nothing but this crown's own top face.
            const float distance = 18f;

            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images";

            GameObject bossGo = null;
            SludgequeenRig rig = null;

            IEnumerator Setup(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                Vector3 focus = CaptureDirector.OpenZoneCenter() ?? Vector3.zero;

                bossGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bossGo.name = "MV699CaptureSludgequeen";
                bossGo.transform.position = focus;
                // The camera below approaches from -Z looking toward +Z — face the rig's own +Z
                // front (the CuratorEye's side) back at it, or the one part meant to read as a face
                // would sit on the far, camera-away side of the drum.
                bossGo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                var boss = bossGo.AddComponent<SludgequeenBoss>();
                boss.SetArenaBounds(new Rect(focus.x - 22f, focus.z - 22f, 44f, 44f));

                rig = SludgequeenRig.CreateFor(boss);

                for (int i = 0; i < 3; i++) yield return null;   // let the generated body settle

                var rot = Quaternion.Euler(pitch, 0f, 0f);
                Vector3 camFocus = focus + Vector3.up * 2.2f;   // roughly mid-height on the drum
                cam.transform.SetPositionAndRotation(camFocus - rot * Vector3.forward * distance, rot);

                // Same first-manual-Render() warm-up MV693Replicator's own preset needed — URP's
                // Render Graph has logged a one-off NullReferenceException on exactly that first call
                // in headless -nographics runs.
                cam.Render();
                for (int i = 0; i < 3; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv699sludgequeen",
                LogTag = "[MV699Capture]",
                Flag = "-mv699shot",
                ArmFile = "Temp/mv699.arm",
                HeadlessMarker = "Temp/mv699.headless",
                DoneFileName = "_mv699_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same clean-profile guard MV616SentinelBeam/MV674TeleportCrackle/MV693Replicator's
                    // own presets use: on a fresh profile (no slot picked yet) HomeScreen's pick-a-slot
                    // modal freezes Time.timeScale at 0 and blocks this capture indefinitely.
                    SaveSystem.ActiveSlot = 0;
                },
                Shots = new List<CaptureShot> { new CaptureShot("MV-699-result", Setup) },
                Cleanup = () =>
                {
                    if (rig != null) Destroy(rig.gameObject);
                    if (bossGo != null) Destroy(bossGo);
                },
            };
        }

        // ---- IntroHandoffFrame (MV-719 AC6) ---------------------------------------------------

        /// <summary>The single reference frame the opening cinematic's cross-fade hands off onto — Max
        /// at <see cref="MapData"/>'s <see cref="EntityKind.PlayerSpawn"/>, framed by
        /// <see cref="FixedAngleCameraRig"/>'s resting pitch and distance, HUD hidden. Lee's decision
        /// (2026-09-07): the pre-rendered opening film is authored to END on this frame, using the video
        /// model's start/end-frame control, so this is the reference the film gets made to land on — not
        /// a conformance shot, and deliberately NOT wired into cc-verify/cc-screens (MV-719 AC6). Written
        /// beside the other press shots in docs/press/, not the MaxVsTheWorlds-Images folder every other
        /// preset here uses.</summary>
        private static CapturePreset BuildIntroHandoffFrame()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press"));

            Vector3 spawnPos = default;
            Vector3 camPos = default;
            Quaternion camRot = default;

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world

                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                if (rig == null) throw new CaptureAbortException("no FixedAngleCameraRig in the scene");

                MapData map = MapLibrary.Load(MapLibrary.BackyardSlice);
                MapEntity spawn = map?.First(EntityKind.PlayerSpawn);
                if (spawn == null) throw new CaptureAbortException("the shipped map has no PlayerSpawn");
                spawnPos = spawn.GroundedCenter;

                var max = GameObject.FindGameObjectWithTag("Player");
                if (max == null) throw new CaptureAbortException("no Player-tagged Max in the scene");

                // Same collider-disable/teleport/re-enable shape MapRuntime.Adopt uses to place Max at
                // the map's start — a live CharacterController would otherwise fight a direct position set.
                var cc = max.GetComponent<CharacterController>();
                bool was = cc != null && cc.enabled;
                if (cc != null) cc.enabled = false;
                Vector3 at = max.transform.position;
                max.transform.position = new Vector3(spawnPos.x, at.y, spawnPos.z);
                if (cc != null) cc.enabled = was;
                Physics.SyncTransforms();

                rig.RestingPose(spawnPos, out camPos, out camRot);
                cam.transform.SetPositionAndRotation(camPos, camRot);

                var hud = FindFirstObjectByType<HudController>();
                if (hud != null) hud.gameObject.SetActive(false);

                // A clean reference pose has no business showing transient VFX anyway, and disabling
                // every particle renderer sidesteps a real access-violation crash this shot hit inside
                // URP's billboard batcher (GfxDevice::DrawSharedGeometryJobs) under -nographics —
                // headless has no GPU context for the ambient/idle particle systems the world builds.
                foreach (var pr in FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None))
                    pr.enabled = false;

                // Same first-manual-Render() warm-up MV693Replicator/MV699Sludgequeen's own presets
                // need — URP's Render Graph has crashed on exactly the first manual cam.Render() call
                // in a headless -nographics run.
                cam.Render();
                for (int i = 0; i < 3; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "introhandoffframe",
                LogTag = "[IntroHandoffFrame]",
                Flag = "-introHandoffFrame",
                ArmFile = "Temp/introhandoffframe.arm",
                HeadlessMarker = "Temp/introhandoffframe.headless",
                DoneFileName = "_intro_handoff_frame_done.txt",
                Width = 2560,
                Height = 1440,
                SuperSample = 1,   // a still reference frame, not a hero shot — no need to 2x-blit for AA
                OutputDirs = new[] { outDir },
                BeforeSceneLoad = () =>
                {
                    // Same clean-profile guard MV616SentinelBeam/MV674TeleportCrackle/MV693Replicator/
                    // MV699Sludgequeen's own presets use: on a fresh profile (no slot picked yet)
                    // HomeScreen's pick-a-slot modal freezes Time.timeScale at 0 and blocks this capture.
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot> { new CaptureShot("intro_handoff_frame", NoSetup) },
                ExtraReport = () =>
                    $"spawn_world_pos={spawnPos:F3}\ncamera_pos={camPos:F3}\ncamera_rot_euler={camRot.eulerAngles:F3}\n",
            };
        }

        // ---- MV738SludgeCheck (MV-738 AC3) ----------------------------------------------------

        /// <summary>Boots straight into World 2's real shipped config — same <c>WorldIndex</c>
        /// <see cref="MaxWorlds.UI.HomeScreen.StartSlotWorld2"/> seeds, just landed before the FIRST
        /// scene load instead of a second one, since a capture preset only gets one boot to shoot
        /// from — and frames whichever sludge tile <see cref="MaxWorlds.Arena.BackyardPath"/>'s own
        /// <c>MapRuntime.Build</c> actually produced, so the shot proves the real end-to-end boot
        /// path (not an isolated material call) renders acid-green rather than MV-738's magenta.</summary>
        private static CapturePreset BuildMv738SludgeCheck()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + WorldMaterials finish

                var sludge = FindFirstObjectByType<SludgeFlow>();
                if (sludge == null) throw new CaptureAbortException("World 2 built no sludge tile to shoot");

                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;
                float distance = rig != null ? rig.Distance : 20f;

                Vector3 focus = sludge.transform.position + Vector3.up * 0.5f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);

                // Same first-manual-Render() warm-up MV693Replicator/MV699Sludgequeen's own presets
                // need — URP's Render Graph has crashed on exactly the first manual cam.Render() call
                // in a headless -nographics run.
                cam.Render();
                for (int i = 0; i < 3; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv738sludge",
                LogTag = "[MV738Capture]",
                Flag = "-mv738shot",
                ArmFile = "Temp/mv738.arm",
                HeadlessMarker = "Temp/mv738.headless",
                DoneFileName = "_mv738_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // The same WorldIndex HomeScreen's WORLD 2 dev button seeds via StartSlotWorld —
                    // just seeded before the first scene load rather than triggering a second one.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot> { new CaptureShot("MV-738-sludge", NoSetup) },
            };
        }

        // ---- MV769SludgeTreatment (MV-769) ----------------------------------------------------

        /// <summary>MV-769's own AC: "one capture of a puddle and a sludge lane" — two shots off one
        /// boot into World 2 (same <see cref="BuildMv738SludgeCheck"/> seeding). Shot 1 frames an
        /// authored sludge lane close enough that the flow scroll and the new bubble emitters actually
        /// read; shot 2 spawns a Sludge Drone's own <see cref="SludgePuddle"/> nearby and frames that —
        /// proof of the irregular fan outline and the unlit glow, not a description of one.</summary>
        private static CapturePreset BuildMv769SludgeTreatment()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

            Transform laneTransform = null;
            SludgePuddle puddle = null;

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + WorldMaterials finish

                var sludge = FindFirstObjectByType<SludgeFlow>();
                if (sludge == null) throw new CaptureAbortException("World 2 built no sludge tile to shoot");
                laneTransform = sludge.transform;
            }

            // Bubbles rise for RiseFraction (0.7) of their 1.8 s loop, so ~75 real-time frames at the
            // project's capped 60 fps (Bootstrap.cs) lands a shot mid-to-near-peak rise instead of the
            // very bottom of the cycle — waiting only 3 frames (this preset's first pass) never let a
            // single bubble clear the surface before the shot fired.
            const int bubbleSettleFrames = 75;

            IEnumerator SetupLane(Camera cam)
            {
                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;
                // Same distance MV738SludgeCheck already proved frames a lane tile cleanly — this
                // preset's first pass shrank it to 6 m, which put the camera almost inside the (large)
                // tile and filled the whole frame with a single flat green wall.
                float distance = rig != null ? rig.Distance : 20f;

                Vector3 focus = laneTransform.position + Vector3.up * 0.3f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);

                for (int i = 0; i < bubbleSettleFrames; i++) yield return null;

                // Same first-manual-Render() warm-up MV693Replicator/MV699Sludgequeen's own presets
                // need — URP's Render Graph has crashed on exactly the first manual cam.Render() call
                // in a headless -nographics run.
                cam.Render();
                for (int i = 0; i < 3; i++) yield return null;
            }

            IEnumerator SetupPuddle(Camera cam)
            {
                // Neither an offset from the lane tile NOR OpenZoneCenter() is safe: the former put the
                // camera inside Stormdrain wall/pipe dressing, and the latter turned out to sit ON TOP
                // of an authored sludge tile itself here — the puddle's own fan blobs (y=0.01/0.02) sit
                // BELOW that tile's top surface (y=0.05) and rendered fully hidden underneath it. Max's
                // own spawn point is guaranteed to be plain floor (same idiom BuildHealthBarCluster/
                // BuildMissileTrail already use for a safe prop-spawn focus).
                var max = GameObject.FindGameObjectWithTag("Player");
                Vector3 spawnAt = max != null
                    ? max.transform.position + max.transform.forward * 10f
                    : laneTransform.position + new Vector3(10f, 0f, 10f);
                // 3rd fix: this preset's previous pass put the puddle only 3 m ahead of Max, so his own
                // floating "MAX" WorldHealthBar (always-show, billboarded) filled almost the whole
                // frame — same reason BuildMv674TeleportCrackle hides HudController for its own shot.
                // Hiding Max entirely (10 m away now anyway) is the cleanest way to guarantee this is a
                // clean shot of the puddle, nothing else.
                if (max != null) max.SetActive(false);
                puddle = SludgePuddle.Spawn(spawnAt, radius: 2f, duration: 30f, seed: 11);

                for (int i = 0; i < bubbleSettleFrames; i++) yield return null;

                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;
                const float distance = 5f;

                Vector3 focus = spawnAt + Vector3.up * 0.3f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);

                cam.Render();
                for (int i = 0; i < 3; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv769sludge",
                LogTag = "[MV769Capture]",
                Flag = "-mv769shot",
                ArmFile = "Temp/mv769.arm",
                HeadlessMarker = "Temp/mv769.headless",
                DoneFileName = "_mv769_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same WorldIndex seeding as BuildMv742StormdrainCheck/BuildMv738SludgeCheck.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot>
                {
                    new CaptureShot("MV-769-sludge-lane", SetupLane),
                    new CaptureShot("MV-769-sludge-puddle", SetupPuddle),
                },
                Cleanup = () => { if (puddle != null) Destroy(puddle.gameObject); },
            };
        }

        // ---- MV742StormdrainCheck (MV-742 AC3) ------------------------------------------------

        /// <summary>Boots straight into World 2's real shipped config, same seeding as
        /// <see cref="BuildMv738SludgeCheck"/>, and shoots wherever the fixed-angle rig frames Max's
        /// own spawn naturally — no custom camera placement — so the shot proves what a player's first
        /// frame of World 2 actually looks like: floor, wall and cover together, wearing the Stormdrain
        /// palette rather than World 1's Backyard one (MV-742).</summary>
        private static CapturePreset BuildMv742StormdrainCheck()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + WorldMaterials finish

                // CaptureDirector.Run disables the CinemachineBrain (DisableBrain defaults true) before
                // every shot, so the camera never follows Max on its own here — frame him explicitly,
                // same fixed top-down angle the rig itself uses, just aimed by hand (same idiom
                // BuildMv738SludgeCheck uses for the sludge tile).
                var player = FindFirstObjectByType<PlayerController>();
                if (player == null) throw new CaptureAbortException("World 2 built no PlayerController to shoot");

                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;
                float distance = rig != null ? rig.Distance : 20f;

                Vector3 focus = player.transform.position + Vector3.up * 1f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
                for (int i = 0; i < 3; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv742stormdrain",
                LogTag = "[MV742Capture]",
                Flag = "-mv742shot",
                ArmFile = "Temp/mv742.arm",
                HeadlessMarker = "Temp/mv742.headless",
                DoneFileName = "_mv742_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // The same WorldIndex HomeScreen's WORLD 2 dev button seeds via StartSlotWorld —
                    // just seeded before the first scene load rather than triggering a second one.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot> { new CaptureShot("MV-742-stormdrain", NoSetup) },
            };
        }

        // ---- MV750DressingCheck (MV-750 AC3) --------------------------------------------------

        /// <summary>Same boot/frame idiom as <see cref="BuildMv742StormdrainCheck"/> — World 2's real
        /// shipped config, Max's own spawn in area a1 (Outfall Steps), the fixed rig's own angle — shot
        /// fresh under this ticket's own key so scoping the garden dressing to World 1
        /// (<see cref="MapData.WantsGardenDressing"/>) doesn't overwrite MV-742's own evidence file.</summary>
        private static CapturePreset BuildMv750DressingCheck()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + WorldMaterials finish

                var player = FindFirstObjectByType<PlayerController>();
                if (player == null) throw new CaptureAbortException("World 2 built no PlayerController to shoot");

                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;
                float distance = rig != null ? rig.Distance : 20f;

                Vector3 focus = player.transform.position + Vector3.up * 1f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
                for (int i = 0; i < 3; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv750dressing",
                LogTag = "[MV750Capture]",
                Flag = "-mv750shot",
                ArmFile = "Temp/mv750.arm",
                HeadlessMarker = "Temp/mv750.headless",
                DoneFileName = "_mv750_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same WorldIndex seeding as BuildMv742StormdrainCheck/BuildMv738SludgeCheck.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot> { new CaptureShot("MV-750-dressing-fixed", NoSetup) },
            };
        }

        // ---- Mv754LightCheck (MV-754 AC4) -----------------------------------------------------

        /// <summary>Same boot/frame idiom as <see cref="BuildMv742StormdrainCheck"/> — World 2's real
        /// shipped config, Max's own spawn, the fixed rig's own angle — shot fresh under this ticket's
        /// own key so re-lighting the drain (MV-754) has its own before/after evidence rather than
        /// overwriting MV-742's file.</summary>
        private static CapturePreset BuildMv754LightCheck()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + WorldMaterials finish

                var player = FindFirstObjectByType<PlayerController>();
                if (player == null) throw new CaptureAbortException("World 2 built no PlayerController to shoot");

                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;
                float distance = rig != null ? rig.Distance : 20f;

                Vector3 focus = player.transform.position + Vector3.up * 1f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
                for (int i = 0; i < 3; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv-w2-light",
                LogTag = "[MV754Capture]",
                Flag = "-mv754shot",
                ArmFile = "Temp/mv754.arm",
                HeadlessMarker = "Temp/mv754.headless",
                DoneFileName = "_mv754_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same WorldIndex seeding as BuildMv742StormdrainCheck/BuildMv738SludgeCheck.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot> { new CaptureShot("MV-754-w2-light", NoSetup) },
            };
        }

        // ---- Mv755StormdrainDressingCheck (MV-755 AC7) ----------------------------------------

        /// <summary>Same boot/frame idiom as <see cref="BuildMv742StormdrainCheck"/> — World 2's real
        /// shipped config, Max's own spawn, the fixed rig's own angle — shot fresh under this ticket's
        /// own key so the Stormdrain kit (MV-755: kerbs, pipe banks, lamps, dressed cover, sludge
        /// chevrons) has its own before/after evidence rather than overwriting MV-742's bare-palette
        /// file.</summary>
        private static CapturePreset BuildMv755StormdrainDressingCheck()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + WorldMaterials finish

                var player = FindFirstObjectByType<PlayerController>();
                if (player == null) throw new CaptureAbortException("World 2 built no PlayerController to shoot");

                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;
                float distance = rig != null ? rig.Distance : 20f;

                Vector3 focus = player.transform.position + Vector3.up * 1f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
                for (int i = 0; i < 3; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv-w2-kit",
                LogTag = "[MV755Capture]",
                Flag = "-mv755shot",
                ArmFile = "Temp/mv755.arm",
                HeadlessMarker = "Temp/mv755.headless",
                DoneFileName = "_mv755_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same WorldIndex seeding as BuildMv742StormdrainCheck/BuildMv738SludgeCheck.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot> { new CaptureShot("MV-755-stormdrain-kit", NoSetup) },
                // MV-782: FrameContrastGate (MV-777) existed only as a pure function proven against a
                // checked-in fixture and a synthetic texture in EditMode — nothing ever ran it against a
                // frame this preset actually produced, so a real regression (this ticket's own fog
                // defect) shipped straight past it. Loading the shot back in and logging the gate's own
                // figures into the done-report is the fix: diagnostic only (this never blocks
                // cc-verify, which doesn't run this preset), but it means the next World 2 visual
                // ticket has real numbers to quote instead of none. Same 40%-width skybox-wedge crop
                // MV777FrameContrastTests already applies to this exact preset's own captures.
                ExtraReport = () =>
                {
                    string path = Path.Combine(outDir, "MV-755-stormdrain-kit.png");
                    if (!File.Exists(path)) return "FrameContrastGate: capture file missing\n";

                    var full = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    Texture2D playArea = null;
                    try
                    {
                        if (!ImageConversion.LoadImage(full, File.ReadAllBytes(path)))
                            return "FrameContrastGate: could not decode capture\n";

                        int cropX0 = Mathf.RoundToInt(full.width * 0.40f);
                        int cropWidth = full.width - cropX0;
                        playArea = new Texture2D(cropWidth, full.height, TextureFormat.RGB24, false);
                        playArea.SetPixels(full.GetPixels(cropX0, 0, cropWidth, full.height));
                        playArea.Apply();

                        FrameContrastGate.Result result = FrameContrastGate.Check(playArea);
                        return $"FrameContrastGate: {result}\n";
                    }
                    finally
                    {
                        Destroy(full);
                        if (playArea != null) Destroy(playArea);
                    }
                },
            };
        }

        // ---- MV758LppeSalvo (MV-758) ----------------------------------------------------------

        /// <summary>Puts a standalone <see cref="PulseLaser"/>'s muzzle flash and impact/Shock beat on
        /// screen together for one representative frame, via the same real production calls
        /// <c>FireTick()</c>/<c>RegisterHit()</c> make (a real <c>FireTick()</c> invocation for the
        /// muzzle, the same public <c>LppeVfx.Impact()</c> for the impact/Shock beat) — not a scripted
        /// VFX call, but also not a wait on <see cref="SeekerPulse"/>'s own autonomous fire-and-lock
        /// loop, which proved non-deterministic and slow against World 2's still-spawning roster (see
        /// the method body comment). <c>Mv758LppeVfxTests</c> is what proves each call actually fires
        /// off the real path; this preset only needs to frame the result.</summary>
        private static CapturePreset BuildMv758LppeSalvo()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";
            const float pitch = 60f;
            const float distance = 4.5f;

            FieldInfo pulseLaserVfxField =
                typeof(PulseLaser).GetField("_vfx", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo[] burstFields = typeof(LppeVfx).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo fireTickMethod =
                typeof(PulseLaser).GetMethod("FireTick", BindingFlags.NonPublic | BindingFlags.Instance);

            GameObject laserGo = null;
            GameObject targetGo = null;

            IEnumerator Setup(Camera cam)
            {
                // Deliberately NOT waiting on SeekerPulse's own autonomous fire-and-lock loop here:
                // World 2's still-spawning roster (dozens of robots sharing RobotEnemy.Active) made
                // target acquisition non-deterministic, and the multi-second real-world wait for
                // several 0.22s-cadence pulses to land made this preset fragile under the same
                // frame-time spikes cc-verify's own gate records during scene load. The muzzle flash
                // (via a single real FireTick() call) and the impact/Shock beat (via the same public
                // Impact() RegisterHit calls) are each individually proven by the EditMode tests
                // (Mv758LppeVfxTests) to fire off the real production path — this preset only needs to
                // put them on screen together for one representative frame, fast and deterministically.
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                var playerGo = GameObject.FindGameObjectWithTag("Player");
                Vector3 focus = playerGo != null ? playerGo.transform.position : (CaptureDirector.OpenZoneCenter() ?? Vector3.zero);
                Vector3 aimDir = playerGo != null ? playerGo.transform.forward : Vector3.forward;
                aimDir.y = 0f;
                if (aimDir.sqrMagnitude < 0.01f) aimDir = Vector3.forward; else aimDir.Normalize();

                Vector3 laserPos = focus;
                Vector3 targetPos = focus + aimDir * 4f;
                laserGo = new GameObject("MV758CaptureLaser");
                laserGo.transform.SetPositionAndRotation(laserPos,
                    Quaternion.LookRotation(aimDir, Vector3.up));
                PulseLaser laser = laserGo.AddComponent<PulseLaser>();

                targetGo = BuildClusterRobot(EnemyKind.Bruiser, targetPos);
                Physics.SyncTransforms();

                var rot = Quaternion.Euler(pitch, 0f, 0f);
                Vector3 camFocus = targetPos + Vector3.up * 0.6f;
                cam.transform.SetPositionAndRotation(camFocus - rot * Vector3.forward * distance, rot);

                var vfx = (LppeVfx)pulseLaserVfxField.GetValue(laser);
                foreach (var field in burstFields)
                {
                    var burst = field.GetValue(vfx) as VfxBurst;
                    if (burst == null || burst.GameObject == null) continue;
                    var m = burst.GameObject.GetComponent<ParticleSystem>().main;
                    m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                }

                fireTickMethod.Invoke(laser, null);   // real FireTick() -> real Muzzle() call
                yield return null;

                Vector3 impactPoint = targetGo.transform.position + Vector3.up * 0.6f;
                vfx.Impact(impactPoint, 9f, isShockHit: false);   // same public Impact() RegisterHit calls
                yield return null;
                vfx.Impact(impactPoint, 9f, isShockHit: true);    // the Shock-carrying 4th-hit beat

                if (vfx.MuzzleFlashEmitCount < 1)
                    throw new CaptureAbortException("FireTick did not produce a muzzle flash");

                for (int settle = 0; settle < 2; settle++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv758lppesalvo",
                LogTag = "[MV758Capture]",
                Flag = "-mv758shot",
                ArmFile = "Temp/mv758.arm",
                HeadlessMarker = "Temp/mv758.headless",
                DoneFileName = "_mv758_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same HomeScreen-modal-freezes-time dodge BuildMv616SentinelBeam uses.
                    SaveSystem.ActiveSlot = 0;
                },
                Shots = new List<CaptureShot> { new CaptureShot("MV-758", Setup) },
                Cleanup = () =>
                {
                    if (laserGo != null) Destroy(laserGo);
                    if (targetGo != null) Destroy(targetGo);
                },
            };
        }

        // ---- Mv759GateDoorsCheck (MV-759 AC6) --------------------------------------------------

        /// <summary>Before/after evidence for the World 2 gate re-skin (MV-759): the same World-2-real-
        /// config boot idiom as <see cref="BuildMv755StormdrainDressingCheck"/>, framed on a specific,
        /// ordinary-sized doorway gate ("g9" in the shipped map — 3.8 x 1.5 x 0.64, well clear of the
        /// player's own spawn point) rather than whichever gate <c>FindFirstObjectByType</c> happens to
        /// return first, which is unstable run to run and, for an 11.8 m-wide boss gate or a gate right
        /// next to Max's own spawn, framed nothing recognisable at this preset's distance. Shot 1 is the
        /// gate as <see cref="BackyardPath"/> left it (closed, ring + double doors shut); shot 2 calls
        /// the same public <see cref="AreaGate.ForceOpen"/> every other gate test/preset uses and then
        /// waits out real frames (this preset runs inside an actual Play session, so
        /// <c>Time.deltaTime</c> ticks for real) well past the 0.45 s slide, so the doors are caught
        /// fully open, not mid-slide.</summary>
        private static CapturePreset BuildMv759GateDoorsCheck()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";
            const string gateId = "g9";   // an ordinary doorway gate well away from the player's spawn point
            // A shallow, near-frontal pitch on purpose, not this game's ~72 deg in-game rig angle: the
            // shipped map's gates are short (~1.5 m) and wide, so the production top-down angle
            // forecloses almost the entire ring/door face behind the near wall crest — a design-review
            // framing, the same departure BuildMv758LppeSalvo's own close-up angle already takes from
            // the rig.
            const float pitch = 14f;
            const float distance = 5.5f;

            AreaGate gate = null;

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + the World 2 gate skin pass finish

                GameObject gateGo = GameObject.Find(gateId);
                gate = gateGo != null ? gateGo.GetComponent<AreaGate>() : null;
                if (gate == null) gate = FindFirstObjectByType<AreaGate>();
                if (gate == null) throw new CaptureAbortException("World 2 built no AreaGate to shoot");

                // Out of frame, not out of the scene: Max's own spawn sits close enough to some gates
                // that the camera ended up looking straight into his own head model (caught during this
                // ticket's own QA pass) — nothing downstream of Awake needs him active for a single
                // still frame.
                var playerGo = GameObject.FindGameObjectWithTag("Player");
                if (playerGo != null) playerGo.SetActive(false);

                // Faces the gate's own FRONT (its local forward), not a fixed world axis — a gate on an
                // E/W wall is spun 90 deg by MapRuntime.BuildAreaGate, so a fixed approach direction
                // would view half the gates in the map edge-on instead of framing the ring.
                Vector3 approach = gate.transform.forward;
                if (approach.sqrMagnitude < 0.01f) approach = Vector3.forward;
                approach.Normalize();

                Vector3 focus = gate.transform.position + Vector3.up * 0.15f;
                Quaternion facing = Quaternion.LookRotation(approach, Vector3.up) * Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - facing * Vector3.forward * distance, facing);
                for (int i = 0; i < 3; i++) yield return null;
            }

            IEnumerator OpenGate(Camera cam)
            {
                gate.ForceOpen();
                for (int i = 0; i < 40; i++) yield return null;   // >> 0.45s slide at the project's 60fps target
            }

            return new CapturePreset
            {
                Key = "mv-w2-gate-doors",
                LogTag = "[MV759Capture]",
                Flag = "-mv759shot",
                ArmFile = "Temp/mv759.arm",
                HeadlessMarker = "Temp/mv759.headless",
                DoneFileName = "_mv759_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same WorldIndex seeding as BuildMv742StormdrainCheck/BuildMv755StormdrainDressingCheck.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot>
                {
                    new CaptureShot("MV-759-gate-closed", NoSetup),
                    new CaptureShot("MV-759-gate-open", OpenGate),
                },
            };
        }

        // ---- MV770RocketSalvo (MV-770) --------------------------------------------------------

        /// <summary>Frames a stand-in <see cref="ShoulderRack"/> firing a real, auto-triggered salvo at
        /// a live target robot — proof the staggered launch (MV-770: three rockets over a 0.24s window,
        /// not one same-frame burst) and the rescaled body/nose/fins/exhaust are what actually lands on
        /// screen, not a description of them. Same stand-in-emitter idiom
        /// <see cref="BuildMv758LppeSalvo"/> uses (a fresh GameObject, not the real Player-tagged Max) —
        /// sidesteps the real Max's own camera rig/nameplate/HUD entanglement entirely, since this
        /// preset only needs the rack's own real firing/timing path on screen, not Max himself. Same
        /// "set the RIG state directly, don't drive the whole morph choreography" shortcut
        /// <c>MV768WeaponNodesAreWiredTests.AssertCluster</c> uses in EditMode.</summary>
        private static CapturePreset BuildMv770RocketSalvo()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";
            // Near-top-down rather than the game's own ~60 deg rig angle — a design-review framing,
            // the same departure BuildMv759GateDoorsCheck's own close-up angle already takes from the
            // rig, and one that can never show sky/horizon by construction (this ticket's own capture
            // pass burned several iterations on a horizon-line artifact at pitch 60 whose actual cause
            // was never pinned down; looking straight down sidesteps the whole class of bug).
            const float pitch = 88f;
            const float distance = 8f;
            // Just past two of the three staggered launches (0s/0.12s/0.24s spacing) — enough for
            // multiple rockets to be visibly in flight at once without waiting out the third.
            const float settleSeconds = 0.16f;
            const float maxWaitSeconds = 3f; // >> the rack's own 1.8s base reload window

            GameObject rackGo = null;
            GameObject targetGo = null;

            IEnumerator Setup(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                var hud = FindFirstObjectByType<HudController>();
                if (hud != null) hud.gameObject.SetActive(false);

                // Out of frame, not out of the scene: the REAL Max's always-on nameplate/health bar
                // otherwise sits right on top of the stand-in rack and dominates the shot (caught during
                // this ticket's own capture pass). Same "hide the real Max for a single still frame"
                // idiom BuildMv759GateDoorsCheck already uses.
                var playerGo = GameObject.FindGameObjectWithTag("Player");
                if (playerGo != null) playerGo.SetActive(false);

                // The largest open room on the map. Vector3.left, not Max's own spawn facing (world
                // +Z) — the same direction BuildWaterGroundTrail/BuildMv617WaterReach/BuildMv674TeleportCrackle
                // all deliberately fire/blink toward, specifically because the Entry room's own
                // fences/hedges sit directly in front of Max's default spawn facing and clipped straight
                // through an earlier version of this capture (caught during this ticket's own pass).
                Vector3 focus = CaptureDirector.OpenZoneCenter() ?? Vector3.zero;
                Vector3 aimDir = Vector3.left;

                rackGo = new GameObject("MV770CaptureRack");
                rackGo.transform.SetPositionAndRotation(focus, Quaternion.LookRotation(aimDir, Vector3.up));
                rackGo.AddComponent<CharacterController>();
                rackGo.AddComponent<ShoulderRack>();

                Vector3 targetPos = focus + aimDir * 6f;
                targetGo = BuildClusterRobot(EnemyKind.Rusher, targetPos);
                Physics.SyncTransforms();

                float waitStart = Time.time;
                while (PlayerRocket.Active.Count < 1 && Time.time - waitStart < maxWaitSeconds) yield return null;
                if (PlayerRocket.Active.Count < 1)
                    throw new CaptureAbortException("the Shoulder Rack never fired a salvo within the capture window");

                float firstLaunchAt = Time.time;
                while (Time.time - firstLaunchAt < settleSeconds) yield return null;

                // Framed on the midpoint between rack and target — same "frame the pair, not just one
                // end" recipe BuildMv616SentinelBeam uses for its own beam-between-two-actors shot.
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                Vector3 camFocus = Vector3.Lerp(focus, targetPos, 0.5f) + Vector3.up * 0.6f;
                cam.transform.SetPositionAndRotation(camFocus - rot * Vector3.forward * distance, rot);

                yield return null;
            }

            return new CapturePreset
            {
                Key = "mv770rocketsalvo",
                LogTag = "[MV770Capture]",
                Flag = "-mv770shot",
                ArmFile = "Temp/mv770.arm",
                HeadlessMarker = "Temp/mv770.headless",
                DoneFileName = "_mv770_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same HomeScreen-modal-freezes-time dodge BuildMv616SentinelBeam uses.
                    SaveSystem.ActiveSlot = 0;
                    RigBoard.UseWorld(1);   // s_rkt/s_sal live only on rig_board.world2.json
                    WeaponSystemState.SecondaryKind = SecondaryKind.ShoulderRack;
                    RigState.RestoreSnapshot(new Dictionary<string, int> { { "s_rkt", 1 }, { "s_sal", 3 } },
                        new[] { "SECONDARY" });
                    PickupWallet.SetPowerCellSecondary(3);
                },
                Shots = new List<CaptureShot> { new CaptureShot("MV-770-rocket-salvo", Setup) },
                Cleanup = () =>
                {
                    if (rackGo != null) Destroy(rackGo);
                    if (targetGo != null) Destroy(targetGo);
                },
            };
        }

        // ---- MV773GrateLurker (MV-773) --------------------------------------------------------

        /// <summary>The AC's own "one capture of a grate with a Lurker on it" — builds a real
        /// <see cref="MaxWorlds.Rendering.StormdrainKit.BuildGrate"/> and a real Lurker
        /// (<see cref="BuildClusterRobot"/>, held at its default Chase/body-visible state, same as
        /// every other stand-in in this file) centred on the SAME tile, proof of the actual result
        /// rather than a description of one.</summary>
        private static CapturePreset BuildMv773GrateLurker()
        {
            // Near-top-down (same departure BuildMv770RocketSalvo/BuildMv759GateDoorsCheck already take
            // from the game's own ~60 deg rig angle) and pulled back far enough that the 1x1 m grate's
            // own frame/bars/rim are visible around the Lurker's feet, not hidden entirely under its body.
            const float pitch = 80f;
            const float distance = 3.2f;
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

            GameObject grateGo = null;
            GameObject lurkerGo = null;

            IEnumerator Setup(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                Vector3 focus = CaptureDirector.OpenZoneCenter() ?? Vector3.zero;
                var corner = new Vector2(focus.x - 0.5f, focus.z - 0.5f);

                // floorTopY 0.02 (not the production default 0): this stand-in has no real floor slab
                // under it the way a built map does, only Backyard_Slice's own grass plane — a hair
                // above it avoids the two coplanar surfaces fighting for the same pixel in a still frame.
                grateGo = MaxWorlds.Rendering.StormdrainKit.BuildGrate(null, "MV773CaptureGrate", corner, floorTopY: 0.02f);
                lurkerGo = BuildClusterRobot(EnemyKind.Lurker, focus);

                for (int i = 0; i < 3; i++) yield return null;   // let the rig build and settle

                var rot = Quaternion.Euler(pitch, 0f, 0f);
                Vector3 camFocus = focus + Vector3.up * 0.15f;
                cam.transform.SetPositionAndRotation(camFocus - rot * Vector3.forward * distance, rot);

                yield return null;
            }

            return new CapturePreset
            {
                Key = "mv773gratelurker",
                LogTag = "[MV773Capture]",
                Flag = "-mv773shot",
                ArmFile = "Temp/mv773.arm",
                HeadlessMarker = "Temp/mv773.headless",
                DoneFileName = "_mv773_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                Shots = new List<CaptureShot> { new CaptureShot("MV-773-grate-lurker", Setup) },
                Cleanup = () =>
                {
                    if (grateGo != null) Destroy(grateGo);
                    if (lurkerGo != null) Destroy(lurkerGo);
                },
            };
        }

        // ---- MV775ReplicatorMachine (MV-775) --------------------------------------------------

        /// <summary>The AC's own "one capture mid-cycle" — builds a real Replicator and a real Rusher,
        /// lures and consumes it through the Intake beat via the actual <see cref="Replicator.TickLure"/>/
        /// <see cref="Replicator.TickConsumption"/> path (no scripted VFX call), then advances exactly
        /// half of <see cref="Replicator.CycleSeconds"/> so the shot lands mid-Cycle: hatch shut again,
        /// fan spinning, amber hatch-glow still lit while the pair it becomes is cooking — proof of the
        /// actual staged result, not a description of one.</summary>
        private static CapturePreset BuildMv775ReplicatorMachine()
        {
            const float pitch = 60f;
            const float distance = 5f;
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images";

            GameObject replicatorGo = null;
            GameObject rusherGo = null;

            IEnumerator Setup(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                Vector3 focus = CaptureDirector.OpenZoneCenter() ?? Vector3.zero;

                replicatorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                replicatorGo.name = "MV775CaptureReplicator";
                replicatorGo.transform.position = focus;
                replicatorGo.transform.localScale = new Vector3(2f, 2f, 1.5f);
                var replicator = replicatorGo.AddComponent<Replicator>();
                replicator.Configure(1);

                for (int i = 0; i < 3; i++) yield return null;   // let the generated body (hatch/fan/LED) settle

                rusherGo = BuildClusterRobot(EnemyKind.Rusher, replicator.HatchPosition + new Vector3(5f, 0f, 0f));
                // BuildClusterRobot disables its RobotEnemy ("hold the pose") — re-enabling re-fires
                // OnEnable, which is what actually populates RobotEnemy.Active for TickLure to find it.
                rusherGo.GetComponent<RobotEnemy>().enabled = true;

                // The real lure/intake path, not a scripted pose: TickLure hands it the hatch as its
                // seek target, then TickConsumption draws it in and starts the Cycle beat.
                replicator.TickLure();
                rusherGo.transform.position = replicator.HatchPosition;
                replicator.TickConsumption(Replicator.IntakeSeconds + 0.01f);
                replicator.TickConsumption(Replicator.CycleSeconds * 0.5f);   // land the shot mid-Cycle

                var rot = Quaternion.Euler(pitch, 0f, 0f);
                Vector3 camFocus = focus + Vector3.up * 1f;
                cam.transform.SetPositionAndRotation(camFocus - rot * Vector3.forward * distance, rot);

                cam.Render();
                for (int i = 0; i < 3; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv775replicatormachine",
                LogTag = "[MV775Capture]",
                Flag = "-mv775shot",
                ArmFile = "Temp/mv775.arm",
                HeadlessMarker = "Temp/mv775.headless",
                DoneFileName = "_mv775_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same clean-profile guard MV616SentinelBeam/MV693Replicator's own presets use: on
                    // a fresh profile HomeScreen's pick-a-slot modal freezes Time.timeScale at 0.
                    SaveSystem.ActiveSlot = 0;
                },
                Shots = new List<CaptureShot> { new CaptureShot("MV-775-mid-cycle", Setup) },
                Cleanup = () =>
                {
                    if (replicatorGo != null) Destroy(replicatorGo);
                    if (rusherGo != null) Destroy(rusherGo);
                },
            };
        }
    }
}
