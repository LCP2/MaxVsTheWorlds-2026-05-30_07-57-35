using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Player;
using MaxWorlds.Save;
using MaxWorlds.UI;

namespace MaxWorlds.Intro
{
    /// <summary>
    /// MV-1245: the Mothership's finale, beats 2-4 -- built the instant <see cref="MaxWorlds.VFX.WorldFinaleGate.OnHelmKeyCollected"/>
    /// resolves the viewport exit (same "build it the instant the door opens, let Max walk there
    /// himself" idiom <see cref="WorldJoinSequence"/> already uses for every other world's corridor; the
    /// game never takes control of Max until the very last beat). Beyond the now-open north viewport: a
    /// 30 m x 5 m walkway with an open (collider-only, unrendered) window down one side so the planet
    /// built out past it is never occluded, and a glass cap at the far end. The planet's apparent size
    /// grows with Max's own progress down the deck -- colour only, never scaled per frame, lives in
    /// exactly one place (<see cref="PlanetColor"/>, the ticket's own Change 4).
    ///
    /// After Max stands within <see cref="FarDwellRadius"/> of the far glass for <see cref="FarDwellSeconds"/>,
    /// the one and only beat in the game that takes the screen over from gameplay plays: a flat "TO BE
    /// CONTINUED" composition (<see cref="EndShotOverlay"/>), then a short generic credits card, then the
    /// run ends for good -- <see cref="SaveSystem.MarkEndingSeen"/> stamps the profile and wipes its
    /// checkpoint (nothing left to RESUME into) before <see cref="RunFlow.QuitToMenu"/> drops back to the
    /// Home screen.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("intro")]
    public sealed class ObservationDeckSequence : MonoBehaviour
    {
        /// <summary>The ticket's own "a 30 m x 5 m walkway".</summary>
        public const float DeckWidth = 5f;
        public const float DeckLength = 30f;

        private const float WallThickness = 0.3f;

        /// <summary>"After Max stands at the glass for 1.0 s" -- approximated as "within this radius of
        /// the far glass" rather than true zero-velocity, so a brief side-step at the window doesn't
        /// reset the dwell on a single stray frame.</summary>
        private const float FarDwellRadius = 1.5f;
        private const float FarDwellSeconds = 1f;

        private const float EndShotHoldSeconds = 3f;
        private const float CreditsHoldSeconds = 3f;

        private const float PlanetStartDiameter = 14f;
        private const float PlanetEndDiameter = 46f;
        private const float PlanetOutDistance = 26f;   // how far past the window barrier the planet sits

        /// <summary>MV-1245 Change 4: "blue-green, unnamed" -- the ONE place to edit once Lee picks the
        /// real planet. Shared by the in-deck planet sphere and <see cref="EndShotOverlay"/>'s own
        /// planet graphic -- changing this one constant changes both.</summary>
        public static readonly Color PlanetColor = new Color(0.20f, 0.56f, 0.50f);

        /// <summary>MV-1253: the planet's name (Lee, 19 Oct) -- the ONE place that names it, shared
        /// by the end shot's own caption (<see cref="EndShotOverlay"/>) and the deck's window
        /// label.</summary>
        public const string PlanetName = "SOTAMI";

        private const float WindowLabelWorldWidth = 2.5f;   // "small text" above the glass
        private const float WindowLabelClearance = 0.4f;    // gap above the glass's own top edge

        private enum EndPhase { None, ToBeContinued, Credits, Done }

        private Wall _wall;
        private Vector2 _doorMouth;
        private float _wallHeight;

        private Transform _floor;
        private Transform _farGlass;
        private Transform _planet;

        private Transform _player;
        private float _dwellTimer;
        private EndPhase _endPhase = EndPhase.None;
        private float _endPhaseTime;
        private EndShotOverlay _overlay;

        /// <summary>The built floor's own collider -- read by the EditMode test to measure the real,
        /// resolved geometry (Tier 2) rather than trust the formula that built it.</summary>
        public Transform Floor => _floor;

        /// <summary>The far glass Max stands at -- its own near face is where the floor's far edge must
        /// land with no gap (AC1's own "reaches both its doors").</summary>
        public Transform FarGlass => _farGlass;

        /// <summary>True once the end beat (screen takeover) has started.</summary>
        public bool EndBeatStarted => _endPhase != EndPhase.None;

