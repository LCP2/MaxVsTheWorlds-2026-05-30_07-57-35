using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using MaxWorlds.Player;
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
    /// YT-155 — the gameplay half OWNS the trigger, the skip, and the handoff.
    ///
    /// The five-beat sequence is a code-driven timeline: the beats scrub the art act-builders
    /// (<see cref="IntroSpace.SetComet"/>, <see cref="IntroShed.SetPhase"/>, …) on an unscaled clock in
    /// <see cref="Tick"/>. It is deliberately NOT a Unity <c>TimelineAsset</c>/<c>PlayableDirector</c>:
    /// per <c>docs/CODE_DRIVEN_SCENES.md</c> a fresh clone must build and run headlessly with zero
    /// inspector wiring, and a Timeline asset's track bindings are exactly that hand-wiring. So the
    /// "Timeline" is a committed C# sequence — same shape as the boss abilities (YT-157).
    ///
    ///  * TRIGGER — <see cref="TryPlay"/> starts it once per process (the New Game). YT-151's Home
    ///    screen calls it from its New Game button (never from Continue, and never on a Replay-triggered
    ///    scene reload — <see cref="MaxWorlds.Save.SaveSystem.ActiveSlot"/> being already set is what
    ///    skips the Home screen, and with it this call, on those loads).
    ///  * SKIP — a tap, click, or any key ends it immediately and drops straight into gameplay
    ///    (<see cref="Skip"/>, driven from <see cref="LateUpdate"/>; a "Tap to skip" prompt shows while
    ///    it runs).
    ///  * HANDOFF — at the end (or on skip) the screen is given back exactly as it was borrowed and
    ///    control returns to the player (<see cref="Restore"/>).
    ///
    /// It takes the screen over cleanly and puts it back: the intro camera draws OVER the gameplay one
    /// (higher depth + a solid clear), rather than disabling it — so <c>Camera.main</c> stays valid for
    /// the many systems that cache it (the HUD, world health bars, the minimap). The HUD is hidden, fog
    /// switched off, and the player's <see cref="PlayerController"/> suspended for the ~24 s it runs, so
    /// blind input under the cinematic never moves Max; all three are restored at handoff.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IntroCinematic : MonoBehaviour
    {
        private static bool s_consumed;

        /// <summary>
        /// Start the opening cinematic — the New-Game trigger, called by the Home screen (YT-151). Plays
        /// once per process (never on a Replay-triggered scene reload, and never on Continue) and only
        /// when there is a <c>Camera.main</c> to take over and hand back to. Returns true if it started.
        /// </summary>
        public static bool TryPlay()
        {
            if (s_consumed) return false;                        // once per process — not on Replay
            if (FindFirstObjectByType<IntroCinematic>() != null) return false;
            if (Camera.main == null) return false;              // nothing to take over / hand back to
            s_consumed = true;
            new GameObject("IntroCinematic").AddComponent<IntroCinematic>();
            return true;
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
        private PlayerController _suspendedPlayer;   // control taken for the cinematic, returned at handoff
        private bool _restored;                      // Restore() is idempotent — handoff, skip, or Destroy
        private GUIStyle _skipStyle;

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

        /// <summary>True while the intro is holding the player's control (disabled for the cinematic).
        /// A test reads this to prove control is suspended during, and returned after, the sequence.</summary>
        public bool PlayerControlSuspended => _suspendedPlayer != null && !_suspendedPlayer.enabled;

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

            // Suspend the player: the gameplay camera keeps ticking unseen beneath the cinematic, so
            // without this a player mashing the stick during the intro would drive Max blind and hand off
            // to a Max who has wandered off (or died). Disabling the controller stops Update AND releases
            // its input actions; re-enabled at handoff, which is "control returns to the player".
            var player = FindFirstObjectByType<PlayerController>();
            if (player != null) { _suspendedPlayer = player; player.enabled = false; }

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
                // Lengthened from 5.4s (YT-162): the old duration didn't leave room to hold on Max, stage
                // a real hose grab, THEN turn and open the door — see IntroShed.SetPhase's sub-beats.
                new Beat("shed", 6.6f, ShedBeat, () => { Cut(); Show(_shed.Root); }),
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

        // The camera reaches its final framing here (as a fraction of the beat) and holds — the rest of
        // the beat is hang time on the impacts, not more camera travel (YT-161).
        private const float SplitLandCameraSettle = 0.7f;

        // ---- beat 3: it splits; the pods rain down and land ----
        private void SplitLand(float t)
        {
            _space.SetComet(1f);
            _space.SetSplit(t);
            // Framed on the impact cluster on the near hemisphere, not the globe's centre — aiming at
            // (0,-6,0) put the landings small and off toward the globe's edge (YT-161); this holds on
            // where they actually strike, closer in, so several unmistakable landings read in-frame.
            float camT = Mathf.Clamp01(t / SplitLandCameraSettle);
            AimCam(_space.Root,
                    new Vector3(4f, 12f, -150f), Vector3.zero,
                    new Vector3(0f, 18f, -94f), new Vector3(0f, 9f, -38f), camT);
        }

        // ---- beat 4: plunge — continent, town, the shed roof, and a readable arrival ----
        private void Dive(float t)
        {
            var apex = _descent.DiveTarget;
            Vector3 apexLocal = _descent.Root.InverseTransformPoint(apex.position);
            // From high above, looking down and a little forward, down to just over the roof.
            Vector3 from = apexLocal + new Vector3(0f, 640f, -260f);
            Vector3 to = apexLocal + new Vector3(0f, 22f, -30f);
            // AimCam already eases with SmoothStep; driving it with a SECOND, quadratic ease compounded
            // into an extreme late-beat rush — over 90% of the whole descent used to land in the final
            // ~20% of the beat, too fast to resolve continent -> town -> shed before the cut (YT-161).
            // A single ease settles the camera on the roof well before the flash: the shed is on screen
            // and legible, THEN the beat flashes to the cut — not a blur that happens to end on white.
            AimCam(_descent.Root, from, apexLocal, to, apexLocal, t);
            // The flash starts only once the camera has essentially arrived (see above), so it reads as
            // a brief flash on an already-legible shot of the shed, not a hard slam mid-motion.
            if (t > 0.85f) _fadeColor = Color.white;
            _fadeAlpha = Mathf.Max(_fadeAlpha, IntroBuild.Ramp(0.85f, 1f, t));
        }

        // The camera holds still on this fraction of the beat before it starts pushing in — long enough
        // to actually read Max before anything moves (YT-162; matches IntroShed.NoticeStart).
        private const float ShedCameraHoldEnd = 0.18f;

        // ---- beat 5: hold on Max, then the grab, then he turns, then the door ----
        private void ShedBeat(float t)
        {
            _shed.SetPhase(t);
            Vector3 anchor = _shed.Root.InverseTransformPoint(_shed.CameraAnchor.position);
            Vector3 look = _shed.Root.InverseTransformPoint(_shed.Max.position) + Vector3.up * 1.1f;
            // Hold on the opening frame, THEN a slow push in through the grab, the turn, and the door —
            // not a push that's already under way the instant the beat starts (YT-162).
            float camT = Mathf.Clamp01((t - ShedCameraHoldEnd) / (1f - ShedCameraHoldEnd));
            AimCam(_shed.Root, anchor + new Vector3(0.6f, 0.1f, -0.4f), look,
                    anchor + new Vector3(-0.2f, 0f, 0.5f), look + Vector3.up * 0.1f, camT);
            // Fade to daylight-white only once the door is nearly wide — after Max and the grab have
            // clearly read, not stacked on top of them (YT-162).
            if (t > 0.94f) { _fadeColor = Color.white; _fadeAlpha = IntroBuild.Ramp(0.94f, 1f, t); }
        }

        // ------------------------------------------------------------------ running it

        private void LateUpdate()
        {
            if (_done) return;
            if (SkipRequested()) { Skip(); return; }
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>A tap, click, pen, or any key skips the intro. Polled on the New Input System so it
        /// works with touch on device and mouse/keyboard in the editor and on WebGL.</summary>
        private static bool SkipRequested()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
            var ptr = Pointer.current;                 // mouse, pen, or the primary touch
            if (ptr != null && ptr.press.wasPressedThisFrame) return true;
            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.wasPressedThisFrame) return true;
            return false;
        }

        /// <summary>Tap-to-skip: end the cinematic now and drop straight into gameplay. Public so a test
        /// can drive it without synthesising input.</summary>
        public void Skip()
        {
            if (_done) return;
            Handoff();
        }

        // The "Tap to skip" affordance. IMGUI (like the FPS readout) so it needs no font asset and is
        // guaranteed to draw in the WebGL/headless build.
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
        }

        /// <summary>
        /// Advance the cinematic one step: pick the live beat, scrub it, and drive the intro camera. This
        /// IS the code-driven timeline (YT-155) — the beats and their by-time scrubs of the art builders.
        /// Public so a test can fast-forward the ~24 s sequence; in the game it runs from LateUpdate on
        /// unscaled time, and hands off to gameplay when the clock passes the last beat.
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
            Restore();
            Destroy(gameObject);
        }

        /// <summary>
        /// Return everything the intro borrowed: show the HUD, restore fog, and hand control back to the
        /// player. Idempotent and also called from <see cref="OnDestroy"/>, so a stray cinematic cleared
        /// by a test (or any early teardown) never leaves the player disabled or the yard's fog off.
        /// </summary>
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
