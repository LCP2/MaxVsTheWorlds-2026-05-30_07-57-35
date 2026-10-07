using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1104 (the one new test, MV-465 Rule 1) — Lee's device screenshot showed every circular
    /// on-screen control (move stick, aim stick, every ability joystick, FIELD/CAPTURE) at its
    /// pre-ticket size: move/aim rings 200x200, FIELD/CAPTURE 110x110, every <see cref="AbilityControlArt.BuildJoystick"/>
    /// joystick maxing out at 200x200, <see cref="AbilityJoystickControlBase.DragRadiusPixels"/> 90,
    /// <see cref="AbilityJoystickControlBase.KnobRadiusPixels"/> 26. Fails on base commit 1d8d937 (main's
    /// tip when this ticket was picked up), where none of these are scaled, at AC1's growth check
    /// (quoted in the fix comment).
    ///
    /// One consolidated test carrying every AC sub-check: (1) every control's resolved diameter grows to
    /// at least 1.29x its base-commit diameter at 2556x1179 / 2340x1080 / 1334x750 under a maxed World 3
    /// rig (Sentinel, Teleport, FIELD, CAPTURE all present — <see cref="HomeScreen.StartSlotWorld"/>'s own
    /// maxRig path, MV-736, the same real entry point MV1068TrapButtonPositionTests uses — measured: World
    /// 3's own SecondaryKind morphs to ShoulderRack, an auto-fire turret with no joystick of its own, and
    /// the Water Balloon ability is never acquired on this world's board, so there is no Shoulder Rack
    /// circular control to check); (2) pairwise non-overlap with a 12px gap between every interactive rect
    /// (each control's real touch-sensitive area, including the invisible OnScreenStick/joystick touch pad
    /// where one exists); (3) every rect lies inside the simulated safe area at every aspect; (4) the
    /// Teleport joystick's real <see cref="UnityEngine.EventSystems.IPointerDownHandler"/>/
    /// <see cref="UnityEngine.EventSystems.IDragHandler"/> entry points resolve a bigger drag radius and
    /// knob travel than the base commit's 90/26 — driven through the control's own public pointer
    /// handlers, not a reflected private field.
    /// </summary>
    public sealed class MV1104ControlSizeTests
    {
        private const float RefW = 1920f, RefH = 1080f;
        private const float MinGrowth = 1.29f;
        private const float MinGapPx = 12f;

        private static readonly (string name, float w, float h)[] Aspects =
        {
            ("iPhone 16 Pro landscape", 2556f, 1179f),
            ("iPhone 12 mini landscape", 2340f, 1080f),
            ("iPhone SE2 landscape", 1334f, 750f),
        };

        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1104-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            WeaponSystemState.Reset();
            RigBoard.ResetForTests();
            RigFusionState.Reset();
            PickupWallet.Reset();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void CircularControlsGrow1_3xStayOnScreenAndNeverOverlap_MV1104()
        {
            // World 3, every node (including p_trp) maxed — same real entry point MV1068's own fixture
            // uses (HomeScreen's DEV WORLD 3 path, MV-736).
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER", WorldIndex = 0 });
            HomeScreen.StartSlotWorld(0, worldIndex: 2, maxRig: true);

            GameObject maxGo = null, hudGo = null;
            try
            {
                maxGo = new GameObject("MV-1104 test Max", typeof(CharacterController)) { tag = "Player" };
                var playerHealth = maxGo.AddComponent<PlayerHealth>();
                playerHealth.Initialize();
                maxGo.AddComponent<PlayerAbilities>();
                maxGo.transform.position = new Vector3(5f, 0f, -3f);

                hudGo = new GameObject("HUD");
                var hud = hudGo.AddComponent<HudController>();
                InvokeLifecycle(hud, "Awake");
                InvokeLifecycle(hud, "OnEnable");

                var move = FindRect(hudGo, "Move Joystick");
                var aim = FindRect(hudGo, "Aim Joystick");
                var field = FindRect(hudGo, "Force Field Button");
                var trap = FindRect(hudGo, "Trap Button");
                var sentinel = FindRect(hudGo, "Sentinel Button"); // MV-1113: retired joystick -> button
                var teleport = FindRect(hudGo, "Teleport Joystick");
                var focus = FindRect(hudGo, "Sentinel Focus Toggle");
                var map = FindRect(hudGo, "Map Button");
                var rig = FindRect(hudGo, "Weapons Tap Target");
                var boss = FindRect(hudGo, "Boss Bar");

                Assert.That(move, Is.Not.Null, "fixture: the move stick must exist");
                Assert.That(aim, Is.Not.Null, "fixture: the aim stick must exist");
                Assert.That(field, Is.Not.Null, "fixture: the FIELD button must exist");
                Assert.That(field.gameObject.activeInHierarchy, Is.True, "fixture: FIELD must be active under a maxed World 3 rig");
                Assert.That(trap, Is.Not.Null, "fixture: the CAPTURE button must exist");
                Assert.That(trap.gameObject.activeInHierarchy, Is.True, "fixture: CAPTURE must be active under a maxed World 3 rig");
                Assert.That(sentinel, Is.Not.Null, "fixture: the SENTINEL button must exist");
                Assert.That(sentinel.gameObject.activeInHierarchy, Is.True, "fixture: Sentinel must be active under a maxed World 3 rig");
                Assert.That(teleport, Is.Not.Null, "fixture: the Teleport joystick must exist");
                Assert.That(teleport.gameObject.activeInHierarchy, Is.True, "fixture: Teleport must be active under a maxed World 3 rig");
                // MV-1104 measured: under World 3 (worldIndex 2), WeaponSystemState's SecondaryKind morphs
                // to ShoulderRack (an auto-fire turret with no joystick of its own, not a relabelled
                // Water Balloon joystick) and the Water Balloon ability itself is not acquired on this
                // world's board, so "Water Balloon Joystick" never builds active here — there is no
                // Shoulder Rack circular control to size-check.
                Assert.That(focus, Is.Not.Null, "fixture: the FOCUS toggle must exist");
                Assert.That(map, Is.Not.Null, "fixture: the MAP button must exist");
                Assert.That(rig, Is.Not.Null, "fixture: the RIG tap target must exist");
                Assert.That(boss, Is.Not.Null, "fixture: the boss bar must exist");
                // Boss bar only shows mid-encounter; force it on so overlap covers the real worst case.
                boss.gameObject.SetActive(true);

                // ---------------------------------------------------------------- AC: resolved diameter
                // grows to >= 1.29x its base-commit (d30d293) diameter. Reference-space sizeDelta is
                // aspect-independent (CanvasScaler scales every element by the same factor), so a single
                // measurement covers every aspect.
                var growth = new (string id, RectTransform rt, float baseDiameter)[]
                {
                    ("Move", move, 200f),
                    ("Aim", aim, 200f),
                    ("FIELD", field, 110f),
                    ("CAPTURE", trap, 110f),
                    // MV-1113: Sentinel moved from a 200px joystick to a 110px round button, same size
                    // as FIELD/CAPTURE — the ticket's own "same size as the FIELD button" instruction.
                    ("Sentinel", sentinel, 110f),
                    ("Teleport", teleport, 200f),
                };
                foreach (var (id, rt, baseDiameter) in growth)
                {
                    float resolved = rt.rect.width;
                    Assert.That(resolved, Is.GreaterThanOrEqualTo(baseDiameter * MinGrowth),
                        $"{id} resolved diameter {resolved:F1} must be at least {MinGrowth}x its base diameter {baseDiameter}");
                }

                // ---------------------------------------------------------------- AC: drag radius / knob
                // travel scale by the same factor — driven through the Teleport joystick's own real
                // IPointerDownHandler/IDragHandler entry points, not a reflected private field.
                var teleportControl = teleport.GetComponentInChildren<TeleportJoystickControl>(true);
                Assert.That(teleportControl, Is.Not.Null, "fixture: the Teleport joystick must carry its control");
                var knob = FindRect(teleport.gameObject, "Knob");
                Assert.That(knob, Is.Not.Null, "fixture: the joystick knob must exist");

                var pressPos = new Vector2(500f, 500f);
                teleportControl.OnPointerDown(new PointerEventData(EventSystem.current) { position = pressPos });
                // A drag past the base commit's own 90px radius (but short of the real, scaled radius)
                // must NOT read as full deflection any more — proves the radius itself grew, not just the
                // knob's travel distance at some fixed fraction.
                teleportControl.OnDrag(new PointerEventData(EventSystem.current) { position = pressPos + new Vector2(100f, 0f) });
                float partialTravel = knob.anchoredPosition.magnitude;
                Assert.That(partialTravel, Is.LessThan(26f * MinGrowth),
                    $"a 100px drag (past the base commit's 90px radius) resolved {partialTravel:F1}px of knob travel — " +
                    "the drag radius must have grown past 90px, not stayed there");

                teleportControl.OnDrag(new PointerEventData(EventSystem.current)
                {
                    position = pressPos + new Vector2(AbilityJoystickControlBase.DragRadiusPixels, 0f)
                });
                float fullTravel = knob.anchoredPosition.magnitude;
                Assert.That(fullTravel, Is.GreaterThanOrEqualTo(26f * MinGrowth),
                    $"a full-deflection drag resolved only {fullTravel:F1}px of knob travel — " +
                    $"must be at least {26f * MinGrowth:F1}px (1.29x the base commit's 26px)");

                // ---------------------------------------------------------------- AC: on-screen + no
                // overlap, at every supported aspect.
                // 30px is the base commit's own OnScreenStick/joystick touch-pad margin (AddOnScreenStick,
                // Water Balloon/Teleport/Sentinel "Touch" pads); 1.3x is the ticket's shared scale factor
                // (literal here, not read off AbilityControlArt.ControlSizeScale, so this test still
                // compiles — and fails on behaviour, not a missing symbol — against the pre-fix base commit).
                const float scaledTouchPad = 30f * 1.3f;
                var others = new (string id, RectTransform rt, float touchPad)[]
                {
                    ("MoveStick", move, scaledTouchPad),
                    ("AimStick", aim, scaledTouchPad),
                    ("FIELD", field, 0f),
                    ("CAPTURE", trap, 0f),
                    ("Sentinel", sentinel, scaledTouchPad),
                    ("Teleport", teleport, scaledTouchPad),
                    ("FOCUS", focus, 0f),
                    ("MAP", map, 0f),
                    ("RIG", rig, 0f),
                    ("BossBar", boss, 0f),
                };

                foreach (var (name, w, h) in Aspects)
                {
                    float logWidth = Mathf.Log(w / RefW, 2f);
                    float logHeight = Mathf.Log(h / RefH, 2f);
                    float scaleFactor = Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, 0.5f));
                    float effectiveWidth = w / scaleFactor;
                    float effectiveHeight = h / scaleFactor;

                    Camera cam = ConfigureCanvasForCapture(hudGo, effectiveWidth, effectiveHeight, out RenderTexture rt);
                    try
                    {
                        var rects = new (string id, Rect rect)[others.Length];
                        for (int i = 0; i < others.Length; i++)
                        {
                            var (id, element, pad) = others[i];
                            rects[i] = (id, ScreenRect(element, cam, pad));
                        }

                        foreach (var (id, r) in rects)
                        {
                            Assert.That(r.xMin, Is.GreaterThanOrEqualTo(-1f), $"{id} crops past the safe area's left edge at {name}");
                            Assert.That(r.xMax, Is.LessThanOrEqualTo(effectiveWidth + 1f), $"{id} crops past the safe area's right edge at {name}");
                            Assert.That(r.yMin, Is.GreaterThanOrEqualTo(-1f), $"{id} crops past the safe area's bottom edge at {name}");
                            Assert.That(r.yMax, Is.LessThanOrEqualTo(effectiveHeight + 1f), $"{id} crops past the safe area's top edge at {name}");
                        }

                        for (int i = 0; i < rects.Length; i++)
                        {
                            for (int j = i + 1; j < rects.Length; j++)
                            {
                                Rect a = Inflate(rects[i].rect, MinGapPx * 0.5f);
                                Rect b = Inflate(rects[j].rect, MinGapPx * 0.5f);
                                Assert.That(a.Overlaps(b), Is.False,
                                    $"{rects[i].id} {rects[i].rect} and {rects[j].id} {rects[j].rect} are closer than the required " +
                                    $"{MinGapPx}px gap at {name}");
                            }
                        }

                    }
                    finally
                    {
                        Object.DestroyImmediate(cam.gameObject);
                        rt.Release();
                        Object.DestroyImmediate(rt);
                    }
                }
            }
            finally
            {
                if (hudGo != null) InvokeLifecycle(hudGo.GetComponent<HudController>(), "OnDisable");
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (hudGo != null) Object.DestroyImmediate(hudGo);
                WeaponSystemState.Reset();
                RigState.Reset();
                RigFusionState.Reset();
                PickupWallet.Reset();
            }
        }

        // ---------------------------------------------------------------- helpers (mirrors MV1068TrapButtonPositionTests/
        // MV676HudPhoneAspectMarginTests)

        private static Rect Inflate(Rect r, float by) => new Rect(r.x - by, r.y - by, r.width + 2f * by, r.height + 2f * by);

        private static Camera ConfigureCanvasForCapture(GameObject hudGo, float width, float height, out RenderTexture rt)
        {
            var canvas = hudGo.GetComponentInChildren<Canvas>();
            var scaler = hudGo.GetComponentInChildren<CanvasScaler>();
            scaler.enabled = false;
            canvas.scaleFactor = 1f;

            int w = Mathf.RoundToInt(width);
            int h = Mathf.RoundToInt(height);
            var camGo = new GameObject("MV1104 Capture Cam", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            rt = new RenderTexture(w, h, 16);
            cam.targetTexture = rt;
            cam.aspect = width / height;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            return cam;
        }

        private static Rect ScreenRect(RectTransform rt, Camera cam, float pad)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, c[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
            return new Rect(min.x - pad, min.y - pad, (max.x - min.x) + pad * 2f, (max.y - min.y) + pad * 2f);
        }

        private static RectTransform FindRect(GameObject go, string name)
        {
            foreach (var t in go.GetComponentsInChildren<RectTransform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static void InvokeLifecycle(Object component, string methodName)
        {
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
        }
    }
}
