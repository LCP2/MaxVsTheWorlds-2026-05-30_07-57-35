using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Profiling;
using static UnityEngine.Object;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.CameraRig;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Intro;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Rendering;
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

        /// <summary>MV-1024: optional pixel readback of the exact <see cref="Texture2D"/> <see cref="CaptureDirector.Capture"/>
        /// is about to encode to PNG — for a rendered-colour AC (Tier 3, CLAUDE.md's testing policy)
        /// that needs a measured value in the hand-off report, not a second render and not an
        /// EditMode test asserting the authored constant.</summary>
        public readonly Action<Texture2D> Measure;

        public CaptureShot(string name, Func<Camera, IEnumerator> setup, string[] mirrorPaths = null, Action<Texture2D> measure = null)
        {
            Name = name;
            Setup = setup;
            MirrorPaths = mirrorPaths;
            Measure = measure;
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

        /// <summary>Is any preset currently armed? Public so the Home screen (MV-1032) can take the
        /// same skip-the-modal bypass PressKitDirector/UiScreensDirector/PerfCaptureDirector already
        /// use, routing a capture run through <c>HomeScreen.StartSlot</c>'s clean wipe instead of
        /// trusting whatever a stale <see cref="SaveSystem.ActiveSlot"/> already points at — a capture
        /// must never resume a live/saved run.</summary>
        public static bool Armed() => ArmedPreset() != null;

        private static bool IsArmed(CapturePreset preset)
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, preset.Flag, StringComparison.OrdinalIgnoreCase)) return true;
            try { return File.Exists(preset.ArmFile); } catch { return false; }
        }

        /// <summary>Set the moment <see cref="Finish"/> or <see cref="Fail"/> writes the done-marker —
        /// <see cref="Watchdog"/>'s cue that it no longer needs to force one (MV-1032).</summary>
        private bool _done;

        private void Start()
        {
            StartCoroutine(Run());
            StartCoroutine(Watchdog());
        }

        /// <summary>Independent of anything <see cref="Run"/> awaits on: an unattended capture can hang
        /// for reasons no preset-authored wait sees coming (MV-1032) — Max dying mid-shot freezes
        /// <see cref="Time.timeScale"/>, which used to freeze every preset wait built on scaled
        /// <see cref="Time.time"/> right along with it. This timer runs on <see cref="Time.unscaledTime"/>
        /// so it keeps counting through a frozen game, and forces the same <see cref="Fail"/> path any
        /// other capture failure takes once <see cref="CapturePreset.TimeoutSeconds"/> is up — so a
        /// headless run always exits via its own done-marker instead of hanging for the editor-side
        /// watchdog (<c>CaptureEntryPoint.PollHeadless</c>) to notice.</summary>
        private IEnumerator Watchdog()
        {
            var preset = _preset;
            float deadline = Time.unscaledTime + (float)preset.TimeoutSeconds;
            while (!_done && Time.unscaledTime < deadline) yield return null;
            if (_done) yield break;

            var liveDirs = new List<string>();
            foreach (var dir in preset.OutputDirs)
            {
                try { Directory.CreateDirectory(dir); liveDirs.Add(dir); }
                catch { /* best effort — Fail() below falls back to "." if this stays empty */ }
            }
            Fail(preset, liveDirs, $"watchdog: exceeded TimeoutSeconds ({preset.TimeoutSeconds}s) with no result");
        }

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

        /// <summary>Pumps a nested setup/prepare IEnumerator, catching any exception (MV-1032: not just
        /// a deliberate <see cref="CaptureAbortException"/> — an unplanned one, e.g. a reflection call
        /// failing or a null scene reference, used to end the coroutine with no done-marker at all,
        /// which is exactly what left CI hanging until the watchdog took over) into
        /// <see cref="_failReason"/> instead of letting it fault the whole coroutine — a plain
        /// try/catch can't wrap a yield, so the MoveNext() driving happens inside the try and the yield
        /// happens outside it.</summary>
        private IEnumerator Drive(IEnumerator inner)
        {
            while (true)
            {
                bool more;
                try { more = inner.MoveNext(); }
                catch (Exception ex)
                {
                    _failReason = ex is CaptureAbortException ? ex.Message : ex.ToString();
                    yield break;
                }
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

                shot.Measure?.Invoke(tex);

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
            if (_done) return;
            _done = true;
            var manifest = new System.Text.StringBuilder();
            foreach (var shot in preset.Shots) manifest.Append(shot.Name).Append(".png\n");
            string report = "ok\n" + manifest + (preset.ExtraReport?.Invoke() ?? "");
            File.WriteAllText(Path.Combine(liveDirs[0], preset.DoneFileName), report);
            Log(preset, preset.Key + " capture complete. " + report);
            QuitIfStandalonePlayer(0);
        }

        private void Fail(CapturePreset preset, List<string> liveDirs, string why)
        {
            if (_done) return;
            _done = true;
            LogWarn(preset, preset.Key + " capture aborted: " + why);
            try
            {
                string dir = liveDirs.Count > 0 ? liveDirs[0] : ".";
                File.WriteAllText(Path.Combine(dir, preset.DoneFileName), "fail: " + why + "\n");
            }
            catch { /* best effort */ }
            QuitIfStandalonePlayer(1);
        }

        /// <summary>MV-1063: every preset above was written for <see cref="CaptureEntryPoint"/>'s
        /// Editor-Play-Mode harness, which stops play mode and exits the EDITOR process itself once the
        /// done-marker lands — nothing here ever quit a running PLAYER. A ticket whose AC specifically
        /// needs player-build evidence (not Editor evidence — shader-variant stripping and other
        /// build-only effects don't reproduce in the Editor) launches the already-built standalone .exe
        /// with this preset's own command-line flag instead of going through the Editor at all; without
        /// this, that process would sit at its done-marker forever under <c>-batchmode</c>, since nothing
        /// else ever calls <see cref="Application.Quit"/> for it. A no-op in the Editor (Play Mode exits
        /// via <c>EditorApplication.isPlaying = false</c> in <see cref="CaptureEntryPoint"/> instead,
        /// same as it always has — this never fires there).</summary>
        private static void QuitIfStandalonePlayer(int exitCode)
        {
            if (Application.isEditor) return;
            Application.Quit(exitCode);
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
            Add(BuildMv1025RocketFireEvent());
            Add(BuildMv773GrateLurker());
            Add(BuildMv775ReplicatorMachine());
            Add(BuildMv818CoverCheck());
            Add(BuildMv819PipeCheck());
            Add(BuildMv821DeckWalkwayCheck());
            Add(BuildMv822HazardBandingCheck());
            Add(BuildMv825LppeFire());
            Add(BuildMv824LitGroundCheck());
            Add(BuildMv857MaxWorld2Colors());
            Add(BuildMv913MissileLauncherCheck());
            Add(BuildMv939W2ColorCheck());
            Add(BuildMv965Corridor());
            Add(BuildMv967Corridor());
            Add(BuildMv993Crossing());
            Add(BuildMv1018Anchorhead());
            Add(BuildMv1024SentinelColorCheck());
            Add(BuildMv1019ReefFloorCheck());
            Add(BuildMv1063ReefFixCheck());
            Add(BuildMv1064UndertowLatchCheck());
            Add(BuildMv1121UndertowBeamCheck());
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
            e.SetCutsceneFrozen(true);   // MV-1081: hold the pose — nothing in this shot for it to chase

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
        /// own ~0.1-0.2s life hasn't faded yet. Waits on <see cref="Time.unscaledTime"/> rather than a
        /// fixed frame count (MV-1032: not scaled <c>Time.time</c> — an unattended Max dying mid-capture
        /// freezes <see cref="Time.timeScale"/>, which would freeze a scaled-time wait right along with
        /// it): an idle headless scene can render far more or fewer frames per real second than 60, and
        /// <see cref="MaxWorlds.VFX.CombatVfx"/>'s own beat coroutine stagger (<c>TeleportFlashStagger</c>,
        /// 0.08s) is itself real-time-driven, not frame-count-driven.</summary>
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
                float teleportedAt = Time.unscaledTime;
                if (!abilities.TryTeleport(Vector3.left))
                    throw new CaptureAbortException("TryTeleport returned false — ability not acquired or on cooldown");

                // transform.position is already the arrival point (TryTeleport sets it synchronously).
                Vector3 focus = maxGo.transform.position; focus.y = 1f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);

                while (Time.unscaledTime - teleportedAt < settleSeconds) yield return null;
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

        // ---- MV1019ReefFloorCheck (MV-1019 AC3) -----------------------------------------------

        /// <summary>Same boot/frame idiom as <see cref="BuildMv742StormdrainCheck"/>, one world
        /// further in — World 3's real shipped config, Max's own spawn in area a1, the fixed rig's own
        /// angle — proof of how the lifted Reef floor/crate palette (MV-1019) actually reads together,
        /// not a description of one. ONE capture only, per the ticket's own "no art iteration"
        /// instruction; Lee judges the look on staging.</summary>
        private static CapturePreset BuildMv1019ReefFloorCheck()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + WorldMaterials/ReefKit finish

                var player = FindFirstObjectByType<PlayerController>();
                if (player == null) throw new CaptureAbortException("World 3 built no PlayerController to shoot");

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
                Key = "mv1019reeffloor",
                LogTag = "[MV1019Capture]",
                Flag = "-mv1019shot",
                ArmFile = "Temp/mv1019.arm",
                HeadlessMarker = "Temp/mv1019.headless",
                DoneFileName = "_mv1019_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same WorldIndex seeding idiom as BuildMv742StormdrainCheck, one world further in.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 2;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot> { new CaptureShot("MV-1019-reef-floor", NoSetup) },
            };
        }

        // ---- MV1063ReefFixCheck (MV-1063 AC1/AC2) ---------------------------------------------

        /// <summary>MV-1063's own player-build evidence. A real World 3 area's hydroponic-bed and
        /// wall-lamp placement is level-authored (<c>MaxWorlds.Arena.ReefHydroponics</c>'s own
        /// cover/gate/garrison-aware search, not a fixed spot this capture could frame reliably) — same
        /// reason <see cref="BuildMv616SentinelBeam"/>/<see cref="BuildMv693Replicator"/>/
        /// <see cref="BuildMv1024SentinelColorCheck"/> all build a synthetic probe instead of trusting
        /// where the real map happens to put things. This probe calls the exact production dressing
        /// code (<see cref="ReefKit.DressHull"/>, <see cref="ReefKit.BuildHydroponicBed"/>) a real World
        /// 3 area calls, then reflectively fires the installed <see cref="RuntimeSurfaceDirector"/>'s
        /// own <c>Sweep()</c> — simulating the one-time pass that runs after every object's Awake in a
        /// real scene, which is NOT guaranteed to land before this coroutine's own next frame if left to
        /// Unity's own scheduling. Launched directly against the standalone .exe
        /// (<c>-mv1063shot</c>, no Editor involved at all) so the pixels this measures are player-build
        /// ones, not Editor ones — the ticket's own AC1.</summary>
        private static CapturePreset BuildMv1063ReefFixCheck()
        {
            const float pitch = 60f;
            const float distance = 16f;
            const int floorPatchBoxHalf = 50;
            const int bedLampBoxHalf = 30;
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

            GameObject host = null;
            Vector3 bedWorldPos = Vector3.zero;
            Vector3 lampWorldPos = Vector3.zero;
            Vector3 floorPatchWorldPos = new Vector3(4f, 0.1f, 2f);   // open floor, clear of the bed (0,-2) and the wall (z=6)

            float cyanVioletPercent = -1f;
            float floorLuminanceStdDev = -1f;
            float bedMeanValue = -1f;
            float lampMeanValue = -1f;

            IEnumerator Setup(Camera cam)
            {
                host = new GameObject("MV1063 Reef Fix Probe");

                GameObject floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floorGo.name = "Probe Floor";
                floorGo.transform.SetParent(host.transform, false);
                floorGo.transform.localScale = new Vector3(14f, 0.2f, 14f);
                floorGo.AddComponent<StructuralFloor>();

                GameObject wallGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wallGo.name = "Probe Wall";
                wallGo.transform.SetParent(host.transform, false);
                wallGo.transform.position = new Vector3(0f, 1.5f, 6f);
                wallGo.transform.localScale = new Vector3(14f, 3f, 0.3f);
                wallGo.AddComponent<StructuralWall>();

                // The exact call BackyardPath.Awake makes for a real World 3 scene: sets
                // M_ShipFloor/M_ShipWall, lays the circuit spine along the wall base, builds the
                // observation-glass/wall-lamp groups, and (MV-1063's fix) tags every piece it builds
                // with KeepsOwnMaterial.
                ReefKit.DressHull(host.transform);

                bedWorldPos = new Vector3(0f, 0f, -2f);
                ReefKit.BuildHydroponicBed(host.transform, bedWorldPos);

                yield return null;
                yield return null;

                var director = FindFirstObjectByType<RuntimeSurfaceDirector>();
                if (director == null)
                    throw new CaptureAbortException("no RuntimeSurfaceDirector installed — its AfterSceneLoad hook did not fire");
                typeof(RuntimeSurfaceDirector).GetMethod("Sweep", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(director, null);

                Transform lampsRoot = host.transform.Find("Wall Lamps");
                if (lampsRoot == null || lampsRoot.childCount == 0)
                    throw new CaptureAbortException("DressHull built no wall lamps for the probe wall");
                lampWorldPos = lampsRoot.GetChild(0).position;

                var rot = Quaternion.Euler(pitch, 0f, 0f);
                Vector3 focus = new Vector3(0f, 1f, 1f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);

                yield return null;
            }

            void Measure(Texture2D tex)
            {
                var cam = Camera.main;
                if (cam == null) return;

                // AC2a: cyan (hue 170-200deg) or violet (hue 265-295deg) at value>=0.7, across every
                // pixel the probe put on screen — at this framing that's floor, wall, circuit spine,
                // bed and lamp, i.e. "the floor pixels" the AC means by a World 3 surface that reads
                // as Reef rather than flat.
                int cyanOrViolet = 0, totalPixels = tex.width * tex.height;
                for (int y = 0; y < tex.height; y++)
                {
                    for (int x = 0; x < tex.width; x++)
                    {
                        Color.RGBToHSV(tex.GetPixel(x, y), out float h, out float s, out float v);
                        float deg = h * 360f;
                        bool cyan = deg >= 170f && deg <= 200f;
                        bool violet = deg >= 265f && deg <= 295f;
                        if ((cyan || violet) && v >= 0.7f) cyanOrViolet++;
                    }
                }
                cyanVioletPercent = totalPixels > 0 ? cyanOrViolet * 100f / totalPixels : 0f;

                // AC2b: plate-seam contrast — luminance std-dev over an open floor patch, clear of the
                // bed/wall/lamp, compared against the SAME patch captured on the pre-fix commit (see
                // the ticket comment for that paired baseline run).
                floorLuminanceStdDev = SampleLuminanceStdDev(tex, cam, floorPatchWorldPos, floorPatchBoxHalf);

                // AC2c: hydroponic bed / wall lamp mean value >= 0.6.
                bedMeanValue = SampleMeanValue(tex, cam, bedWorldPos + Vector3.up * 0.15f, bedLampBoxHalf);
                lampMeanValue = SampleMeanValue(tex, cam, lampWorldPos, bedLampBoxHalf);
            }

            return new CapturePreset
            {
                Key = "mv1063reeffix",
                LogTag = "[MV1063Capture]",
                Width = 1600,
                Height = 1000,
                Flag = "-mv1063shot",
                ArmFile = "Temp/mv1063.arm",
                HeadlessMarker = "Temp/mv1063.headless",
                DoneFileName = "_mv1063_done.txt",
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                Shots = new List<CaptureShot> { new CaptureShot("MV-1063-reef-fix", Setup, measure: Measure) },
                Cleanup = () => { if (host != null) Destroy(host); },
                ExtraReport = () =>
                    $"cyan/violet floor pixels: {cyanVioletPercent:F2}% (AC2a >= 1.50%)\n" +
                    $"floor patch luminance std-dev: {floorLuminanceStdDev:F4} (AC2b: compare against the pre-fix baseline run, needs >= 3x)\n" +
                    $"hydroponic bed mean value: {bedMeanValue:F3} (AC2c >= 0.600)\n" +
                    $"wall lamp mean value: {lampMeanValue:F3} (AC2c >= 0.600)\n",
            };
        }

        /// <summary>Mean HSV value (brightness) over a square pixel box centred on <paramref name="worldPos"/>'s
        /// own viewport projection — shared by every bed/lamp/body sample in this file
        /// (<see cref="BuildMv1024SentinelColorCheck"/>'s own inline version predates this extraction).</summary>
        private static float SampleMeanValue(Texture2D tex, Camera cam, Vector3 worldPos, int boxHalf)
        {
            Vector3 vp = cam.WorldToViewportPoint(worldPos);
            int cx = Mathf.RoundToInt(vp.x * tex.width);
            int cy = Mathf.RoundToInt(vp.y * tex.height);
            double sum = 0;
            int count = 0;
            for (int dy = -boxHalf; dy <= boxHalf; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= tex.height) continue;
                for (int dx = -boxHalf; dx <= boxHalf; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= tex.width) continue;
                    Color.RGBToHSV(tex.GetPixel(x, y), out _, out _, out float v);
                    sum += v;
                    count++;
                }
            }
            return count > 0 ? (float)(sum / count) : -1f;
        }

        /// <summary>Population std-dev of HSV value over the same kind of box <see cref="SampleMeanValue"/>
        /// samples — the plate-seam-contrast metric AC2b asks for (a flat, undetailed surface reads as a
        /// near-zero std-dev; visible seams/tiling push it up).</summary>
        private static float SampleLuminanceStdDev(Texture2D tex, Camera cam, Vector3 worldPos, int boxHalf)
        {
            Vector3 vp = cam.WorldToViewportPoint(worldPos);
            int cx = Mathf.RoundToInt(vp.x * tex.width);
            int cy = Mathf.RoundToInt(vp.y * tex.height);
            var values = new List<float>();
            for (int dy = -boxHalf; dy <= boxHalf; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= tex.height) continue;
                for (int dx = -boxHalf; dx <= boxHalf; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= tex.width) continue;
                    Color.RGBToHSV(tex.GetPixel(x, y), out _, out _, out float v);
                    values.Add(v);
                }
            }
            if (values.Count == 0) return -1f;
            float mean = 0f;
            foreach (float v in values) mean += v;
            mean /= values.Count;
            float sumSq = 0f;
            foreach (float v in values) sumSq += (v - mean) * (v - mean);
            return Mathf.Sqrt(sumSq / values.Count);
        }

        // ---- Mv1064UndertowLatchCheck (MV-1064 AC3) --------------------------------------------

        /// <summary>MV-1064 AC3: one standalone-PLAYER capture (this preset, launched via its own
        /// <c>-mv1064shot</c> flag — never <c>-nographics</c>, so this is the actual player-build
        /// shader/material path the ticket's own point #11 calls out) of UNDERTOW latched onto a
        /// robot, wrapped in the new coils/sparks. A fresh <see cref="Undertow"/> on a synthetic
        /// GameObject (no <see cref="PlayerController"/> attached, so <c>aimSource</c> stays null and
        /// <see cref="Undertow.SetFiring"/> drives it directly) aimed point-blank at a stationary
        /// <see cref="RobotEnemy"/> built the same <c>BuildClusterRobot</c> way
        /// <see cref="BuildHealthBarCluster"/>/<see cref="BuildMv758LppeSalvo"/> already do — same
        /// "fresh probe, not the real Player-tagged Max" idiom <see cref="BuildMv758LppeSalvo"/>'s own
        /// doc comment explains (deterministic, no dependency on a real scene's spawn roster). Driven
        /// by REAL <c>Update()</c> frames (this runs inside an actual Play session, so
        /// <c>Time.deltaTime</c> ticks for real) until <see cref="Undertow.IsLatched"/>, then a further
        /// settle so the coils/sparks (which ease in over <c>CoilAppearSeconds</c>) are fully built up
        /// for the shot.</summary>
        private static CapturePreset BuildMv1064UndertowLatchCheck()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";
            const float pitch = 55f;
            const float distance = 3.5f;
            const float robotAheadDistance = 4f;
            const int maxAcquireFrames = 120;
            const int settleFramesAfterLatch = 30;
            const float probeRadiusMetres = 1.2f;   // AC3's own "within 1.2m of the robot"

            GameObject undertowGo = null;
            GameObject robotGo = null;
            Undertow undertow = null;
            int orangeRedPixels = -1;
            int probeRadiusPx = -1;

            IEnumerator Setup(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                WeaponSystemState.ApplyWorldLoadout(2);   // World 3 -> UNDERTOW is the active primary
                DevMode.Enabled = true;
                DevMode.InfiniteEnergy = true;

                var playerGo = GameObject.FindGameObjectWithTag("Player");
                Vector3 focus = playerGo != null ? playerGo.transform.position : (CaptureDirector.OpenZoneCenter() ?? Vector3.zero);
                Vector3 aimDir = playerGo != null ? playerGo.transform.forward : Vector3.forward;
                aimDir.y = 0f;
                if (aimDir.sqrMagnitude < 0.01f) aimDir = Vector3.forward; else aimDir.Normalize();

                undertowGo = new GameObject("MV1064CaptureUndertow");
                undertowGo.transform.SetPositionAndRotation(focus, Quaternion.LookRotation(aimDir, Vector3.up));
                undertow = undertowGo.AddComponent<Undertow>();

                robotGo = BuildClusterRobot(EnemyKind.Rusher, focus + aimDir * robotAheadDistance);
                Physics.SyncTransforms();

                undertow.SetFiring(true);

                int frame = 0;
                while (!undertow.IsLatched && frame < maxAcquireFrames)
                {
                    yield return null;
                    frame++;
                }
                if (!undertow.IsLatched)
                    throw new CaptureAbortException($"Undertow never latched onto the probe robot within {maxAcquireFrames} frames");

                for (int i = 0; i < settleFramesAfterLatch; i++) yield return null;   // let the coils/sparks ease fully in and build up

                var rot = Quaternion.Euler(pitch, 0f, 0f);
                Vector3 camFocus = robotGo.transform.position + Vector3.up * 0.6f;
                cam.transform.SetPositionAndRotation(camFocus - rot * Vector3.forward * distance, rot);

                yield return null;
            }

            void Measure(Texture2D tex)
            {
                var cam = Camera.main;
                if (cam == null || robotGo == null) { orangeRedPixels = 0; return; }

                Vector3 centre = robotGo.transform.position + Vector3.up * 0.6f;
                Vector3 edge = centre + cam.transform.right * probeRadiusMetres;
                Vector3 cvp = cam.WorldToViewportPoint(centre);
                Vector3 evp = cam.WorldToViewportPoint(edge);
                int cx = Mathf.RoundToInt(cvp.x * tex.width);
                int cy = Mathf.RoundToInt(cvp.y * tex.height);
                int ex = Mathf.RoundToInt(evp.x * tex.width);
                int ey = Mathf.RoundToInt(evp.y * tex.height);
                float radiusPx = Mathf.Max(4f, Mathf.Sqrt((ex - cx) * (ex - cx) + (float)(ey - cy) * (ey - cy)));
                probeRadiusPx = Mathf.RoundToInt(radiusPx);

                // AC3: "hue 0-40deg, value >= 0.6" -- orange/red family, within probeRadiusMetres
                // (approximated as a screen-space circle at this fixed framing, the same box-around-a-
                // world-point idiom SampleMeanValue/SampleLuminanceStdDev use elsewhere in this file).
                int count = 0;
                int boxHalf = Mathf.CeilToInt(radiusPx);
                for (int dy = -boxHalf; dy <= boxHalf; dy++)
                {
                    int y = cy + dy;
                    if (y < 0 || y >= tex.height) continue;
                    for (int dx = -boxHalf; dx <= boxHalf; dx++)
                    {
                        int x = cx + dx;
                        if (x < 0 || x >= tex.width) continue;
                        if (dx * dx + dy * dy > radiusPx * radiusPx) continue;
                        Color.RGBToHSV(tex.GetPixel(x, y), out float h, out float s, out float v);
                        float deg = h * 360f;
                        if (deg <= 40f && v >= 0.6f) count++;
                    }
                }
                orangeRedPixels = count;
            }

            return new CapturePreset
            {
                Key = "mv1064undertowlatch",
                LogTag = "[MV1064Capture]",
                Flag = "-mv1064shot",
                ArmFile = "Temp/mv1064.arm",
                HeadlessMarker = "Temp/mv1064.headless",
                DoneFileName = "_mv1064_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same HomeScreen-modal-freezes-time dodge BuildMv758LppeSalvo/BuildMv616SentinelBeam
                    // use — without it, HomeScreen.Start() has no slot to resume and blocks on the
                    // pick-a-slot modal forever in an unattended capture.
                    SaveSystem.ActiveSlot = 0;
                },
                Shots = new List<CaptureShot> { new CaptureShot("MV-1064-undertow-latch", Setup, measure: Measure) },
                Cleanup = () =>
                {
                    if (undertowGo != null) Destroy(undertowGo);
                    if (robotGo != null) Destroy(robotGo);
                },
                ExtraReport = () =>
                    $"orange/red pixels within {probeRadiusMetres}m ({probeRadiusPx}px) of the latched robot: " +
                    $"{orangeRedPixels} (AC3 >= 300)\n",
            };
        }

        // ---- Mv1121UndertowBeamCheck (MV-1121 AC2) --------------------------------------------

        /// <summary>MV-1121 AC2: up to four captures of the refreshed UNDERTOW beam in World 3
        /// (<c>WorldIndex</c> seeded to 2, same idiom as <see cref="BuildMv1019ReefFloorCheck"/>) — free
        /// and searching, bending toward a candidate robot, locked on an ordinary robot, and locked on
        /// World 3's largest robot kind (<see cref="EnemyKind.Brute"/>, the biggest <c>BodyScale</c>/
        /// <c>ColliderRadius</c> among <see cref="EnemyArchetype"/>'s roster). Same synthetic-probe
        /// technique as <see cref="BuildMv1064UndertowLatchCheck"/> (a fresh <see cref="Undertow"/> with
        /// no <see cref="PlayerController"/> aim source, driven by real <c>Update()</c> frames in a real
        /// Play session) so the four states are deterministic rather than dependent on where World 3's
        /// own live roster happens to be standing.</summary>
        private static CapturePreset BuildMv1121UndertowBeamCheck()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";
            const float pitch = 55f;
            const float distance = 6f;
            const int maxWaitFrames = 180;

            GameObject undertowGo = null;
            Undertow undertow = null;
            GameObject ordinaryGo = null;
            GameObject bigGo = null;
            Vector3 origin = Vector3.zero;
            Vector3 aimDir = Vector3.forward;

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                // PauseSpawns (BeforeSceneLoad) only stops NEW spawns -- World 3's own area-accumulation
                // pass can already have placed a robot or two before this coroutine ever runs, and the
                // probe's own candidate search would happily latch onto one of those instead of the
                // synthetic robots this preset places on purpose. Clear the board first.
                foreach (var stray in FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
                    if (stray != null) Destroy(stray.gameObject);

                WeaponSystemState.ApplyWorldLoadout(2);   // World 3 -> UNDERTOW is the active primary
                DevMode.Enabled = true;
                DevMode.InfiniteEnergy = true;

                // The largest OPEN zone on the loaded map, not the real player's own spawn point --
                // World 3's real level geometry (walls, cover, hydroponic beds) sits close enough to
                // Max's own spawn that a robot placed a few metres off-angle from it can clip LineOfSight/
                // CombatLevel checks the real seek logic depends on. An open zone keeps this probe's own
                // candidate search geometry clean regardless of which area the map happens to open into.
                var playerGo = GameObject.FindGameObjectWithTag("Player");
                origin = CaptureDirector.OpenZoneCenter() ?? (playerGo != null ? playerGo.transform.position : Vector3.zero);
                aimDir = playerGo != null ? playerGo.transform.forward : Vector3.forward;
                aimDir.y = 0f;
                aimDir = aimDir.sqrMagnitude > 0.01f ? aimDir.normalized : Vector3.forward;

                undertowGo = new GameObject("MV1121CaptureUndertow");
                undertowGo.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(aimDir, Vector3.up));
                undertow = undertowGo.AddComponent<Undertow>();
                undertow.SetFiring(true);
            }

            void FrameOn(Camera cam, Vector3 focus)
            {
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
            }

            IEnumerator FreeShot(Camera cam)
            {
                for (int i = 0; i < 60; i++) yield return null;   // let the free-aim sway settle into its own rhythm
                FrameOn(cam, origin + aimDir * (undertow.Range * 0.5f));
                for (int i = 0; i < 2; i++) yield return null;
            }

            IEnumerator BendingShot(Camera cam)
            {
                Vector3 dir = Quaternion.AngleAxis(30f, Vector3.up) * aimDir;
                Vector3 pos = origin + dir * 6f;
                ordinaryGo = BuildClusterRobot(EnemyKind.Rusher, pos);
                Physics.SyncTransforms();

                for (int i = 0; i < 10 && !undertow.IsLatched; i++) yield return null;   // bending, not yet latched
                if (undertow.IsLatched)
                    throw new CaptureAbortException("the tip latched before the bending shot could be framed -- tighten the frame budget");

                FrameOn(cam, ordinaryGo.transform.position);
                for (int i = 0; i < 2; i++) yield return null;
            }

            IEnumerator LockedOrdinaryShot(Camera cam)
            {
                int frame = 0;
                while (!undertow.IsLatched && frame < maxWaitFrames) { yield return null; frame++; }
                if (!undertow.IsLatched)
                    throw new CaptureAbortException("the tip never latched onto the ordinary robot");
                for (int i = 0; i < 20; i++) yield return null;   // let the ring/coils ease fully in

                FrameOn(cam, ordinaryGo.transform.position + Vector3.up * 0.6f);
                for (int i = 0; i < 2; i++) yield return null;
            }

            IEnumerator LockedBigShot(Camera cam)
            {
                if (ordinaryGo != null) { Destroy(ordinaryGo); ordinaryGo = null; }
                for (int i = 0; i < 10; i++) yield return null;   // let the dropped latch vanish

                foreach (var stray in FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
                    if (stray != null) Destroy(stray.gameObject);

                Vector3 pos = origin + aimDir * 6f;
                bigGo = BuildClusterRobot(EnemyKind.Brute, pos);
                Physics.SyncTransforms();

                int frame = 0;
                while (!undertow.IsLatched && frame < maxWaitFrames) { yield return null; frame++; }
                if (!undertow.IsLatched)
                    throw new CaptureAbortException("the tip never latched onto the big robot");
                for (int i = 0; i < 20; i++) yield return null;   // let the ring/coils ease fully in, sized to the bigger body

                FrameOn(cam, bigGo.transform.position + Vector3.up * 0.9f);
                for (int i = 0; i < 2; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv1121undertowbeam",
                LogTag = "[MV1121Capture]",
                Flag = "-mv1121shot",
                ArmFile = "Temp/mv1121.arm",
                HeadlessMarker = "Temp/mv1121.headless",
                DoneFileName = "_mv1121_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 120,
                BeforeSceneLoad = () =>
                {
                    // Same WorldIndex seeding idiom as BuildMv1019ReefFloorCheck.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 2;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                    // Set from frame 0 (before the scene even loads) so World 3's own live roster never
                    // gets a chance to spawn and contest the synthetic probe's candidate search.
                    DevMode.Enabled = true;
                    DevMode.PauseSpawns = true;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot>
                {
                    new CaptureShot("MV-1121-free", FreeShot),
                    new CaptureShot("MV-1121-bending", BendingShot),
                    new CaptureShot("MV-1121-locked-ordinary", LockedOrdinaryShot),
                    new CaptureShot("MV-1121-locked-big", LockedBigShot),
                },
                Cleanup = () =>
                {
                    if (undertowGo != null) Destroy(undertowGo);
                    if (ordinaryGo != null) Destroy(ordinaryGo);
                    if (bigGo != null) Destroy(bigGo);
                },
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

            // MV-791: the whole point of sinking the floor away from the bays is that the camera can
            // move without the depth buffer's pick between two coplanar surfaces changing frame to
            // frame — so the second frame's own setup is a lateral translation, not a state change, and
            // the pass/fail read is "the same surface visible in both", not any pixel measurement.
            IEnumerator TranslateCamera2m(Camera cam)
            {
                cam.transform.position += cam.transform.right * 2f;
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
                Shots = new List<CaptureShot>
                {
                    new CaptureShot("MV-755-stormdrain-kit", NoSetup),
                    // MV-791: same rig, camera translated 2 m sideways — the floor/bay depth fix's own
                    // evidence that no surface's visibility flips between the two frames.
                    new CaptureShot("MV-791-floor-depth-shifted", TranslateCamera2m),
                },
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

                float waitStart = Time.unscaledTime;
                while (PlayerRocket.Active.Count < 1 && Time.unscaledTime - waitStart < maxWaitSeconds) yield return null;
                if (PlayerRocket.Active.Count < 1)
                    throw new CaptureAbortException("the Shoulder Rack never fired a salvo within the capture window");

                float firstLaunchAt = Time.unscaledTime;
                while (Time.unscaledTime - firstLaunchAt < settleSeconds) yield return null;

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

        // ---- MV1025RocketFireEvent (MV-1025) --------------------------------------------------

        /// <summary>The ticket's own AC3: "World 2, three rockets mid-flight ... report the flame's
        /// bounding box in pixels and its peak RGB." Same real-salvo recipe <see cref="BuildMv770RocketSalvo"/>
        /// already proved out (a live <see cref="ShoulderRack"/> firing at a real target, not a scripted
        /// VFX call), just waiting for all 3 salvo rockets to be airborne instead of just the first, and
        /// reading back the flame layers' own pixels instead of only saving the PNG.</summary>
        private static CapturePreset BuildMv1025RocketFireEvent()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";
            const float pitch = 88f;
            const float distance = 5f;
            const float maxWaitSeconds = 3f;
            const int flameSampleHalf = 40; // covers the ~38x11px outer flame plus slop for camera/projection drift

            GameObject rackGo = null;
            GameObject targetGo = null;
            // Each rocket's own flame-layer CENTRE in world space (not its body position) — the outer
            // flame sits Length/2 + OuterFlameLength/2 behind the body along its own forward axis (see
            // PlayerRocket.BuildExhaustFlame), roughly 31px away at this framing: a sample box centred
            // on the BODY instead of the flame put that whole distance right at the box edge, which is
            // what clipped the real flame tip out of the very first capture pass and left only diluted
            // (alpha < 1) trail pixels behind for Measure to find.
            var flameWorldPositions = new List<Vector3>();

            var perRocketReports = new List<string>();
            int bestLength = -1, bestR = -1, bestG = -1, bestB = -1;

            IEnumerator Setup(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                var hud = FindFirstObjectByType<HudController>();
                if (hud != null) hud.gameObject.SetActive(false);

                var playerGo = GameObject.FindGameObjectWithTag("Player");
                if (playerGo != null) playerGo.SetActive(false);   // same "hide the real Max" dodge BuildMv770RocketSalvo uses

                Vector3 focus = CaptureDirector.OpenZoneCenter() ?? Vector3.zero;
                Vector3 aimDir = Vector3.left;

                rackGo = new GameObject("MV1025CaptureRack");
                rackGo.transform.SetPositionAndRotation(focus, Quaternion.LookRotation(aimDir, Vector3.up));
                rackGo.AddComponent<CharacterController>();
                rackGo.AddComponent<ShoulderRack>();

                Vector3 targetPos = focus + aimDir * 6f;
                targetGo = BuildClusterRobot(EnemyKind.Rusher, targetPos);
                Physics.SyncTransforms();

                float waitStart = Time.unscaledTime;
                while (PlayerRocket.Active.Count < 3 && Time.unscaledTime - waitStart < maxWaitSeconds) yield return null;
                if (PlayerRocket.Active.Count < 3)
                    throw new CaptureAbortException(
                        $"only {PlayerRocket.Active.Count} of 3 salvo rockets were airborne within the capture window");

                // Frame as soon as all 3 are airborne rather than waiting out a further real-time
                // settle (BuildMv770RocketSalvo's own 0.16s, tuned for ONE rocket) — the third rocket in
                // a 3-salvo already took ~0.24s (2 * the 0.12s stagger) to leave the tube, and any extra
                // wait only lets all three diverge further apart at TurnRateDegPerSec, forcing a wider,
                // more zoomed-out shot that dilutes every rocket's own flame pixels on downsample.
                for (int i = 0; i < 2; i++) yield return null;

                Vector3 centroid = Vector3.zero;
                int count = 0;
                foreach (PlayerRocket r in PlayerRocket.Active) { if (r == null) continue; centroid += r.transform.position; count++; }
                centroid /= Mathf.Max(count, 1);
                float maxRadius = 0.5f;
                foreach (PlayerRocket r in PlayerRocket.Active)
                {
                    if (r == null) continue;
                    Vector3 d = r.transform.position - centroid; d.y = 0f;
                    maxRadius = Mathf.Max(maxRadius, d.magnitude);
                }

                var rot = Quaternion.Euler(pitch, 0f, 0f);
                float framingDistance = Mathf.Max(distance, maxRadius * 2.2f + 2f);
                cam.transform.SetPositionAndRotation(centroid - rot * Vector3.forward * framingDistance, rot);
                yield return null;

                // Snapshot every live rocket's own flame-centre position on this exact frame — the same
                // frame Capture()'s manual cam.Render() below fires — so Measure()'s viewport projection
                // matches what actually landed in the saved PNG.
                CombatVfxTuning.RocketBodyTuning bodyTuning = CombatVfxTuning.RocketBody();
                float flameOffset = bodyTuning.Length * 0.5f + bodyTuning.OuterFlameLength * 0.5f;
                flameWorldPositions.Clear();
                foreach (PlayerRocket r in PlayerRocket.Active)
                    if (r != null) flameWorldPositions.Add(r.transform.position - r.transform.forward * flameOffset);
            }

            void Measure(Texture2D tex)
            {
                var cam = Camera.main;
                if (cam == null || flameWorldPositions.Count == 0) return;

                for (int ri = 0; ri < flameWorldPositions.Count; ri++)
                {
                    Vector3 vp = cam.WorldToViewportPoint(flameWorldPositions[ri]);
                    if (vp.z <= 0f) { perRocketReports.Add($"R{ri + 1}: off-camera"); continue; }
                    int cx = Mathf.RoundToInt(vp.x * tex.width);
                    int cy = Mathf.RoundToInt(vp.y * tex.height);

                    int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                    int peakR = -1, peakG = -1, peakB = -1, matched = 0;

                    for (int dy = -flameSampleHalf; dy <= flameSampleHalf; dy++)
                    {
                        int y = cy + dy;
                        if (y < 0 || y >= tex.height) continue;
                        for (int dx = -flameSampleHalf; dx <= flameSampleHalf; dx++)
                        {
                            int x = cx + dx;
                            if (x < 0 || x >= tex.width) continue;

                            Color32 c = tex.GetPixel(x, y);
                            // Additive HDR flame pixels clip bright red/white — the outer layer's
                            // (2.4, 0.35, 0.10) and inner core's (2.2, 1.7, 0.9) both tonemap well past
                            // ordinary lit-surface brightness. A plain "is this bright" threshold is
                            // enough to isolate the flame from the gunmetal body/background within each
                            // rocket's own small sample box.
                            if (c.r < 200) continue;

                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                            if (c.r > peakR) { peakR = c.r; peakG = c.g; peakB = c.b; }
                            matched++;
                        }
                    }

                    if (matched == 0) { perRocketReports.Add($"R{ri + 1}: no bright flame pixel found"); continue; }

                    int w = maxX - minX + 1, h = maxY - minY + 1, length = Mathf.Max(w, h);
                    perRocketReports.Add($"R{ri + 1}: bbox {w}x{h}px (n={matched}px), length={length}px, peak RGB=({peakR},{peakG},{peakB})");
                    if (peakR > bestR) { bestLength = length; bestR = peakR; bestG = peakG; bestB = peakB; }
                }
            }

            return new CapturePreset
            {
                Key = "mv1025rocketfireevent",
                LogTag = "[MV1025Capture]",
                Flag = "-mv1025shot",
                ArmFile = "Temp/mv1025.arm",
                HeadlessMarker = "Temp/mv1025.headless",
                DoneFileName = "_mv1025_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    SaveSystem.ActiveSlot = 0;
                    RigBoard.UseWorld(1);   // s_rkt/s_sal live only on rig_board.world2.json
                    WeaponSystemState.SecondaryKind = SecondaryKind.ShoulderRack;
                    RigState.RestoreSnapshot(new Dictionary<string, int> { { "s_rkt", 1 }, { "s_sal", 3 } },
                        new[] { "SECONDARY" });
                    PickupWallet.SetPowerCellSecondary(3);
                },
                Shots = new List<CaptureShot> { new CaptureShot("MV-1025-rockets", Setup, measure: Measure) },
                Cleanup = () =>
                {
                    if (rackGo != null) Destroy(rackGo);
                    if (targetGo != null) Destroy(targetGo);
                },
                ExtraReport = () => (perRocketReports.Count > 0 ? string.Join("\n", perRocketReports) + "\n" : "no rockets sampled\n") +
                    (bestR >= 0 ? $"best: length={bestLength}px peak RGB=({bestR},{bestG},{bestB})\n" : "best: FAILED TO SAMPLE\n"),
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
                // MV-1081: BuildClusterRobot freezes its RobotEnemy to "hold the pose" — unfreeze it for
                // the real lure/intake simulation below.
                rusherGo.GetComponent<RobotEnemy>().SetCutsceneFrozen(false);

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

        // ---- Mv818CoverCheck (MV-818 AC4) -----------------------------------------------------

        /// <summary>MV-818's own AC4 evidence: World 2's real shipped a3, both 10 m cover strips
        /// (<c>a3_cover3</c>, the planter trough, and <c>a3_cover4</c>, the pump line — both authored
        /// at x=67, z=120/101 in <c>world2_config.json</c>) in one frame, so the fix reads against the
        /// design sheet's own row of cells rather than the single-hopper regression it replaces. Framed
        /// by hand at the fixed rig's own pitch, pulled back from the default 26.02 m so a 19 m gap
        /// between the two strips' centres both fit with margin — not the player's own spawn framing
        /// every earlier World 2 capture preset uses, since a3 is not where Max starts.</summary>
        private static CapturePreset BuildMv818CoverCheck()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

            // The two strips' own authored centres (world2_config.json: a3_cover3 x=67 z=120,
            // a3_cover4 x=67 z=101) — hand-picked rather than looked up by id, since this is a one-off
            // evidence shot, not a general-purpose "find area a3" capture other tickets would reuse.
            var stripMidpoint = new Vector3(67f, 0f, 110.5f);
            const float distance = 44f;

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + StormdrainDressing finish

                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;

                Vector3 focus = stripMidpoint + Vector3.up * 1f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
                for (int i = 0; i < 3; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv818cover",
                LogTag = "[MV818Capture]",
                Flag = "-mv818shot",
                ArmFile = "Temp/mv818.arm",
                HeadlessMarker = "Temp/mv818.headless",
                DoneFileName = "_mv818_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same WorldIndex seeding as BuildMv742StormdrainCheck/BuildMv750DressingCheck.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot> { new CaptureShot("MV-818", NoSetup) },
            };
        }

        // ---- Mv819PipeCheck (MV-819 AC4) ------------------------------------------------------

        /// <summary>MV-819's own AC4 evidence: World 2's real shipped area a3 ("Junction Hall,
        /// floor"), framed at the ACTUAL gameplay camera's own pitch/distance — unlike
        /// <see cref="BuildMv818CoverCheck"/>'s deliberately pulled-back design-review framing, this
        /// ticket's AC asks for "one capture ... at the play camera", so the retuned overhead mains,
        /// cross-main and junction boxes are proven to read at the angle players actually see, not a
        /// flattering angle chosen for the screenshot. Framed on a3's own centre (world2_config.json:
        /// origin x=54 z=96, size 36x30 -> centre 72,111) rather than Max's spawn, since a3 is not
        /// where he starts.</summary>
        private static CapturePreset BuildMv819PipeCheck()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";
            var areaCentre = new Vector3(72f, 0f, 111f);

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + StormdrainDressing finish

                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;
                float distance = rig != null ? rig.Distance : 26.02f;

                Vector3 focus = areaCentre + Vector3.up * 1f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
                for (int i = 0; i < 3; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv819pipecheck",
                LogTag = "[MV819Capture]",
                Flag = "-mv819shot",
                ArmFile = "Temp/mv819.arm",
                HeadlessMarker = "Temp/mv819.headless",
                DoneFileName = "_mv819_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same WorldIndex seeding as BuildMv742StormdrainCheck/BuildMv750DressingCheck.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot> { new CaptureShot("MV-819", NoSetup) },
            };
        }

        // ---- Mv821DeckWalkwayCheck (MV-821 AC4) -----------------------------------------------

        /// <summary>MV-821's own AC4 evidence: World 2's real shipped area a3 ("Junction Hall,
        /// floor"), same "at the play camera" framing <see cref="BuildMv819PipeCheck"/> uses (a1's own
        /// centre, actual gameplay pitch/distance) — the deck rails there used to read as a walled
        /// block; this proves the rebuilt open walkway (flat hazard edge, posts, beam, ground shadow)
        /// reads as walkable at the angle players actually see it.</summary>
        private static CapturePreset BuildMv821DeckWalkwayCheck()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";
            var areaCentre = new Vector3(72f, 0f, 111f);

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + StormdrainDressing finish

                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;
                float distance = rig != null ? rig.Distance : 26.02f;

                Vector3 focus = areaCentre + Vector3.up * 1f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
                for (int i = 0; i < 3; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv821deckwalkway",
                LogTag = "[MV821Capture]",
                Flag = "-mv821shot",
                ArmFile = "Temp/mv821.arm",
                HeadlessMarker = "Temp/mv821.headless",
                DoneFileName = "_mv821_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same WorldIndex seeding as BuildMv742StormdrainCheck/BuildMv750DressingCheck.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot> { new CaptureShot("MV-821", NoSetup) },
            };
        }

        // ---- Mv822HazardBandingCheck (MV-822 AC4) ---------------------------------------------

        /// <summary>MV-822's own AC4 evidence: World 2's real shipped area a3 ("Junction Hall,
        /// floor"), same "at the play camera" framing <see cref="BuildMv821DeckWalkwayCheck"/> uses
        /// (a3's own centre, actual gameplay pitch/distance) — proof the channel banding now reads
        /// past the MapRuntime slab this ticket switches off, and that a3's own gate jamb carries its
        /// hazard stripe whether or not it happens to be locked.</summary>
        private static CapturePreset BuildMv822HazardBandingCheck()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";
            var areaCentre = new Vector3(72f, 0f, 111f);

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + StormdrainDressing finish

                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;
                float distance = rig != null ? rig.Distance : 26.02f;

                Vector3 focus = areaCentre + Vector3.up * 1f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
                for (int i = 0; i < 3; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv822hazardbanding",
                LogTag = "[MV822Capture]",
                Flag = "-mv822shot",
                ArmFile = "Temp/mv822.arm",
                HeadlessMarker = "Temp/mv822.headless",
                DoneFileName = "_mv822_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same WorldIndex seeding as BuildMv819PipeCheck/BuildMv821DeckWalkwayCheck.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot> { new CaptureShot("MV-822", NoSetup) },
            };
        }

        // ---- Mv824LitGroundCheck (MV-824 AC4) --------------------------------------------------

        /// <summary>MV-824's own AC4 evidence: World 2's real shipped area a3 ("Junction Hall,
        /// floor"), same "at the play camera" framing <see cref="BuildMv822HazardBandingCheck"/> uses
        /// (a3's own centre, actual gameplay pitch/distance) — proof the raised
        /// <c>LitGroundBaseMultiplier</c> (0.38 -> 0.72) reads as a floor whose cast bays, joints and
        /// cracks are actually legible far from any fitting, not just brighter in the abstract.</summary>
        private static CapturePreset BuildMv824LitGroundCheck()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";
            var areaCentre = new Vector3(72f, 0f, 111f);

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + StormdrainDressing finish

                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;
                float distance = rig != null ? rig.Distance : 26.02f;

                Vector3 focus = areaCentre + Vector3.up * 1f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
                for (int i = 0; i < 3; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv824litground",
                LogTag = "[MV824Capture]",
                Flag = "-mv824shot",
                ArmFile = "Temp/mv824.arm",
                HeadlessMarker = "Temp/mv824.headless",
                DoneFileName = "_mv824_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same WorldIndex seeding as BuildMv819PipeCheck/BuildMv821DeckWalkwayCheck/BuildMv822HazardBandingCheck.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot> { new CaptureShot("MV-824", NoSetup) },
            };
        }

        // ---- Mv825LppeFire (MV-825 AC4) --------------------------------------------------------

        /// <summary>MV-825's own AC4 evidence: Max's rebuilt LPPE bolt (straight, along the travel
        /// axis, with its glow sheath and crackle) firing at a live target, framed at the ACTUAL
        /// gameplay camera's own pitch/distance -- same "at the play camera" idiom
        /// <see cref="BuildMv819PipeCheck"/> established -- rather than <see cref="BuildMv758LppeSalvo"/>'s
        /// own deliberately close, hand-picked design-review angle. Fires the REAL
        /// <see cref="PulseLaser"/> component <see cref="PlayerController"/> self-attaches to Max
        /// (MV-739) directly via <c>FireTick()</c>, not a standalone stand-in laser, so the bolt
        /// genuinely originates from Max's own rig and the shot is "Max firing", not a VFX-only
        /// isolate.</summary>
        private static CapturePreset BuildMv825LppeFire()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

            MethodInfo fireTickMethod =
                typeof(PulseLaser).GetMethod("FireTick", BindingFlags.NonPublic | BindingFlags.Instance);

            GameObject targetGo = null;

            IEnumerator Setup(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                var playerGo = GameObject.FindGameObjectWithTag("Player");
                if (playerGo == null) throw new CaptureAbortException("no Player GameObject in the loaded scene");

                PulseLaser laser = playerGo.GetComponent<PulseLaser>();
                if (laser == null) throw new CaptureAbortException("Max's own PulseLaser never self-attached");

                // Max's actual equipped primary drives which weapon MaxRig renders in his hand --
                // FireTick() itself bypasses that gate (same reflection idiom BuildMv758LppeSalvo
                // uses), but the shot must still show the LPPE, not whatever World 1 defaults to.
                WeaponSystemState.ActivePrimary = WeaponCatalog.PrimaryKind.Lppe;

                // Max's own real spawn position/facing, untouched. Two different teleport-to-open-room
                // attempts (a plain transform write, then the same write with his CharacterController
                // disabled for the move) both left the captured frame showing nothing but his own
                // floating nameplate over a giant close dark surface -- something about his starting
                // alcove's own collision setup doesn't tolerate a scripted relocation this way, and
                // chasing it further isn't worth this one evidence shot. His real spawn, at a close but
                // untouched-position combat distance, is the one configuration already proven to render
                // correctly.
                Vector3 focus = playerGo.transform.position;
                Vector3 aimDir = playerGo.transform.forward;
                aimDir.y = 0f;
                if (aimDir.sqrMagnitude < 0.01f) aimDir = Vector3.forward; else aimDir.Normalize();

                // Close enough that the pulse locks well inside PulseLaser.DefaultLockRange/
                // DefaultLockHalfAngle (the reticle bracket + homing both read as real combat, not a
                // straight miss).
                Vector3 targetPos = focus + aimDir * 3f;
                targetGo = BuildClusterRobot(EnemyKind.Bruiser, targetPos);
                Physics.SyncTransforms();

                // AC4: "at the play camera" -- the live FixedAngleCameraRig's own pitch, and a
                // moderate partial zoom on its own distance (a hand-picked short distance put the
                // camera through this starting alcove's own low roof -- see this method's own doc
                // comment) so Max's actual rig and the bolt's flight both read clearly.
                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;
                float distance = (rig != null ? rig.Distance : 24f) * 0.33f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                Vector3 camFocus = focus + aimDir * 0.6f + Vector3.up * 1.1f;
                cam.transform.SetPositionAndRotation(camFocus - rot * Vector3.forward * distance, rot);

                fireTickMethod.Invoke(laser, null);   // real FireTick() -> real SeekerPulse.Fire + Muzzle()

                if (laser.LastSpawnedPulseForTests == null)
                    throw new CaptureAbortException("FireTick did not spawn a SeekerPulse");

                for (int settle = 0; settle < 2; settle++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv825lppefire",
                LogTag = "[MV825Capture]",
                Flag = "-mv825shot",
                ArmFile = "Temp/mv825.arm",
                HeadlessMarker = "Temp/mv825.headless",
                DoneFileName = "_mv825_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same WorldIndex seeding as BuildMv819PipeCheck/BuildMv821DeckWalkwayCheck --
                    // the LPPE is World 2's primary (MV-708).
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Shots = new List<CaptureShot> { new CaptureShot("MV-825", Setup) },
                Cleanup = () =>
                {
                    if (targetGo != null) Destroy(targetGo);
                },
            };
        }

        // ---- MV857MaxWorld2Colors (MV-857 AC3) ------------------------------------------------

        /// <summary>Boots straight into World 2 (same <c>WorldIndex</c> seeding as
        /// <see cref="BuildMv738SludgeCheck"/>) and frames Max at the same close-up pitch/distance
        /// <c>MaxDetailDirector</c> (MV-453) already uses in World 1, facing the camera — so this
        /// shot and a World-1 <c>cc-max-detail.bat</c> shot are directly comparable, pixel region
        /// for pixel region, for the tunic/hair average-RGB evidence the ticket's fix comment
        /// quotes.</summary>
        private static CapturePreset BuildMv857MaxWorld2Colors()
        {
            const float pitch = 64.88f;
            const float distance = 15.81f;
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens\max-detail";

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let materials/map/MaxRig settle

                var maxGo = GameObject.FindGameObjectWithTag("Player");
                if (maxGo == null) throw new CaptureAbortException("no Player in the scene");
                var controller = maxGo.GetComponent<PlayerController>();
                var facingField = typeof(PlayerController).GetField("_facing",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (controller == null || facingField == null)
                    throw new CaptureAbortException("PlayerController._facing not found");

                facingField.SetValue(controller, Vector3.forward);
                maxGo.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

                for (int i = 0; i < 6; i++) yield return null;   // let the rig's LateUpdate settle on the facing

                Vector3 focus = maxGo.transform.position + Vector3.up * 0.9f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);

                yield return null;
                yield return null;
            }

            return new CapturePreset
            {
                Key = "mv857maxworld2",
                LogTag = "[MV857Capture]",
                Flag = "-mv857shot",
                ArmFile = "Temp/mv857.arm",
                HeadlessMarker = "Temp/mv857.headless",
                DoneFileName = "_mv857_done.txt",
                Width = 1920,
                Height = 1080,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same WorldIndex seeding as BuildMv738SludgeCheck -- lands Max in World 2 on
                    // the one boot a capture preset gets, rather than a second scene load.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot> { new CaptureShot("MV-857-max-world2", NoSetup) },
            };
        }

        // ---- MV939W2ColorCheck (MV-939 AC1) ---------------------------------------------------

        /// <summary>MV-939's own before/after evidence: World 2's real shipped config, framed on the
        /// zone centre of area10 then area13 — the same two "spots" Lee's TestFlight report named —
        /// via <see cref="MapStaticBatchRoot.ApplyAreaGate"/> called directly the same way
        /// MV934CombinedMeshTests/MV939CombinedMeshKeepsOwnMaterialTests already do in EditMode, so the
        /// zone's own combined mesh is actually enabled for the shot rather than gated off. Run once
        /// against the pre-fix commit and once after (see the fix comment for both filenames) — this is
        /// a local Windows-Editor Play capture, not a live WebGL view (CC_AUTONOMY.md / MV-934's own
        /// fix comment: the worker has no path to a deployed WebGL build from this session), but it is
        /// the same rendering pipeline and is what MV-857 already used for an equivalent colour-regression
        /// AC.</summary>
        private static CapturePreset BuildMv939W2ColorCheck()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

            Vector3? ZoneCenter(string zoneId)
            {
                var path = FindFirstObjectByType<BackyardPath>();
                if (path == null || path.Map == null || path.Map.zones == null) return null;
                foreach (MapZone z in path.Map.zones)
                    if (z != null && z.id == zoneId) return z.Center;
                return null;
            }

            IEnumerator FrameZone(Camera cam, string zoneId)
            {
                Vector3? center = ZoneCenter(zoneId);
                if (center == null) throw new CaptureAbortException($"World 2 map carries no zone '{zoneId}'");

                var batchRoot = FindFirstObjectByType<MapStaticBatchRoot>();
                if (batchRoot == null) throw new CaptureAbortException("World 2 built no MapStaticBatchRoot");
                batchRoot.ApplyAreaGate(zoneId);   // enable that zone's own combined mesh for the shot

                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;
                float distance = rig != null ? rig.Distance : 20f;

                Vector3 focus = center.Value + Vector3.up * 1f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
                for (int i = 0; i < 3; i++) yield return null;   // let the gate + renderer swap settle
            }

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + MapStaticBatchRoot.Start finish
                yield return FrameZone(cam, "area10");
            }

            IEnumerator SetupArea13(Camera cam) => FrameZone(cam, "area13");

            return new CapturePreset
            {
                Key = "mv939w2colors",
                LogTag = "[MV939Capture]",
                Flag = "-mv939shot",
                ArmFile = "Temp/mv939.arm",
                HeadlessMarker = "Temp/mv939.headless",
                DoneFileName = "_mv939_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same WorldIndex seeding as BuildMv742StormdrainCheck/BuildMv750DressingCheck.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot>
                {
                    new CaptureShot("MV-939-a10", NoSetup),
                    new CaptureShot("MV-939-a13", SetupArea13),
                },
            };
        }

        // ---- MV913MissileLauncherCheck (MV-913 AC4) -------------------------------------------

        /// <summary>The ticket's own one-shot capture check: a shed body with four
        /// <see cref="ShedFittingKind.Missile"/> fittings mounted on its roof corners — the exact
        /// <c>MapRuntime.BuildShedFittings</c> corner layout, reproduced directly here rather than
        /// loading a whole <c>WorldConfig</c> through <c>WorldMapLoader</c> for one throwaway shed, the
        /// same "build the one prop this shot needs, standalone" idiom <see cref="BuildMv693Replicator"/>
        /// already uses for a Replicator. <c>ShedFitting.Bind</c> is called with a null <c>MowerHutch</c>
        /// (same as <c>MV547ShedFittingTests</c>'s own EditMode fixture) — this is a cosmetic capture,
        /// not a combat scenario, so the fitting needs no living shed to poll.</summary>
        private static CapturePreset BuildMv913MissileLauncherCheck()
        {
            const float pitch = 60f;
            const float distance = 8.5f;   // MV-913 iteration 1: 6m clipped the far two corner fittings out of frame
            const float fittingSize = 0.5f;
            const float fittingInset = 0.85f;
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images";

            GameObject shedGo = null;
            var fittings = new List<GameObject>();

            IEnumerator Setup(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                Vector3 focus = CaptureDirector.OpenZoneCenter() ?? Vector3.zero;
                Vector3 shedSize = new Vector3(3f, 2.4f, 3f);

                shedGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shedGo.name = "MV913CaptureShed";
                shedGo.transform.position = focus + Vector3.up * (shedSize.y * 0.5f);
                shedGo.transform.localScale = shedSize;

                Vector2[] cornerSigns = { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
                float halfW = shedSize.x * 0.5f * fittingInset;
                float halfD = shedSize.z * 0.5f * fittingInset;
                float roofY = shedGo.transform.position.y + shedSize.y * 0.5f + fittingSize * 0.5f;

                for (int i = 0; i < cornerSigns.Length; i++)
                {
                    var go = new GameObject($"MV913CaptureFitting{i + 1}");
                    go.transform.position = new Vector3(shedGo.transform.position.x + cornerSigns[i].x * halfW,
                        roofY, shedGo.transform.position.z + cornerSigns[i].y * halfD);
                    go.transform.SetParent(shedGo.transform, worldPositionStays: true);

                    var fitting = go.AddComponent<ShedFitting>();
                    fitting.Bind(null, ShedFittingKind.Missile);
                    fittings.Add(go);
                }

                for (int i = 0; i < 3; i++) yield return null;   // let the rigs settle and start facing Max

                var rot = Quaternion.Euler(pitch, 0f, 0f);
                Vector3 camFocus = shedGo.transform.position;
                cam.transform.SetPositionAndRotation(camFocus - rot * Vector3.forward * distance, rot);

                yield return null;
            }

            return new CapturePreset
            {
                Key = "mv913missilelauncher",
                LogTag = "[MV913Capture]",
                Flag = "-mv913shot",
                ArmFile = "Temp/mv913.arm",
                HeadlessMarker = "Temp/mv913.headless",
                DoneFileName = "_mv913_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                Shots = new List<CaptureShot> { new CaptureShot("MV-913", Setup) },
                Cleanup = () =>
                {
                    foreach (var go in fittings) if (go != null) Destroy(go);
                    if (shedGo != null) Destroy(shedGo);
                },
            };
        }

        // ---- Mv965Corridor (MV-965 AC3) --------------------------------------------------------

        /// <summary>Frames the World 1 -> World 2 join corridor's own three segments (the garden, the
        /// kerb + grate, and the culvert) at d = 4, 11 and 22 m from the door — MV-965's own dressing
        /// pass, shot on the real corridor MV-964 built rather than a mock-up. Drives
        /// <see cref="WorldJoinSequence.OpenExitDoor"/> directly (the same call
        /// <see cref="MaxWorlds.VFX.WorldFinaleGate"/> makes once World 1's boss dies) since this is a
        /// design-fidelity check, not a playthrough — the boss fight itself is out of scope for three
        /// still frames. The three shots are composited into one file (MV-965's own AC3) in
        /// <c>Cleanup</c>, after the director has already written each one individually.</summary>
        private static CapturePreset BuildMv965Corridor()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";
            float[] shotD = { 4f, 11f, 22f };

            Vector2 doorMouth = default;
            Wall wall = Wall.N;

            Vector3 OutwardDir(Wall w) => w switch
            {
                Wall.N => Vector3.forward,
                Wall.S => Vector3.back,
                Wall.E => Vector3.right,
                Wall.W => Vector3.left,
                _ => Vector3.forward,
            };

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + WorldMaterials finish

                var path = FindFirstObjectByType<BackyardPath>();
                if (path == null || path.Cfg == null || path.Map == null)
                    throw new CaptureAbortException("World 1 built no BackyardPath to open the corridor from");

                WorldTransitionEntry entry = WorldTransitions.For(0);
                if (entry == null) throw new CaptureAbortException("World 1 has no WorldTransitions entry into World 2");

                WorldJoinSequence.OpenExitDoor(path.Cfg, path.Map, entry, fromWorldIndex: 0, path.ExitGate);
                for (int i = 0; i < 4; i++) yield return null;

                if (FindFirstObjectByType<WorldJoinSequence>() == null)
                    throw new CaptureAbortException("WorldJoinSequence failed to build the exit corridor");

                doorMouth = entry.ExitDoorMouth(path.Cfg);
                wall = entry.ExitWall;
            }

            IEnumerator FrameAt(Camera cam, float d)
            {
                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;
                float distance = rig != null ? rig.Distance : 20f;

                Vector3 pos = new Vector3(doorMouth.x, 1f, doorMouth.y) + OutwardDir(wall) * d;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(pos - rot * Vector3.forward * distance, rot);
                for (int i = 0; i < 2; i++) yield return null;
            }

            Texture2D LoadPng(string path)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                tex.LoadImage(File.ReadAllBytes(path));
                return tex;
            }

            void Composite()
            {
                try
                {
                    string p0 = Path.Combine(outDir, "MV-965-corridor-d4.png");
                    string p1 = Path.Combine(outDir, "MV-965-corridor-d11.png");
                    string p2 = Path.Combine(outDir, "MV-965-corridor-d22.png");
                    if (!File.Exists(p0) || !File.Exists(p1) || !File.Exists(p2)) return;

                    Texture2D t0 = LoadPng(p0), t1 = LoadPng(p1), t2 = LoadPng(p2);
                    int w = t0.width, h = t0.height;
                    var combined = new Texture2D(w * 3, h, TextureFormat.RGB24, false);
                    combined.SetPixels(0, 0, w, h, t0.GetPixels());
                    combined.SetPixels(w, 0, w, h, t1.GetPixels());
                    combined.SetPixels(w * 2, 0, w, h, t2.GetPixels());
                    combined.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, "MV-965-corridor.png"), combined.EncodeToPNG());
                    DestroyImmediate(t0); DestroyImmediate(t1); DestroyImmediate(t2); DestroyImmediate(combined);
                }
                catch (Exception e) { Debug.LogWarning("[MV965Capture] composite failed: " + e.Message); }
            }

            return new CapturePreset
            {
                Key = "mv965corridor",
                LogTag = "[MV965Capture]",
                Flag = "-mv965shot",
                ArmFile = "Temp/mv965.arm",
                HeadlessMarker = "Temp/mv965.headless",
                DoneFileName = "_mv965_done.txt",
                Width = 1200,
                Height = 800,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 0;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot>
                {
                    new CaptureShot("MV-965-corridor-d4", cam => FrameAt(cam, shotD[0])),
                    new CaptureShot("MV-965-corridor-d11", cam => FrameAt(cam, shotD[1])),
                    new CaptureShot("MV-965-corridor-d22", cam => FrameAt(cam, shotD[2])),
                },
                Cleanup = Composite,
            };
        }

        // ---- Mv967Corridor (MV-967 AC3) --------------------------------------------------------

        /// <summary>Frames the World 2 -> World 3 join corridor's own three segments (the outfall, the
        /// breach, and the hull) at d = 5, 13 and 22 m from the door — MV-967's own dressing pass, shot
        /// on the real corridor MV-964 built. Same idiom as <see cref="BuildMv965Corridor"/>: drives
        /// <see cref="WorldJoinSequence.OpenExitDoor"/> directly rather than playing through World 2's
        /// own boss fight, and composites the three shots into one file (MV-967's own AC3) in
        /// <c>Cleanup</c>.</summary>
        private static CapturePreset BuildMv967Corridor()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";
            float[] shotD = { 5f, 13f, 22f };

            Vector2 doorMouth = default;
            Wall wall = Wall.E;

            Vector3 OutwardDir(Wall w) => w switch
            {
                Wall.N => Vector3.forward,
                Wall.S => Vector3.back,
                Wall.E => Vector3.right,
                Wall.W => Vector3.left,
                _ => Vector3.forward,
            };

            IEnumerator Prepare(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + WorldMaterials finish

                var path = FindFirstObjectByType<BackyardPath>();
                if (path == null || path.Cfg == null || path.Map == null)
                    throw new CaptureAbortException("World 2 built no BackyardPath to open the corridor from");

                WorldTransitionEntry entry = WorldTransitions.For(1);
                if (entry == null) throw new CaptureAbortException("World 2 has no WorldTransitions entry into World 3");

                WorldJoinSequence.OpenExitDoor(path.Cfg, path.Map, entry, fromWorldIndex: 1, path.ExitGate);
                for (int i = 0; i < 4; i++) yield return null;

                if (FindFirstObjectByType<WorldJoinSequence>() == null)
                    throw new CaptureAbortException("WorldJoinSequence failed to build the exit corridor");

                doorMouth = entry.ExitDoorMouth(path.Cfg);
                wall = entry.ExitWall;
            }

            IEnumerator FrameAt(Camera cam, float d)
            {
                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;
                float distance = rig != null ? rig.Distance : 20f;

                Vector3 pos = new Vector3(doorMouth.x, 1f, doorMouth.y) + OutwardDir(wall) * d;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                cam.transform.SetPositionAndRotation(pos - rot * Vector3.forward * distance, rot);
                for (int i = 0; i < 2; i++) yield return null;
            }

            Texture2D LoadPng(string path)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                tex.LoadImage(File.ReadAllBytes(path));
                return tex;
            }

            void Composite()
            {
                try
                {
                    string p0 = Path.Combine(outDir, "MV-967-corridor-d5.png");
                    string p1 = Path.Combine(outDir, "MV-967-corridor-d13.png");
                    string p2 = Path.Combine(outDir, "MV-967-corridor-d22.png");
                    if (!File.Exists(p0) || !File.Exists(p1) || !File.Exists(p2)) return;

                    Texture2D t0 = LoadPng(p0), t1 = LoadPng(p1), t2 = LoadPng(p2);
                    int w = t0.width, h = t0.height;
                    var combined = new Texture2D(w * 3, h, TextureFormat.RGB24, false);
                    combined.SetPixels(0, 0, w, h, t0.GetPixels());
                    combined.SetPixels(w, 0, w, h, t1.GetPixels());
                    combined.SetPixels(w * 2, 0, w, h, t2.GetPixels());
                    combined.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, "MV-967-corridor.png"), combined.EncodeToPNG());
                    DestroyImmediate(t0); DestroyImmediate(t1); DestroyImmediate(t2); DestroyImmediate(combined);
                }
                catch (Exception e) { Debug.LogWarning("[MV967Capture] composite failed: " + e.Message); }
            }

            return new CapturePreset
            {
                Key = "mv967corridor",
                LogTag = "[MV967Capture]",
                Flag = "-mv967shot",
                ArmFile = "Temp/mv967.arm",
                HeadlessMarker = "Temp/mv967.headless",
                DoneFileName = "_mv967_done.txt",
                Width = 1200,
                Height = 800,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot>
                {
                    new CaptureShot("MV-967-corridor-d5", cam => FrameAt(cam, shotD[0])),
                    new CaptureShot("MV-967-corridor-d13", cam => FrameAt(cam, shotD[1])),
                    new CaptureShot("MV-967-corridor-d22", cam => FrameAt(cam, shotD[2])),
                },
                Cleanup = Composite,
            };
        }

        // ---- Mv993Crossing (MV-993) ------------------------------------------------------------

        /// <summary>MV-993: reproduces Lee's World 1 a20(Glasshouse)->a21(Seed Store) hard crash
        /// headlessly, run against the WINDOWS STANDALONE build (not the Editor Play Mode every other
        /// preset here uses via CaptureEntryPoint) — the ticket specifically wants release-shaped
        /// memory behaviour, and this whole preset system already arms off either a command-line flag
        /// or the arm file (<see cref="CaptureDirector.Install"/>), neither of which cares which player
        /// is running.
        ///
        /// Jumps straight to area 20 with Big Bermuda alive (a fresh session has never fought it —
        /// nothing here fights or kills it), force-opens the g20 boss gate (Lee had already broken it
        /// before the crash), then teleports Max back and forth across it <see cref="Mv993Crossings"/>
        /// times in <see cref="Mv993StepsPerCrossing"/> steps each way so every Update-driven system
        /// (the gate's own self-heal, <see cref="MaxWorlds.Enemies.AreaAccumulationDirector"/>'s
        /// position-crossing fallback, <see cref="FallSafetyNet"/>) sees Max's real position tick by
        /// tick rather than one instantaneous jump. Per crossing: reservedMB (this ticket's own
        /// PerfTelemetry/PerfSessionRecorder addition), the delta in <see cref="FallEventLog"/>'s count
        /// (MV-955), and any <c>LogType.Exception</c> logged during that crossing, all folded into the
        /// done-marker's <see cref="CapturePreset.ExtraReport"/> as one CSV-shaped table.</summary>
        private const int Mv993Crossings = 20;
        private const float Mv993InsideMetres = 3f;
        private const int Mv993StepsPerCrossing = 10;

        private static CapturePreset BuildMv993Crossing()
        {
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

            var table = new System.Text.StringBuilder();
            int exceptionCount = 0;

            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Exception) exceptionCount++;
            }

            IEnumerator Prepare(Camera cam)
            {
                DevMode.Enabled = true;
                DevMode.Invincible = true;
                exceptionCount = 0;
                Application.logMessageReceived += OnLog;

                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath/world build finish

                if (!DevModeController.TryJumpToArea("20"))
                    throw new CaptureAbortException("could not jump to area 20 (The Glasshouse)");

                for (int i = 0; i < 4; i++) yield return null;   // let the gate self-heal / population settle

                var gateGo = GameObject.Find("g20");
                var gate = gateGo != null ? gateGo.GetComponent<AreaGate>() : null;
                if (gate == null)
                    throw new CaptureAbortException("no 'g20' AreaGate found -- the a20->a21 boss gate is missing");

                gate.ForceOpen();
                for (int i = 0; i < 3; i++) yield return null;   // let the hinge swing / threshold drop settle

                Vector3 dir = gate.AwayFromPlayerDirection.sqrMagnitude > 0.01f
                    ? gate.AwayFromPlayerDirection.normalized : Vector3.right;
                Vector3 mouth = gate.transform.position;

                var playerGo = GameObject.FindGameObjectWithTag("Player");
                if (playerGo == null) throw new CaptureAbortException("no Player-tagged Max in the scene");
                Transform player = playerGo.transform;
                var cc = player.GetComponent<CharacterController>();
                float y = player.position.y;

                Vector3 insideA20 = mouth - dir * Mv993InsideMetres; insideA20.y = y;
                Vector3 insideA21 = mouth + dir * Mv993InsideMetres; insideA21.y = y;

                // Same disable/set/enable shape every other direct position write in this project uses
                // (DevModeController.TryJumpToArea, PlayerController.Recover) -- a live CharacterController
                // caches its own position and would otherwise undo a plain transform.position set.
                void Teleport(Vector3 pos)
                {
                    bool wasEnabled = cc != null && cc.enabled;
                    if (cc != null) cc.enabled = false;
                    player.position = pos;
                    if (cc != null) cc.enabled = wasEnabled;
                }

                Teleport(insideA20);
                for (int i = 0; i < 4; i++) yield return null;

                // MV-993 fix (caught by the verifier subagent, not by eye): exceptions logged during
                // setup -- world build, TryJumpToArea, ForceOpen's own FillArea(21) population burst --
                // land in exceptionCount before the very first "exBefore = exceptionCount" snapshot
                // below, so without this line they're silently absorbed into the baseline and never
                // appear in ANY row, understating the run's real exception count (measured: 53 raw
                // ArgumentNullExceptions logged across the whole capture, only 2 landing inside a
                // crossing window -- the other 51 were exactly this setup-phase gap).
                int setupExceptions = exceptionCount;
                table.Append($"setup,-,-,{setupExceptions}\n");
                table.Append("crossing,reservedMB,fallEvents,exceptions\n");

                for (int crossing = 1; crossing <= Mv993Crossings; crossing++)
                {
                    int fallBefore = FallEventLog.Events.Count;
                    int exBefore = exceptionCount;

                    for (int s = 1; s <= Mv993StepsPerCrossing; s++)
                    {
                        Teleport(Vector3.Lerp(insideA20, insideA21, s / (float)Mv993StepsPerCrossing));
                        yield return null;
                    }
                    for (int settle = 0; settle < 3; settle++) yield return null;   // settle inside a21

                    // Back a21 -> a20. EnterArea's forward-only advance (see its own doc comment) means
                    // this leg never re-fires the area-entry telemetry row -- only the very first forward
                    // crossing into a21 does, which is deliberate, not a gap in this loop.
                    for (int s = 1; s <= Mv993StepsPerCrossing; s++)
                    {
                        Teleport(Vector3.Lerp(insideA21, insideA20, s / (float)Mv993StepsPerCrossing));
                        yield return null;
                    }
                    for (int settle = 0; settle < 3; settle++) yield return null;   // settle inside a20

                    double reservedMB = Profiler.GetTotalReservedMemoryLong() / 1048576.0;
                    int fallEvents = FallEventLog.Events.Count - fallBefore;
                    int exceptions = exceptionCount - exBefore;
                    table.Append($"{crossing},{reservedMB:F1},{fallEvents},{exceptions}\n");
                }

                table.Append($"total exceptions this run: {exceptionCount} (setup: {setupExceptions})\n");

                var rig = FindFirstObjectByType<FixedAngleCameraRig>();
                float pitch = rig != null ? rig.Pitch : 60f;
                float distance = rig != null ? rig.Distance : 12f;
                var rot = Quaternion.Euler(pitch, 0f, 0f);
                Vector3 camFocus = mouth + Vector3.up * 1f;
                cam.transform.SetPositionAndRotation(camFocus - rot * Vector3.forward * distance, rot);
                yield return null;
            }

            return new CapturePreset
            {
                Key = "mv993crossing",
                LogTag = "[MV993Capture]",
                Flag = "-mv993shot",
                ArmFile = "Temp/mv993.arm",
                HeadlessMarker = "Temp/mv993.headless",
                DoneFileName = "_mv993_done.txt",
                Width = 1200,
                Height = 800,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 180,
                BeforeSceneLoad = () =>
                {
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 0;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Prepare = Prepare,
                Shots = new List<CaptureShot> { new CaptureShot("MV-993-crossing-gate", NoSetup) },
                Cleanup = () => Application.logMessageReceived -= OnLog,
                ExtraReport = () => table.ToString(),
            };
        }

        // ---- MV1024SentinelColorCheck (MV-1024 AC3) -------------------------------------------

        /// <summary>MV-1024's own capture evidence: one deployed World 2 Sentinel beside two robots,
        /// then a mean-RGB readback of every red-dominant ("body") pixel in a box around the sentinel's
        /// own projected screen position — sampled off the exact <see cref="Texture2D"/>
        /// <see cref="CaptureDirector.Capture"/> encodes to the saved PNG (via <see cref="CaptureShot.Measure"/>),
        /// not a second render. A Tier-3 rendered-pixel measurement (CLAUDE.md's testing policy)
        /// reported through <see cref="CapturePreset.ExtraReport"/> rather than an EditMode test
        /// asserting the authored colour constant, which Tier 1 bans.</summary>
        private static CapturePreset BuildMv1024SentinelColorCheck()
        {
            const float pitch = 60f;
            const float distance = 9f;
            const int sampleBoxHalf = 40; // wide enough to cover the whole body silhouette (dome + skirt) at this framing
            const string outDir = @"C:\Dev\MaxVsTheWorlds-Images\_screens";

            GameObject sentinelGo = null, robotA = null, robotB = null;
            Vector3 sampleWorldPos = Vector3.zero;
            float meanR = -1f, meanG = -1f, meanB = -1f;
            int sampledPixels = 0;

            IEnumerator Setup(Camera cam)
            {
                for (int i = 0; i < 6; i++) yield return null;   // let BackyardPath.Awake + self-installing systems settle

                Vector3 focus = CaptureDirector.OpenZoneCenter() ?? Vector3.zero;
                Vector3 sentinelPos = focus + new Vector3(-1.2f, 0f, 0f);

                sentinelGo = new GameObject("MV1024CaptureSentinel");
                var sentinel = sentinelGo.AddComponent<Sentinel>();
                sentinel.Init(sentinelPos, maxHp: 60f, range: 8f, fireInterval: 0.5f,
                    moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);

                robotA = BuildClusterRobot(EnemyKind.Rusher, focus + new Vector3(1.0f, 0f, -0.6f));
                robotB = BuildClusterRobot(EnemyKind.Gunner, focus + new Vector3(1.2f, 0f, 0.7f));
                Physics.SyncTransforms();

                for (int i = 0; i < 3; i++) yield return null;   // let the rigs/materials build

                // The body's own vertical centre — half the capsule height Sentinel.BuildBody's own
                // ColliderHeight sets — so the sample lands on the torso, not the legs or dome head.
                sampleWorldPos = sentinelGo.transform.position + Vector3.up * 0.6f;

                var rot = Quaternion.Euler(pitch, 0f, 0f);
                Vector3 camFocus = focus; camFocus.y = 1f;
                cam.transform.SetPositionAndRotation(camFocus - rot * Vector3.forward * distance, rot);

                yield return null;
                yield return null;
            }

            void Measure(Texture2D tex)
            {
                // Camera.main is exactly where Setup left it — Capture() never repositions it between
                // Setup returning and this readback, so this viewport point matches the saved PNG.
                var cam = Camera.main;
                if (cam == null) return;
                Vector3 vp = cam.WorldToViewportPoint(sampleWorldPos);
                int cx = Mathf.RoundToInt(vp.x * tex.width);
                int cy = Mathf.RoundToInt(vp.y * tex.height);

                // Only pixels that are actually BODY (red-dominant) count — the box above is generous
                // enough to also catch the stylised shader's own black silhouette outline, AO-darkened
                // skirt shadow and background floor, none of which are "the sentinel body's pixels" the
                // AC asks for. Red-dominant excludes all three: the outline is r=g=b=0, the Stormdrain
                // floor is a desaturated grey-blue, and the eye/robots beside it are gold/blue/green.
                double sumR = 0, sumG = 0, sumB = 0;
                int count = 0;
                for (int dy = -sampleBoxHalf; dy <= sampleBoxHalf; dy++)
                {
                    int y = cy + dy;
                    if (y < 0 || y >= tex.height) continue;
                    for (int dx = -sampleBoxHalf; dx <= sampleBoxHalf; dx++)
                    {
                        int x = cx + dx;
                        if (x < 0 || x >= tex.width) continue;
                        Color32 c = tex.GetPixel(x, y);
                        if (c.r <= c.g * 1.3f || c.r <= c.b * 1.3f) continue; // not red-dominant -> not body
                        sumR += c.r; sumG += c.g; sumB += c.b;
                        count++;
                    }
                }
                if (count == 0) return;
                meanR = (float)(sumR / count);
                meanG = (float)(sumG / count);
                meanB = (float)(sumB / count);
                sampledPixels = count;
            }

            return new CapturePreset
            {
                Key = "mv1024sentinelcolor",
                LogTag = "[MV1024Capture]",
                Flag = "-mv1024shot",
                ArmFile = "Temp/mv1024.arm",
                HeadlessMarker = "Temp/mv1024.headless",
                DoneFileName = "_mv1024_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same World 2 seeding as BuildMv857MaxWorld2Colors/BuildMv939W2ColorCheck.
                    SaveSlotData data = SaveSystem.Load(0);
                    data.WorldIndex = 1;
                    SaveSystem.Save(0, data);
                    SaveSystem.ActiveSlot = 0;
                },
                Shots = new List<CaptureShot> { new CaptureShot("MV-1024-sentinel", Setup, measure: Measure) },
                Cleanup = () =>
                {
                    if (sentinelGo != null) Destroy(sentinelGo);
                    if (robotA != null) Destroy(robotA);
                    if (robotB != null) Destroy(robotB);
                },
                ExtraReport = () => sampledPixels > 0
                    ? $"sentinel body mean RGB (n={sampledPixels}px): R={meanR:F1} G={meanG:F1} B={meanB:F1}\n"
                    : "sentinel body mean RGB: FAILED TO SAMPLE (viewport projection landed outside frame)\n",
            };
        }

        // ---- MV1018Anchorhead (MV-1018 AC3) ---------------------------------------------------

        /// <summary>Builds a standalone Anchorhead the same way <c>MapRuntime.BuildBoss</c> does for
        /// its own "anchorhead" id (<see cref="BigBermudaBoss"/> + <see cref="AnchorheadBoss"/>,
        /// <see cref="AnchorheadRig"/> for the body) and frames it once it has actually woken --
        /// asleep, its eyes are dark and the design's own "amber eyes" never shows. Same standalone-
        /// boss shape as <see cref="BuildMv699Sludgequeen"/>'s own capture.</summary>
        private static CapturePreset BuildMv1018Anchorhead()
        {
            const float pitch = 60f;
            const float distance = 10f;

            // Primary: the committed CI/verify screenshots folder (this ticket's own AC3), same
            // docs/press/<slug> convention MissileTrail/TeleportCrackle already use. Mirrored to Lee's
            // design-review folder too, same dual-write those two presets' own MirrorPaths use.
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press", "mv1018-anchorhead"));
            const string mirrorPath = @"C:\Dev\MaxVsTheWorlds-Images\MV-1018-anchorhead.png";

            GameObject bossGo = null;
            AnchorheadRig rig = null;

            IEnumerator Setup(Camera cam)
            {
                for (int i = 0; i < 4; i++) yield return null;   // let the self-installing systems dress the world first

                Vector3 focus = CaptureDirector.OpenZoneCenter() ?? Vector3.zero;

                bossGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bossGo.name = "MV1018CaptureAnchorhead";
                bossGo.transform.position = focus;
                // The camera below approaches from -Z looking toward +Z -- face the rig's own front
                // (the flukes/eyes) back at it, same reasoning BuildMv699Sludgequeen's own 180-degree
                // spin uses for its own front-facing CuratorEye.
                bossGo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                var boss = bossGo.AddComponent<BigBermudaBoss>();
                var anchor = bossGo.AddComponent<AnchorheadBoss>();
                rig = AnchorheadRig.CreateFor(boss, anchor);

                float wokeAt = Time.unscaledTime;
                typeof(BigBermudaBoss).GetMethod("Wake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(boss, null);
                // Past the boss's own 1.6s intro AND the rig's 0.9s wake-stutter -- a steady, settled
                // amber rather than whatever the Perlin-noise flicker lands on mid-stutter.
                while (Time.unscaledTime - wokeAt < 1.8f) yield return null;

                var rot = Quaternion.Euler(pitch, 0f, 0f);
                Vector3 camFocus = focus + Vector3.up * 1.8f;
                cam.transform.SetPositionAndRotation(camFocus - rot * Vector3.forward * distance, rot);

                // Same first-manual-Render() warm-up MV693Replicator/MV699Sludgequeen's own presets
                // need -- URP's Render Graph has logged a one-off NullReferenceException on exactly
                // that first call in headless -nographics runs.
                cam.Render();
                for (int i = 0; i < 3; i++) yield return null;
            }

            return new CapturePreset
            {
                Key = "mv1018anchorhead",
                LogTag = "[MV1018Capture]",
                Flag = "-mv1018shot",
                ArmFile = "Temp/mv1018.arm",
                HeadlessMarker = "Temp/mv1018.headless",
                DoneFileName = "_mv1018_done.txt",
                Width = 1600,
                Height = 1000,
                OutputDirs = new[] { outDir },
                TimeoutSeconds = 90,
                BeforeSceneLoad = () =>
                {
                    // Same clean-profile guard MV616SentinelBeam/MV674TeleportCrackle/MV693Replicator/
                    // MV699Sludgequeen's own presets use: on a fresh profile (no slot picked yet)
                    // HomeScreen's pick-a-slot modal freezes Time.timeScale at 0 and blocks the wake
                    // wait above indefinitely.
                    SaveSystem.ActiveSlot = 0;
                },
                Shots = new List<CaptureShot> { new CaptureShot("MV-1018-anchorhead", Setup, new[] { mirrorPath }) },
                Cleanup = () =>
                {
                    if (rig != null) Destroy(rig.gameObject);
                    if (bossGo != null) Destroy(bossGo);
                },
            };
        }
    }
}