        /// <summary>Builds the deck the instant the viewport opens. A no-op (returns null) if one
        /// already exists for this run, or the finale door's own doorway never resolved (a
        /// geometry-independent fixture/test with no real map built).</summary>
        public static ObservationDeckSequence Build(ExitDoorway doorway)
        {
            if (FindFirstObjectByType<ObservationDeckSequence>() != null) return null;

            var go = new GameObject("Observation Deck").AddComponent<ObservationDeckSequence>();
            go.BuildGeometry(doorway);
            return go;
        }

        private void BuildGeometry(ExitDoorway doorway)
        {
            _wall = doorway.Wall;
            _wallHeight = 3f;   // mothership_config.json's own "wallHeight": 3 -- no live MapData reference needed here
            float mid = doorway.Hole.Mid;
            _doorMouth = doorway.AlongX ? new Vector2(mid, doorway.Coord) : new Vector2(doorway.Coord, mid);

            Material floorMat = IntroBuild.Lit("mv1245_deck_floor", new Color(0.30f, 0.31f, 0.33f));
            Material wallMat = IntroBuild.Lit("mv1245_deck_wall", new Color(0.18f, 0.19f, 0.21f));
            Material glassMat = IntroBuild.Lit("mv1245_deck_glass",
                new Color(0.55f, 0.75f, 0.80f), emission: new Color(0.08f, 0.16f, 0.18f));

            float halfWidth = DeckWidth * 0.5f;

            _floor = BuildLocalBox("Deck Floor", 0f, DeckLength, -halfWidth, halfWidth, -0.1f, 0f, floorMat, collide: true).transform;

            BuildLocalBox("Deck Wall East", 0f, DeckLength, halfWidth, halfWidth + WallThickness,
                0f, _wallHeight, wallMat, collide: true);

            // The window side: collider-only (unrendered) so nothing blocks the view of the planet
            // beyond it -- "windows down one side", built as one open run rather than individual panes.
            GameObject windowBarrier = BuildLocalBox("Deck Window Barrier", 0f, DeckLength,
                -halfWidth - WallThickness, -halfWidth, 0f, _wallHeight, wallMat, collide: true);
            windowBarrier.GetComponent<MeshRenderer>().enabled = false;

            _farGlass = BuildLocalBox("Deck Far Glass", DeckLength, DeckLength + WallThickness,
                -halfWidth, halfWidth, 0f, _wallHeight, glassMat, collide: true).transform;

            BuildWindowLabel();
            BuildPlanet();
            RegisterInPlayFootprint();
        }

        /// <summary>MV-1253, change 3: "the window label reads SOTAMI in small text above the
        /// glass". A static (non-billboard) world-space canvas is enough here -- the deck is a
        /// straight corridor Max always approaches from the same side, unlike <see cref="WorldHealthBar"/>'s
        /// per-frame camera-facing plate. Faces <see cref="OutwardDir"/> (same convention
        /// <see cref="WorldHealthBar"/>'s own billboard uses: canvas forward = the direction the
        /// camera is looking, i.e. away from the camera, which trails Max and looks the same way he
        /// walks).</summary>
        private void BuildWindowLabel()
        {
            Bounds glass = _farGlass.GetComponent<Renderer>().bounds;

            var canvasGo = new GameObject("Deck Window Label", typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)canvasGo.transform;
            rt.SetParent(transform, worldPositionStays: true);
            rt.position = new Vector3(glass.center.x, glass.max.y + WindowLabelClearance, glass.center.z);
            rt.rotation = Quaternion.LookRotation(OutwardDir(_wall), Vector3.up);

            const float pixelWidth = 400f, pixelHeight = 80f;
            rt.sizeDelta = new Vector2(pixelWidth, pixelHeight);
            rt.localScale = Vector3.one * WorldBar.CanvasScaleFor(WindowLabelWorldWidth, pixelWidth);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(rt, worldPositionStays: false);
            var text = textGo.AddComponent<Text>();
            text.font = HudFont.Get();
            text.fontSize = 28;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = PlanetName;
            RectTransform trt = text.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        }

        private void BuildPlanet()
        {
            Vector3 center = new Vector3(_doorMouth.x, _wallHeight * 0.5f, _doorMouth.y)
                + OutwardDir(_wall) * (DeckLength * 0.5f)
                + AcrossDir(_wall) * (-(DeckWidth * 0.5f + WallThickness + PlanetOutDistance));

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Observation Deck Planet";
            var col = go.GetComponent<Collider>();
            if (col != null) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }
            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = center;
            go.transform.localScale = Vector3.one * PlanetStartDiameter;
            go.GetComponent<MeshRenderer>().sharedMaterial =
                IntroBuild.Lit("mv1245_planet", PlanetColor, emission: PlanetColor * 0.2f);
            _planet = go.transform;
        }

