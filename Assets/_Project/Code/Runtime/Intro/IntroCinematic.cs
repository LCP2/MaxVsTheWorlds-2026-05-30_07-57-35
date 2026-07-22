using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Intro
{
    /// <summary>
    /// The opening cinematic (YT-156, epic YT-154): space → comet → the invaders landing → a plunge to
    /// Earth → Max's shed, where he grabs his hose and opens the door on the game.
    ///
    /// This is the ART half. It builds every asset in the sequence and animates it — the starfield and
    /// Earth (<see cref="IntroSpace"/>), the comet and its splitting pods, the continent/town/shed dive
    /// (<see cref="IntroDescent"/>), and the shed interior with Max, his hose and the door
    /// (<see cref="IntroShed"/>). The three acts are staged far apart and the camera CUTS between them,
    /// which is how an in-engine intro fakes an orbit-to-ground journey no single top-down set can hold.
    ///
    /// ---------------------------------------------------------------------------------------------
    /// THE YT-155 SEAM — this drives its own camera and skip as a STAND-IN
    ///
    /// The gameplay half (YT-155: Unity Timeline, camera moves, the New-Game trigger, skip, handoff) is
    /// not built yet. So — exactly as the Brood-Hulk's spawn hatches run an art-side cadence until YT-157
    /// lands (<see cref="MaxWorlds.VFX.BigBermudaRig"/>) — this plays itself on an internal clock, drives
    /// its own camera, and hands off on its own, so the art is on Lee's WebGL link NOW. When YT-155 lands,
    /// it OWNS the trigger/camera/skip/handoff: keep the act builders and their by-time scrub methods
    /// (<see cref="IntroSpace.SetComet"/>, <see cref="IntroShed.SetPhase"/>, …) and drive them from the
    /// Timeline; delete <see cref="Tick"/>'s camera + input block. The seam is marked below.
    ///
    /// It takes the screen over cleanly and puts it back: the intro camera draws OVER the gameplay one
    /// (higher depth + a solid clear), rather than disabling it — so <c>Camera.main</c> stays valid for
    /// the many systems that cache it (the HUD, world health bars, the minimap). The HUD is hidden and
    /// fog switched off for the ~21 s it runs, then both restored at handoff. It plays ONCE per process
    /// (never on a Replay-triggered scene reload), and touches no gameplay logic. There is no skip — the
    /// real tap-to-skip is YT-155's; the art stand-in simply plays through and hands off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IntroCinematic : MonoBehaviour
    {
        private static bool s_consumed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (s_consumed) return;                              // once per process — not on Replay
            if (FindFirstObjectByType<IntroCinematic>() != null) return;
            if (Camera.main == null) return;                    // nothing to take over / hand back to
            s_consumed = true;
            new GameObject("IntroCinematic").AddComponent<IntroCinematic>();
        }

        // The three acts live far apart in world space so nothing overlaps the yard (at the origin) or
        // each other; the camera cuts between them.
        private static readonly Vector3 IntroOrigin = new Vector3(6000f, 6000f, 6000f);
        private static readonly Vector3 SpaceOffset = Vector3.zero;
        private static readonly Vector3 DescentOffset = new Vector3(0f, -3000f, 0f);
        private static readonly Vector3 ShedOffset = new Vector3(0f, -6000f, 0f);

        private Transform _root;
        private Camera _cam;
        private IntroSpace _space;
        private IntroDescent _descent;
        private IntroShed _shed;

        // The screen the intro borrowed, to be handed back exactly as it was found.
        private GameObject _hud;
        private bool _fogWas;

        // A full-screen fader for the opening fade-in, the cuts between acts, and the final fade-out.
        private Transform _fade;
        private Color _fadeColor = Color.black;
        private float _fadeAlpha = 1f;      // opens on black and fades up
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock _fadeMpb;

        private Beat[] _beats;
        private float _clock;
        private int _beat = -1;
        private bool _done;

        // ------------------------------------------------------------------ test-facing state

        /// <summary>Running until it hands off. A test reads this to prove it reaches the end.</summary>
        public bool IsPlaying => !_done;
        public float Elapsed => _clock;
        public float TotalDuration { get; private set; }
        public int BeatIndex => _beat;
        public string BeatName => _beat >= 0 && _beat < _beats.Length ? _beats[_beat].Name : "(none)";
        public IntroSpace Space => _space;
        public IntroDescent Descent => _descent;
        public IntroShed Shed => _shed;
        public Camera IntroCamera => _cam;

        private readonly struct Beat
        {
            public readonly string Name;
            public readonly float Duration;
            public readonly System.Action<float> Apply;   // t in 0..1 across the beat
            public readonly System.Action OnEnter;
            public Beat(string name, float duration, System.Action<float> apply, System.Action onEnter)
            {
                Name = name; Duration = duration; Apply = apply; OnEnter = onEnter;
            }
        }

        // ------------------------------------------------------------------ build

        private void Awake()
        {
            TakeOverScreen();
            BuildSet();
            BuildBeats();
        }

        private void TakeOverScreen()
        {
            var gameCam = Camera.main;   // captured only to draw ABOVE it — never disabled

            // The HUD is a ScreenSpaceOverlay canvas — it draws to the backbuffer after every camera, so
            // it would sit on top of the cinematic. Hide it (PressKitDirector does the same to film clean
            // shots) and bring it back at handoff.
            var hud = FindFirstObjectByType<HudController>();
            if (hud != null) { _hud = hud.gameObject; _hud.SetActive(false); }

            // Fog is global and tuned for the yard; at the intro's scale it would grey out space. Off for
            // the duration, restored at handoff.
            _fogWas = RenderSettings.fog;
            RenderSettings.fog = false;

            var camGo = new GameObject("IntroCam");
            camGo.transform.SetParent(transform, worldPositionStays: false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = IntroPalette.Space;
            _cam.fieldOfView = 55f;
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 5000f;
            // Highest depth in the scene: it renders last and its solid clear paints over the gameplay
            // camera, which keeps ticking (and stays Camera.main) unseen beneath.
            _cam.depth = (gameCam != null ? gameCam.depth : 0f) + 100f;

            BuildFade();
        }

        private void BuildSet()
        {
            _root = IntroBuild.Pivot(transform, "IntroSet", IntroOrigin);
            _space = new IntroSpace(_root, SpaceOffset);
            _descent = new IntroDescent(_root, DescentOffset);
            _shed = new IntroShed(_root, ShedOffset);
        }

        private void BuildFade()
        {
            _fadeMpb = new MaterialPropertyBlock();
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Fade";
            IntroBuild.Strip(go);
            go.transform.SetParent(_cam.transform, worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.4f);   // just past the near plane
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(3f, 3f, 1f);        // oversized to cover any aspect
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = VfxMaterials.AlphaBlend(VfxMaterials.Solid());
            r.shadowCastingMode = ShadowCastingMode.Off;
            _fade = go.transform;
            ApplyFade();
        }

        // ------------------------------------------------------------------ the timeline

        private void BuildBeats()
        {
            _beats = new[]
            {
                new Beat("space-open", 3.2f, SpaceOpen, () => Show(_space.Root)),
                new Beat("comet", 4.6f, Comet, null),
                new Beat("split-land", 4.4f, SplitLand, null),
                new Beat("descent", 4.8f, Dive, () => { Cut(); Show(_descent.Root); }),
                new Beat("shed", 5.4f, ShedBeat, () => { Cut(); Show(_shed.Root); }),
            };

            float total = 0f;
            foreach (var b in _beats) total += b.Duration;
            TotalDuration = total;
        }

        /// <summary>Only one act is on screen at a time.</summary>
        private void Show(Transform actRoot)
        {
            _space.SetActive(actRoot == _space.Root);
            _descent.SetActive(actRoot == _descent.Root);
            _shed.SetActive(actRoot == _shed.Root);
        }

        // ---- beat 1: open on the starfield, Earth small in high orbit ----
        private void SpaceOpen(float t)
        {
            _space.SetComet(0f);
            _space.ResetSplit();
            AimCam(_space.Root,
                    new Vector3(-30f, 24f, -235f), new Vector3(60f, 26f, -30f),
                    new Vector3(-14f, 20f, -215f), new Vector3(30f, 14f, -20f), t);
        }

        // ---- beat 2: the comet scorches past; camera pans in to reveal high orbit ----
        private void Comet(float t)
        {
            _space.SetComet(t);
            AimCam(_space.Root,
                    new Vector3(-14f, 20f, -215f), new Vector3(30f, 14f, -20f),
                    new Vector3(4f, 12f, -150f), Vector3.zero, t);
        }

        // ---- beat 3: it splits; the pods rain down and land ----
        private void SplitLand(float t)
        {
            _space.SetComet(1f);
            _space.SetSplit(t);
            AimCam(_space.Root,
                    new Vector3(4f, 12f, -150f), Vector3.zero,
                    new Vector3(22f, 6f, -120f), new Vector3(0f, -6f, 0f), t);
        }

        // ---- beat 4: plunge — continent, town, the shed roof, and through it ----
        private void Dive(float t)
        {
            var apex = _descent.DiveTarget;
            Vector3 apexLocal = _descent.Root.InverseTransformPoint(apex.position);
            // From high above, looking down and a little forward, down to just over the roof.
            Vector3 from = apexLocal + new Vector3(0f, 640f, -260f);
            Vector3 to = apexLocal + new Vector3(0f, 22f, -30f);
            float e = t * t;                                   // accelerate down — it is a plunge
            AimCam(_descent.Root, from, apexLocal, to, apexLocal, e);
            // The last instant drops through the roof: a hard white flash into the cut.
            if (t > 0.9f) _fadeColor = Color.white;
            _fadeAlpha = Mathf.Max(_fadeAlpha, IntroBuild.Ramp(0.9f, 1f, t));
        }

        // ---- beat 5: inside the shed — oblivious, then the hose, then the door ----
        private void ShedBeat(float t)
        {
            _shed.SetPhase(t);
            Vector3 anchor = _shed.Root.InverseTransformPoint(_shed.CameraAnchor.position);
            Vector3 look = _shed.Root.InverseTransformPoint(_shed.Max.position) + Vector3.up * 1.1f;
            // A slow push in on Max as he turns and the door opens.
            AimCam(_shed.Root, anchor + new Vector3(0.6f, 0.1f, -0.4f), look,
                    anchor + new Vector3(-0.2f, 0f, 0.5f), look + Vector3.up * 0.1f, t);
            // Fade to daylight-white as the door reaches wide, into the handoff.
            if (t > 0.85f) { _fadeColor = Color.white; _fadeAlpha = IntroBuild.Ramp(0.85f, 1f, t); }
        }

        // ------------------------------------------------------------------ running it

        private void LateUpdate()
        {
            if (_done) return;
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Advance the cinematic. Public so a test can fast-forward it; in the game it is called from
        /// LateUpdate on unscaled time. >>> YT-155 SEAM: the camera driving below, and the auto-handoff at
        /// the end, are the stand-in the Timeline (and its tap-to-skip) replace. <<<
        /// </summary>
        public void Tick(float dt)
        {
            if (_done) return;

            // The per-frame housekeeping for whichever act is live (Earth turns, VFX budgets refill).
            if (_beat >= 0 && _beat <= 2) _space.Frame(dt);

            _clock += dt;

            // Which beat are we in, and how far through it?
            float acc = 0f;
            int idx = _beats.Length - 1;
            float local = 1f;
            for (int i = 0; i < _beats.Length; i++)
            {
                if (_clock < acc + _beats[i].Duration || i == _beats.Length - 1)
                {
                    idx = i;
                    local = Mathf.Clamp01((_clock - acc) / _beats[i].Duration);
                    break;
                }
                acc += _beats[i].Duration;
            }

            if (idx != _beat)
            {
                _beat = idx;
                _beats[_beat].OnEnter?.Invoke();
            }

            _beats[_beat].Apply(local);

            // The opening fade-up, and the cut flashes, decay every frame unless a beat re-drives them.
            _fadeAlpha = Mathf.MoveTowards(_fadeAlpha, 0f, dt * 2.2f);
            ApplyFade();

            // Past the last beat, we are done.
            if (_clock >= TotalDuration) Handoff();
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

        /// <summary>A cut to a new act: snap the fader to white so the switch never shows a seam.</summary>
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
            var c = _fadeColor; c.a = Mathf.Clamp01(_fadeAlpha);
            r.GetPropertyBlock(_fadeMpb);
            _fadeMpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(_fadeMpb);
            r.enabled = c.a > 0.001f;
        }

        // ------------------------------------------------------------------ handoff

        /// <summary>Give the screen back exactly as it was borrowed, and remove the cinematic entirely.</summary>
        private void Handoff()
        {
            if (_done) return;
            _done = true;

            if (_hud != null) _hud.SetActive(true);
            RenderSettings.fog = _fogWas;

            Destroy(gameObject);
        }
    }
}
