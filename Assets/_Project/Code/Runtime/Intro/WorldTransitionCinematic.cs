using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Intro
{
    /// <summary>The World 1 -> World 2 transition's own small palette (MV-704) — kept local to this
    /// file rather than folded into <see cref="IntroPalette"/>, since it belongs to a different
    /// cinematic's story (the Stormdrain descent, not the opening invasion) and this ticket has no
    /// reason to touch that shared, already-tuned file.</summary>
    internal static class StormdrainTransitionPalette
    {
        public static readonly Color Concrete = new Color(0.32f, 0.33f, 0.34f);
        public static readonly Color WetConcrete = new Color(0.20f, 0.24f, 0.22f);
        public static readonly Color Lawn = new Color(0.30f, 0.45f, 0.22f);
        public static readonly Color WreckHull = new Color(0.30f, 0.30f, 0.33f);
        public static readonly Color Pipe = new Color(0.38f, 0.22f, 0.14f);
        public static readonly Color PortalViolet = new Color(0.55f, 0.22f, 0.95f);
        public static readonly Color PortalDim = new Color(0.16f, 0.08f, 0.22f);
        public static readonly Color DaylightDisc = new Color(1f, 0.97f, 0.86f);
    }

    /// <summary>
    /// MV-704: the World 1 -> World 2 transition. Max collects the Weapon Core, the last Big Bermuda's
    /// wreck settles onto the lawn's storm-water grate, it gives way under violet portal light, and he
    /// drops through — the camera follows him down the shaft, the daylight shrinking above — to land on
    /// the Outfall Steps of the Stormdrain. Under 20 s, no dialogue, skippable.
    ///
    /// Reuses <see cref="IntroCinematic"/>'s beat-timeline idiom (YT-155) through the shared
    /// <see cref="BeatSequencer"/> rather than a second hand-rolled "which beat" loop, and the same
    /// build-a-set-out-of-primitives toolkit (<see cref="IntroBuild"/>) the opening cinematic's acts
    /// use. It is deliberately a much smaller harness than <see cref="IntroCinematic"/>: no video path,
    /// no cross-fade pre-warm (MV-719) — this ticket's AC only needs a clean, skippable hand-off, not
    /// the opening cinematic's full reveal choreography.
    ///
    /// TRIGGER — <see cref="TryPlay"/>, called by <c>RunFlow.StartNextWorld</c> only when the active
    /// save's <c>WorldIndex</c> shows it is leaving World 1 for World 2; every other world transition
    /// plays no cinematic yet.
    /// SKIP — any input jumps straight to the LAND beat at 0 s (not straight to the end), so the title
    /// card and landing beat still read even on a fast skip.
    /// HANDOFF — <see cref="OnFinished"/> style callback (passed into <see cref="TryPlay"/>) fires
    /// exactly once, whether by the timeline running out or a skip playing LAND out, and hands control
    /// back to the caller — production wiring reloads the scene into World 2, which lands Max at the
    /// entry stub (<see cref="MaxWorlds.Arena.WorldMapLoader"/> synthesises the "spawn" entity at the
    /// entry-role area's centre for every world, world2_config's included).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldTransitionCinematic : MonoBehaviour
    {
        public const string Wreck = "wreck";
        public const string Drop = "drop";
        public const string Shaft = "shaft";
        public const string Land = "land";
        private const int LandBeatIndex = 3;

        private static System.Action s_pendingOnFinished;

        /// <summary>Start the transition once, only if there is a <c>Camera.main</c> to take over and
        /// hand back to (mirrors <see cref="IntroCinematic.TryPlay"/>'s same bail — tests/captures with
        /// no camera fall straight through to the caller's own fallback). Returns true if it started;
        /// on true, the cinematic owns calling <paramref name="onFinished"/> exactly once, later.</summary>
        public static bool TryPlay(System.Action onFinished)
        {
            if (FindFirstObjectByType<WorldTransitionCinematic>() != null) return false;
            if (Camera.main == null) return false;
            s_pendingOnFinished = onFinished;
            new GameObject("WorldTransitionCinematic").AddComponent<WorldTransitionCinematic>();
            return true;
        }

        private void Awake()
        {
            System.Action pending = s_pendingOnFinished;
            s_pendingOnFinished = null;
            Initialize(pending);
        }

        // ------------------------------------------------------------------ state

        private System.Action _onFinished;
        private BeatSequencer _sequencer;

        private Transform _root;
        private Camera _cam;
        private Transform _wreckSet;
        private Transform _shaftSet;
        private Transform _landSet;

        private Transform _wreckRig;
        private MeshRenderer _wreckGlow;
        private Transform _maxDrop;
        private MeshRenderer _daylightDisc;

        private GameObject _hud;
        private bool _fogWas;
        private PlayerController _suspendedPlayer;
        private bool _restored;
        private bool _done;
        private GUIStyle _skipStyle;
        private GUIStyle _titleStyle;
        private float _titleAlpha;

        private Transform _fade;
        private Color _fadeColor = Color.black;
        private float _fadeAlpha = 1f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock _fadeMpb;

        private static readonly Vector3 SetOrigin = new Vector3(-6000f, -6000f, -6000f);

        // ------------------------------------------------------------------ test-facing state

        /// <summary>Running until it hands off. A test reads this to prove it reaches the end.</summary>
        public bool IsPlaying => !_done;
        public float Elapsed => _sequencer.Elapsed;
        public float TotalDuration => _sequencer.TotalDuration;
        public int BeatIndex => _sequencer.BeatIndex;
        public string BeatName => _sequencer.BeatName;
        public float BeatElapsed => _sequencer.BeatElapsed;
        public int BeatCount => _sequencer.Count;

        // ------------------------------------------------------------------ build

        /// <summary>The Awake work, exposed so an EditMode test can build the cinematic directly — a
        /// MonoBehaviour without <c>[ExecuteAlways]</c> only ever receives <c>Awake</c> once Unity is
        /// actually in Play Mode, which this project's EditMode-only test suite never enters
        /// (MV-299/311/330).</summary>
        public void Initialize(System.Action onFinished)
        {
            _onFinished = onFinished;
            TakeOverScreen();
            BuildSet();
            BuildBeats();
        }

        private void TakeOverScreen()
        {
            Camera gameCam = Camera.main;

            var hud = FindFirstObjectByType<HudController>();
            if (hud != null) { _hud = hud.gameObject; _hud.SetActive(false); }

            var player = FindFirstObjectByType<PlayerController>();
            if (player != null) { _suspendedPlayer = player; player.enabled = false; }

            _fogWas = RenderSettings.fog;
            RenderSettings.fog = false;

            var camGo = new GameObject("WorldTransitionCam");
            camGo.transform.SetParent(transform, worldPositionStays: false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.fieldOfView = 55f;
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 4000f;
            _cam.depth = (gameCam != null ? gameCam.depth : 0f) + 100f;

            BuildFade();
        }

        private void BuildFade()
        {
            _fadeMpb = new MaterialPropertyBlock();
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Fade";
            IntroBuild.Strip(go);
            go.transform.SetParent(_cam.transform, worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.4f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(3f, 3f, 1f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = VfxMaterials.AlphaBlend(VfxMaterials.Solid());
            r.shadowCastingMode = ShadowCastingMode.Off;
            go.AddComponent<KeepsOwnMaterial>();
            _fade = go.transform;
            ApplyFade();
        }

        private void BuildSet()
        {
            _root = IntroBuild.Pivot(transform, "WorldTransitionSet", SetOrigin);
            _root.gameObject.AddComponent<KeepsOwnMaterial>();
            BuildWreckSet();
            BuildShaftSet();
            BuildLandSet();
        }

        /// <summary>WRECK + DROP share one set: the lawn, the wrecked boss rig settling onto the grate,
        /// and Max stepping in and falling through.</summary>
        private void BuildWreckSet()
        {
            _wreckSet = IntroBuild.Pivot(_root, "Wreck", Vector3.zero);

            var lawn = IntroBuild.Lit("wreck_lawn", StormdrainTransitionPalette.Lawn);
            IntroBuild.Part(_wreckSet, "Lawn", PrimitiveType.Cube, new Vector3(0f, -0.1f, 0f),
                            new Vector3(30f, 0.2f, 30f), lawn, castShadows: false);

            var hull = IntroBuild.Lit("wreck_hull", StormdrainTransitionPalette.WreckHull);
            _wreckRig = IntroBuild.Part(_wreckSet, "BossRig", PrimitiveType.Cube, new Vector3(0f, 1.5f, 0f),
                                        new Vector3(6f, 3f, 6f), hull, castShadows: false);

            _wreckGlow = IntroBuild.Glow(_wreckSet, "GrateGlow", new Vector3(0f, 0.05f, 0f), 0.1f,
                                         StormdrainTransitionPalette.PortalViolet);

            var maxMat = IntroBuild.Lit("wreck_max", IntroPalette.Hoodie);
            _maxDrop = IntroBuild.Part(_wreckSet, "MaxDrop", PrimitiveType.Capsule, new Vector3(0f, 1f, -4f),
                                       new Vector3(0.6f, 0.9f, 0.6f), maxMat, castShadows: false);

            _wreckSet.gameObject.SetActive(false);
        }

        /// <summary>SHAFT: a concrete shaft the camera falls down, a shrinking daylight disc above, and
        /// pipes flicking past — sold by the camera's own descent rather than any per-pipe animation.</summary>
        private void BuildShaftSet()
        {
            _shaftSet = IntroBuild.Pivot(_root, "Shaft", new Vector3(400f, 0f, 0f));

            const float shaftW = 6f;
            const float shaftDepth = 60f;
            var concrete = IntroBuild.Lit("shaft_wall", StormdrainTransitionPalette.Concrete);
            IntroBuild.Part(_shaftSet, "WallN", PrimitiveType.Cube,
                            new Vector3(0f, -shaftDepth * 0.5f, shaftW * 0.5f),
                            new Vector3(shaftW, shaftDepth, 0.6f), concrete, castShadows: false);
            IntroBuild.Part(_shaftSet, "WallS", PrimitiveType.Cube,
                            new Vector3(0f, -shaftDepth * 0.5f, -shaftW * 0.5f),
                            new Vector3(shaftW, shaftDepth, 0.6f), concrete, castShadows: false);
            IntroBuild.Part(_shaftSet, "WallE", PrimitiveType.Cube,
                            new Vector3(shaftW * 0.5f, -shaftDepth * 0.5f, 0f),
                            new Vector3(0.6f, shaftDepth, shaftW), concrete, castShadows: false);
            IntroBuild.Part(_shaftSet, "WallW", PrimitiveType.Cube,
                            new Vector3(-shaftW * 0.5f, -shaftDepth * 0.5f, 0f),
                            new Vector3(0.6f, shaftDepth, shaftW), concrete, castShadows: false);

            var pipeMat = IntroBuild.Lit("shaft_pipe", StormdrainTransitionPalette.Pipe);
            const int pipeCount = 5;
            for (int i = 0; i < pipeCount; i++)
            {
                float y = -(i + 0.5f) * (shaftDepth / pipeCount);
                IntroBuild.Part(_shaftSet, "Pipe", PrimitiveType.Cylinder,
                                new Vector3(shaftW * 0.42f, y, 0f),
                                new Vector3(0.3f, shaftDepth / pipeCount * 0.5f, 0.3f), pipeMat,
                                castShadows: false);
            }

            _daylightDisc = IntroBuild.Glow(_shaftSet, "DaylightDisc", new Vector3(0f, 3f, 0f), 5f,
                                            StormdrainTransitionPalette.DaylightDisc, flatten: 0.15f);

            _shaftSet.gameObject.SetActive(false);
        }

        /// <summary>LAND: the Outfall Steps — a1's landing pad, the light shaft Max fell through, and
        /// the three dim, dark portal rings on the north wall (the GDD's "other kids tried this" beat).
        /// Every prop here is sized/positioned off a1's own footprint, read fresh from the loaded World
        /// 2 config — never an absolute coordinate — so MV-700 replacing the placeholder config cannot
        /// desync it. Decoration only: no collider (<see cref="IntroBuild.Strip"/>, every part), and
        /// none of it is a <c>WorldCover</c> entry in any config — it never touches the real gameplay
        /// arena at all, the same "picture, not a place" idiom every other intro act already follows.</summary>
        private void BuildLandSet()
        {
            WorldArea a1 = ResolveA1();
            float w = a1?.size != null ? Mathf.Max(a1.size.w, 8f) : 22f;
            float d = a1?.size != null ? Mathf.Max(a1.size.d, 8f) : 20f;

            _landSet = IntroBuild.Pivot(_root, "Land", new Vector3(800f, 0f, 0f));

            var floor = IntroBuild.Lit("land_floor", StormdrainTransitionPalette.WetConcrete);
            IntroBuild.Part(_landSet, "LandingPad", PrimitiveType.Cube, new Vector3(0f, -0.5f, 0f),
                            new Vector3(w, 1f, d), floor, castShadows: false);

            var wall = IntroBuild.Lit("land_wall", StormdrainTransitionPalette.Concrete);
            IntroBuild.Part(_landSet, "NorthWall", PrimitiveType.Cube, new Vector3(0f, 3f, -d * 0.5f),
                            new Vector3(w, 6f, 1f), wall, castShadows: false);

            Vector3 shaftLocal = new Vector3(-w * 0.3f, 5f, -d * 0.3f);
            IntroBuild.Glow(_landSet, "LightShaft", shaftLocal, 2.2f,
                            StormdrainTransitionPalette.DaylightDisc, flatten: 5f);

            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * (w * 0.2f);
                IntroBuild.Glow(_landSet, $"PortalRing{i}", new Vector3(x, 3f, -d * 0.5f + 0.05f), 1.4f,
                                StormdrainTransitionPalette.PortalDim);
            }

            _landSet.gameObject.SetActive(false);
        }

        /// <summary>World 2's a1 ("Outfall Steps"), read fresh — never cached across calls — so the
        /// LAND set always matches whatever a1 currently is, placeholder or MV-700's real design.
        /// Null if world2_config is missing/broken, which <see cref="BuildLandSet"/> falls back from
        /// rather than ever failing the transition over a data problem downstream of it.</summary>
        private static WorldArea ResolveA1()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            return cfg?.Area("a1");
        }

        // ------------------------------------------------------------------ the timeline

        private void BuildBeats()
        {
            var beats = new[]
            {
                new Beat(Wreck, 2f, WreckBeat, () => Show(_wreckSet)),
                new Beat(Drop, 1.5f, DropBeat, null),
                new Beat(Shaft, 4f, ShaftBeat, () => { Cut(); Show(_shaftSet); }),
                new Beat(Land, 2.5f, LandBeat, () => { Cut(); Show(_landSet); }),
            };
            _sequencer = new BeatSequencer(beats);
        }

        private void Show(Transform actRoot)
        {
            _wreckSet.gameObject.SetActive(actRoot == _wreckSet);
            _shaftSet.gameObject.SetActive(actRoot == _shaftSet);
            _landSet.gameObject.SetActive(actRoot == _landSet);
        }

        // ---- beat: WRECK — the boss rig settles onto the grate, which cracks and bleeds violet light ----
        private void WreckBeat(float t)
        {
            _wreckRig.localPosition = new Vector3(0f, 1.5f - 0.6f * t, 0f);
            float glowT = IntroBuild.Ramp(0.35f, 1f, t);
            float scale = Mathf.Lerp(0.1f, 2.2f, glowT);
            _wreckGlow.transform.localScale = new Vector3(scale, scale, scale);
            IntroBuild.SetGlow(_wreckGlow, StormdrainTransitionPalette.PortalViolet * (0.4f + glowT));
            AimCam(_wreckSet, new Vector3(0f, 6f, -10f), Vector3.zero,
                              new Vector3(0f, 4f, -6f), Vector3.zero, t);
        }

        // ---- beat: DROP — Max steps to the grate and falls through ----
        private void DropBeat(float t)
        {
            float walk = IntroBuild.Ramp(0f, 0.35f, t);
            float fall = IntroBuild.Ramp(0.35f, 1f, t);
            Vector3 pos = Vector3.Lerp(new Vector3(0f, 1f, -4f), Vector3.zero, walk);
            pos.y = Mathf.Lerp(pos.y, -20f, fall);
            _maxDrop.localPosition = pos;
            AimCam(_wreckSet, new Vector3(0f, 4f, -6f), Vector3.zero,
                              new Vector3(0f, 1f, -1f), new Vector3(0f, -2f, 0f), t);
            if (t > 0.85f) _fadeColor = StormdrainTransitionPalette.PortalViolet;
            _fadeAlpha = Mathf.Max(_fadeAlpha, IntroBuild.Ramp(0.85f, 1f, t));
        }

        // ---- beat: SHAFT — the camera falls, the daylight disc above shrinks with distance ----
        private const float ShaftDepth = 60f;

        private void ShaftBeat(float t)
        {
            float camY = Mathf.Lerp(-2f, -ShaftDepth + 6f, t);
            Vector3 pos = new Vector3(0f, camY, -1.5f);
            Vector3 look = new Vector3(0f, camY - 6f, 0f);
            _cam.transform.position = _shaftSet.TransformPoint(pos);
            Vector3 dir = _shaftSet.TransformPoint(look) - _cam.transform.position;
            if (dir.sqrMagnitude > 1e-5f)
                _cam.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

            float discScale = Mathf.Lerp(5f, 0.6f, t);
            _daylightDisc.transform.localScale = new Vector3(discScale, discScale, discScale * 0.15f);
        }

        // ---- beat: LAND — the Outfall Steps, and the title card ----
        private void LandBeat(float t)
        {
            AimCam(_landSet, new Vector3(0f, 10f, -14f), Vector3.zero,
                             new Vector3(0f, 7f, -9f), Vector3.zero, t);
            _titleAlpha = IntroBuild.Ramp(0.25f, 0.75f, t);
            _fadeAlpha = Mathf.Min(_fadeAlpha, 1f - IntroBuild.Ramp(0f, 0.15f, t));
        }

        // ------------------------------------------------------------------ running it

        private void LateUpdate()
        {
            if (_done) return;
            if (SkipRequested()) Skip();
            Tick(Time.unscaledDeltaTime);
        }

        private static bool SkipRequested()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
            var ptr = Pointer.current;
            if (ptr != null && ptr.press.wasPressedThisFrame) return true;
            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.wasPressedThisFrame) return true;
            return false;
        }

        /// <summary>Jump straight to LAND at 0 s — not straight to the end — so the title card and
        /// landing beat still read on a fast skip. A no-op once already there (repeat input mid-LAND
        /// must not restart it). Public so a test can drive it without synthesising input.</summary>
        public void Skip()
        {
            if (_done || _sequencer.BeatIndex == LandBeatIndex) return;
            _sequencer.JumpTo(LandBeatIndex, 0f);
            ApplyFade();
        }

        /// <summary>Advance the timeline by <paramref name="dt"/> unscaled seconds. Public so a test can
        /// fast-forward the ~10 s sequence.</summary>
        public void Tick(float dt)
        {
            if (_done) return;
            bool finished = _sequencer.Tick(dt);
            _fadeAlpha = Mathf.MoveTowards(_fadeAlpha, 0f, dt * 2.2f);
            ApplyFade();
            if (finished) Finish();
        }

        private void AimCam(Transform actRoot, Vector3 fromLocal, Vector3 lookFromLocal,
                            Vector3 toLocal, Vector3 lookToLocal, float t)
        {
            float e = Mathf.SmoothStep(0f, 1f, t);
            Vector3 pos = actRoot.TransformPoint(Vector3.Lerp(fromLocal, toLocal, e));
            Vector3 look = actRoot.TransformPoint(Vector3.Lerp(lookFromLocal, lookToLocal, e));
            _cam.transform.position = pos;
            Vector3 dir = look - pos;
            if (dir.sqrMagnitude > 1e-5f)
                _cam.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        private void Cut()
        {
            _fadeColor = Color.white;
            _fadeAlpha = 1f;
            ApplyFade();
        }

        private void ApplyFade()
        {
            if (_fade == null) return;
            var r = _fade.GetComponent<MeshRenderer>();
            if (r == null) return;
            Color c = _fadeColor; c.a = Mathf.Clamp01(_fadeAlpha);
            r.GetPropertyBlock(_fadeMpb);
            _fadeMpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(_fadeMpb);
            r.enabled = c.a > 0.001f;
        }

        // ------------------------------------------------------------------ handoff

        private void OnGUI()
        {
            if (_done) return;
            _skipStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(16, Mathf.RoundToInt(Screen.height * 0.03f)),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 1f, 1f, 0.72f) }
            };
            float h = Screen.height * 0.06f;
            GUI.Label(new Rect(0f, Screen.height - h * 1.6f, Screen.width, h), "TAP TO SKIP", _skipStyle);

            if (_titleAlpha > 0.01f)
            {
                _titleStyle ??= new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(20, Mathf.RoundToInt(Screen.height * 0.05f)),
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white }
                };
                Color c = _titleStyle.normal.textColor;
                c.a = _titleAlpha;
                _titleStyle.normal.textColor = c;
                GUI.Label(new Rect(0f, Screen.height * 0.35f, Screen.width, Screen.height * 0.12f),
                         "WORLD 2 — STORMDRAIN", _titleStyle);
            }
        }

        /// <summary>Give the screen back exactly as it was borrowed, hand control to
        /// <see cref="_onFinished"/>, and remove the cinematic entirely — the natural end of the
        /// timeline or a skip playing LAND out, either way.</summary>
        private void Finish()
        {
            if (_done) return;
            _done = true;
            Restore();
            _onFinished?.Invoke();
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        private void Restore()
        {
            if (_restored) return;
            _restored = true;
            if (_hud != null) _hud.SetActive(true);
            RenderSettings.fog = _fogWas;
            if (_suspendedPlayer != null) _suspendedPlayer.enabled = true;
        }

        private void OnDestroy() => Restore();
    }
}