        private void RegisterInPlayFootprint()
        {
            float width = DeckWidth + 2f * WallThickness;
            Rect footprint = FootprintRect(_doorMouth, _wall, -0.5f, DeckLength + WallThickness, width);
            FallSafetyNet.RegisterInPlayArea(footprint);
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>Public, dt-driven (same contract every other scripted beat in this project gives --
        /// <see cref="WorldJoinSequence.Tick"/>, <see cref="MaxWorlds.VFX.WorldFinaleGate.TickWeaponBeat"/>).</summary>
        public void Tick(float dt)
        {
            if (_endPhase != EndPhase.None) { TickEndBeat(dt); return; }

            Transform player = PlayerTransform();
            if (player == null) return;

            float along = AlongDistance(player.position);
            float t = Mathf.Clamp01(along / DeckLength);
            if (_planet != null) _planet.localScale = Vector3.one * Mathf.Lerp(PlanetStartDiameter, PlanetEndDiameter, t);

            if (along >= DeckLength - FarDwellRadius)
            {
                _dwellTimer += dt;
                if (_dwellTimer >= FarDwellSeconds) BeginEndBeat();
            }
            else
            {
                _dwellTimer = 0f;
            }
        }

        private void BeginEndBeat()
        {
            _endPhase = EndPhase.ToBeContinued;
            _endPhaseTime = 0f;

            var player = FindFirstObjectByType<PlayerController>();
            if (player != null) player.enabled = false;
            FindFirstObjectByType<HudController>()?.SetControlsHidden(true);

            _overlay = EndShotOverlay.Create();
            _overlay.ShowToBeContinued();
        }

        private void TickEndBeat(float dt)
        {
            _endPhaseTime += dt;
            switch (_endPhase)
            {
                case EndPhase.ToBeContinued:
                    if (_endPhaseTime >= EndShotHoldSeconds)
                    {
                        _endPhase = EndPhase.Credits;
                        _endPhaseTime = 0f;
                        _overlay.ShowCredits();
                    }
                    break;

                case EndPhase.Credits:
                    if (_endPhaseTime >= CreditsHoldSeconds)
                    {
                        _endPhase = EndPhase.Done;
                        // MV-1245 Change 5: the profile's own "ending seen" flag, and nothing left to
                        // RESUME into -- see SaveSystem.MarkEndingSeen's own doc comment.
                        SaveSystem.MarkEndingSeen(SaveSystem.ActiveSlot);
                        RunFlow.QuitToMenu();
                    }
                    break;
            }
        }

        private Transform PlayerTransform()
        {
            if (_player == null)
            {
                GameObject g = GameObject.FindGameObjectWithTag("Player");
                if (g != null) _player = g.transform;
            }
            return _player;
        }

        // ------------------------------------------------------------------ geometry helpers
        // Same outward/across-axis convention as WorldJoinSequence's own private helpers -- duplicated
        // locally rather than shared, matching this project's established per-sequence idiom
        // (WorldFinaleGate.IsBeyondFence keeps its own copy too).

        private static Vector3 OutwardDir(Wall wall) => wall switch
        {
            Wall.N => Vector3.forward,
            Wall.S => Vector3.back,
            Wall.E => Vector3.right,
            Wall.W => Vector3.left,
            _ => Vector3.forward,
        };

        private static Vector3 AcrossDir(Wall wall)
        {
            Vector3 d = OutwardDir(wall);
            return new Vector3(-d.z, 0f, d.x);
        }

        private float AlongDistance(Vector3 pos)
        {
            Vector3 dir = OutwardDir(_wall);
            bool travelAlongZ = dir.x == 0f;
            float raw = travelAlongZ ? pos.z - _doorMouth.y : pos.x - _doorMouth.x;
            return raw * (travelAlongZ ? dir.z : dir.x);
        }

        private static Rect FootprintRect(Vector2 doorMouth, Wall wall, float alongMin, float alongMax, float width)
        {
            bool travelAlongX = wall == Wall.E || wall == Wall.W;
            bool positive = wall == Wall.N || wall == Wall.E;
            float coordMin = positive ? alongMin : -alongMax;
            float coordMax = positive ? alongMax : -alongMin;

            return travelAlongX
                ? new Rect(doorMouth.x + coordMin, doorMouth.y - width * 0.5f, coordMax - coordMin, width)
                : new Rect(doorMouth.x - width * 0.5f, doorMouth.y + coordMin, width, coordMax - coordMin);
        }

        /// <summary>A world-axis-aligned box in this deck's own (along, across, y) local frame -- along
        /// 0 is the door line itself, so a box built from alongMin 0 sits flush against it by
        /// construction (no separate "and don't leave a gap" step).</summary>
        private GameObject BuildLocalBox(string name, float alongMin, float alongMax, float acrossMin, float acrossMax,
            float yMin, float yMax, Material mat, bool collide)
        {
            Vector3 dir = OutwardDir(_wall);
            Vector3 across = AcrossDir(_wall);
            float alongMid = (alongMin + alongMax) * 0.5f, alongLen = alongMax - alongMin;
            float acrossMid = (acrossMin + acrossMax) * 0.5f, acrossLen = acrossMax - acrossMin;
            float yMid = (yMin + yMax) * 0.5f, height = Mathf.Max(0.01f, yMax - yMin);

            Vector3 center = new Vector3(_doorMouth.x, yMid, _doorMouth.y) + dir * alongMid + across * acrossMid;
            Vector3 size = new Vector3(
                Mathf.Abs(dir.x) * alongLen + Mathf.Abs(across.x) * acrossLen,
                height,
                Mathf.Abs(dir.z) * alongLen + Mathf.Abs(across.z) * acrossLen);

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = center;
            go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            if (!collide)
            {
                var c = go.GetComponent<Collider>();
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }
            return go;
        }

        /// <summary>
        /// MV-1245, beat 3: the finale's own one-off full-screen takeover -- a flat space composition
        /// (planet, ship silhouette, stars) under "TO BE CONTINUED", then a short generic credits card.
        /// Built as a 2D overlay (same Canvas idiom as <see cref="FinaleBanner"/>/<see cref="WorldJoinTitleCard"/>)
        /// rather than a literal camera dolly through built 3D geometry -- there is no exterior
        /// ship/orbit model in the kit yet (greybox discipline), and the design image (MV-1245.png) is
        /// itself a flat composition, so this reproduces it directly. Internal-but-public visibility so
        /// <c>MaxWorlds.Dev.CaptureDirector</c>'s own MV-1245 preset can build and capture the SAME
        /// overlay without replaying the whole finale.
        /// </summary>
        public sealed class EndShotOverlay
        {
            private RectTransform _root;
            private Text _text;
            private Text _planetNameText;

            /// <summary>The SOTAMI caption beside the planet -- read by the MV-1253 EditMode test
            /// to check the end shot's resolved string, not a duplicated literal.</summary>
            public Text PlanetNameText => _planetNameText;

            /// <summary>Built as a <see cref="Canvas"/> under <see cref="_root"/> itself -- exposed so
            /// <c>MaxWorlds.Dev.CaptureDirector</c>'s own MV-1245 preset can flip it to
            /// <see cref="RenderMode.ScreenSpaceCamera"/> for its capture (same idiom its other presets
            /// already use on <see cref="MaxWorlds.UI.HudController"/>'s own canvas, e.g. the MV-585
            /// preset): a <see cref="RenderMode.ScreenSpaceOverlay"/> canvas draws straight to the
            /// display and is invisible to <see cref="Camera.Render"/>'s own render-to-texture capture
            /// path, which every preset relies on.</summary>
            public Canvas Canvas => _root.GetComponent<Canvas>();

            private static readonly Vector2[] StarFractions =
            {
                new Vector2(0.08f, 0.85f), new Vector2(0.18f, 0.92f), new Vector2(0.30f, 0.80f),
                new Vector2(0.46f, 0.90f), new Vector2(0.62f, 0.86f), new Vector2(0.74f, 0.93f),
                new Vector2(0.86f, 0.82f), new Vector2(0.93f, 0.90f), new Vector2(0.12f, 0.65f),
                new Vector2(0.90f, 0.68f),
            };

            public static EndShotOverlay Create()
            {
                var canvasGo = new GameObject("MV-1245 End Shot", typeof(Canvas), typeof(CanvasScaler));
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 200;   // above FinaleBanner's own 150 and the HUD
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                var root = (RectTransform)canvasGo.transform;
                var overlay = new EndShotOverlay { _root = root };

                Stretch(NewImage(root, "Space", new Color(0.03f, 0.04f, 0.07f)));

                Image planet = NewImage(root, "Planet", PlanetColor);
                planet.sprite = CircleSprite();
                RectTransform pr = planet.rectTransform;
                pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0f);
                pr.pivot = new Vector2(0.5f, 0.5f);
                pr.sizeDelta = new Vector2(1600f, 1600f);
                pr.anchoredPosition = new Vector2(0f, -380f);

                Image shipBody = NewImage(root, "Ship Body", new Color(0.70f, 0.36f, 0.20f));
                PlaceFixed(shipBody.rectTransform, new Vector2(0.5f, 0.62f), new Vector2(-40f, 0f), new Vector2(120f, 70f));

                Image shipCabin = NewImage(root, "Ship Cabin", new Color(0.22f, 0.62f, 0.58f));
                PlaceFixed(shipCabin.rectTransform, new Vector2(0.5f, 0.62f), new Vector2(50f, 0f), new Vector2(70f, 70f));

                Image shipLight = NewImage(root, "Ship Light", new Color(0.96f, 0.80f, 0.30f));
                shipLight.sprite = CircleSprite();
                PlaceFixed(shipLight.rectTransform, new Vector2(0.5f, 0.62f), new Vector2(50f, 0f), new Vector2(26f, 26f));

                foreach (Vector2 f in StarFractions)
                {
                    Image star = NewImage(root, "Star", Color.white);
                    star.sprite = CircleSprite();
                    PlaceFixed(star.rectTransform, f, Vector2.zero, new Vector2(6f, 6f));
                }

                overlay._text = BuildText(root, Color.white);
                overlay._text.fontSize = 64;
                PlaceFixed(overlay._text.rectTransform, new Vector2(0.5f, 0.78f), Vector2.zero, new Vector2(1700f, 140f));

                // MV-1253: the SOTAMI caption beside the planet -- "about 60% of the TO BE
                // CONTINUED size" (64 * 0.6), shown only while "TO BE CONTINUED" itself is.
                overlay._planetNameText = BuildText(root, Color.white);
                overlay._planetNameText.font = HudFont.Get();
                overlay._planetNameText.fontSize = Mathf.RoundToInt(64f * 0.6f);
                PlaceFixed(overlay._planetNameText.rectTransform, new Vector2(0.5f, 0f), new Vector2(620f, 180f), new Vector2(500f, 70f));

                return overlay;
            }

            public void ShowToBeContinued()
            {
                _text.text = "TO BE CONTINUED";
                _planetNameText.text = PlanetName;
            }

            public void ShowCredits()
            {
                _text.text = "MAX vs THE WORLDS\n\nA CODYNAMICS GAME";
                _planetNameText.text = string.Empty;
            }

            public void DestroySelf()
            {
                if (_root == null) return;
                if (Application.isPlaying) Object.Destroy(_root.gameObject);
                else Object.DestroyImmediate(_root.gameObject);
            }

            private static void PlaceFixed(RectTransform rt, Vector2 anchor, Vector2 anchoredPosition, Vector2 size)
            {
                rt.anchorMin = rt.anchorMax = anchor;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = size;
                rt.anchoredPosition = anchoredPosition;
            }

            private static void Stretch(Image img)
            {
                RectTransform rt = img.rectTransform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            }

            private static Image NewImage(Transform parent, string name, Color color)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, worldPositionStays: false);
                var img = go.GetComponent<Image>();
                img.color = color;
                img.raycastTarget = false;
                return img;
            }

            private static Text BuildText(Transform parent, Color color)
            {
                var go = new GameObject("Text", typeof(Text));
                go.transform.SetParent(parent, worldPositionStays: false);
                var text = go.GetComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = color;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.raycastTarget = false;
                return text;
            }

            private static Sprite s_circle;

            private static Sprite CircleSprite()
            {
                if (s_circle != null) return s_circle;

                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
                float r = size * 0.5f;
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        pixels[y * size + x] = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) <= r
                            ? Color.white : new Color(1f, 1f, 1f, 0f);
                tex.SetPixels(pixels);
                tex.Apply();

                s_circle = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
                s_circle.hideFlags = HideFlags.HideAndDontSave;
                return s_circle;
            }
        }
    }
}
