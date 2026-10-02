using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1068 (the one new test): <c>BuildTrapButton</c> anchored the TRAP button to the canvas's
    /// bottom-left corner but positioned it at <c>(TrapButtonX, TrapButtonRise) = (-360, 820)</c> — the
    /// doc comment claimed this mirrored the Sentinel joystick, but the Sentinel joystick is anchored
    /// bottom-CENTRE, not bottom-left, so from the bottom-left corner X=-360 sits 360 units left of the
    /// screen edge: the button existed, was active, and was entirely off-screen. Fixed by moving it
    /// beside the Force Field button in the same bottom-left-anchored left play-area column (X=330,
    /// Rise=357, same size as FIELD at X=150).
    ///
    /// Fails on 0a89571 (the commit the ticket's own observation reads code from): TrapButtonX/Rise are
    /// still (-360, 820) there, so AC1's on-screen assertion fails at every
    /// <see cref="RigBoardLayout.CaptureAspects"/> entry (quoted in the fix comment).
    ///
    /// One consolidated test (MV-465 Rule 1): AC1 (the TRAP button's resolved rect lies fully inside the
    /// safe area and never overlaps any other active HUD control, at every captureAspects entry
    /// including the phone one) plus AC2 (the button's own onClick actually drops a trap at Max's
    /// position) — both through real entry points: <see cref="HomeScreen.StartSlotWorld"/> (the DEV
    /// WORLD 3 path the ticket's own context note refers to, MV-736) to reach a fully maxed World 3 rig,
    /// and the TRAP button's real <see cref="Button"/> component, not a direct
    /// <see cref="PlayerAbilities.TryDropTrap"/> call (already covered by MV1035TrapAbilityTests).
    /// </summary>
    public sealed class MV1068TrapButtonPositionTests
    {
        // Mirrors HudController's own private CanvasScaler reference (RefW/RefH) and
        // MV676HudPhoneAspectMarginTests' replication of CanvasScaler.ScaleWithScreenSize's log-blend
        // (matchWidthOrHeight=0.5) — the effective canvas a given real resolution resolves to.
        private const float RefW = 1920f, RefH = 1080f;

        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1068-tests");
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
        public void TrapButtonStaysOnScreenNeverOverlapsAndDropsATrap_MV1068()
        {
            // World 3, every node (including p_trp) maxed — HomeScreen's own DEV WORLD 3 entry point
            // (MV-736), the real path the ticket's own "DEV WORLD 3" context note refers to.
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER", WorldIndex = 0 });
            HomeScreen.StartSlotWorld(0, worldIndex: 2, maxRig: true);

            GameObject maxGo = null, hudGo = null;
            Object droppedTrapGo = null;
            try
            {
                maxGo = new GameObject("MV-1068 test Max", typeof(CharacterController)) { tag = "Player" };
                var playerHealth = maxGo.AddComponent<PlayerHealth>();
                playerHealth.Initialize();
                var abilities = maxGo.AddComponent<PlayerAbilities>();
                maxGo.transform.position = new Vector3(5f, 0f, -3f);

                hudGo = new GameObject("HUD");
                var hud = hudGo.AddComponent<HudController>();
                InvokeLifecycle(hud, "Awake");
                InvokeLifecycle(hud, "OnEnable");

                var trap = FindRect(hudGo, "Trap Button");
                var move = FindRect(hudGo, "Move Joystick");
                var aim = FindRect(hudGo, "Aim Joystick");
                var field = FindRect(hudGo, "Force Field Button");
                var sentinel = FindRect(hudGo, "Sentinel Joystick");
                var focus = FindRect(hudGo, "Sentinel Focus Toggle");
                var map = FindRect(hudGo, "Map Button");
                var teleport = FindRect(hudGo, "Teleport Joystick");
                var rig = FindRect(hudGo, "Weapons Tap Target");
                var boss = FindRect(hudGo, "Boss Bar");

                Assert.That(trap, Is.Not.Null, "fixture: the TRAP button must exist in the HUD tree");
                Assert.That(trap.gameObject.activeInHierarchy, Is.True, "the TRAP button must be visible once p_trp is owned");
                Assert.That(move, Is.Not.Null, "fixture: the move stick must exist");
                Assert.That(aim, Is.Not.Null, "fixture: the aim stick must exist");
                Assert.That(field, Is.Not.Null, "fixture: the FIELD button must exist");
                Assert.That(field.gameObject.activeInHierarchy, Is.True, "fixture: FIELD must be active under a maxed World 3 rig");
                Assert.That(sentinel, Is.Not.Null, "fixture: the Sentinel joystick must exist");
                Assert.That(sentinel.gameObject.activeInHierarchy, Is.True, "fixture: Sentinel must be active under a maxed World 3 rig");
                Assert.That(focus, Is.Not.Null, "fixture: the FOCUS toggle must exist");
                Assert.That(focus.gameObject.activeInHierarchy, Is.True, "fixture: FOCUS must be active under a maxed World 3 rig");
                Assert.That(map, Is.Not.Null, "fixture: the MAP button must exist");
                Assert.That(teleport, Is.Not.Null, "fixture: the Teleport joystick must exist");
                Assert.That(teleport.gameObject.activeInHierarchy, Is.True, "fixture: Teleport must be active under a maxed World 3 rig");
                Assert.That(rig, Is.Not.Null, "fixture: the RIG tap target must exist");
                Assert.That(boss, Is.Not.Null, "fixture: the boss bar must exist");
                // The boss bar only shows mid-encounter; force it on so this check covers the real worst
                // case (a trap dropped during a boss fight) instead of a vacuous skip over an inactive rect.
                boss.gameObject.SetActive(true);

                var others = new (string id, RectTransform rt, float pad)[]
                {
                    ("MoveStick", move, 30f),
                    ("AimStick", aim, 30f),
                    ("FIELD", field, 0f),
                    ("Sentinel", sentinel, 0f),
                    ("FOCUS", focus, 0f),
                    ("MAP", map, 0f),
                    ("Teleport", teleport, 0f),
                    ("RIG", rig, 0f),
                    ("BossBar", boss, 0f),
                };

                // ---------------------------------------------------------------- AC1: on-screen + no overlap,
                // at every aspect the board's own captureAspects list names (MV-463), phone included.
                foreach (var aspect in RigBoardLayout.CaptureAspects)
                {
                    float logWidth = Mathf.Log(aspect.W / RefW, 2f);
                    float logHeight = Mathf.Log(aspect.H / RefH, 2f);
                    float scaleFactor = Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, 0.5f));
                    float effectiveWidth = aspect.W / scaleFactor;
                    float effectiveHeight = aspect.H / scaleFactor;

                    Camera cam = ConfigureCanvasForCapture(hudGo, effectiveWidth, effectiveHeight, out RenderTexture rt);
                    try
                    {
                        Rect trapRect = ScreenRect(trap, cam, 0f);

                        Assert.That(trapRect.xMin, Is.GreaterThanOrEqualTo(-1f), $"TRAP crops past the safe area's left edge at {aspect.Name}");
                        Assert.That(trapRect.xMax, Is.LessThanOrEqualTo(effectiveWidth + 1f), $"TRAP crops past the safe area's right edge at {aspect.Name}");
                        Assert.That(trapRect.yMin, Is.GreaterThanOrEqualTo(-1f), $"TRAP crops past the safe area's bottom edge at {aspect.Name}");
                        Assert.That(trapRect.yMax, Is.LessThanOrEqualTo(effectiveHeight + 1f), $"TRAP crops past the safe area's top edge at {aspect.Name}");

                        foreach (var (id, otherRt, pad) in others)
                        {
                            Rect otherRect = ScreenRect(otherRt, cam, pad);
                            Assert.That(trapRect.Overlaps(otherRect), Is.False,
                                $"TRAP {trapRect} overlaps '{id}' {otherRect} at {aspect.Name}");
                        }
                    }
                    finally
                    {
                        Object.DestroyImmediate(cam.gameObject);
                        rt.Release();
                        Object.DestroyImmediate(rt);
                    }
                }

                // ---------------------------------------------------------------- AC2: tapping the real button
                // drops a trap at Max's own position.
                Assert.That(abilities.ActiveTrap, Is.Null, "fixture: no trap must be active before the tap");
                var button = trap.GetComponentInChildren<Button>(true);
                Assert.That(button, Is.Not.Null, "fixture: the TRAP button must carry a Button component");
                button.onClick.Invoke();
                Assert.That(abilities.ActiveTrap, Is.Not.Null, "tapping the TRAP button must drop a trap");
                droppedTrapGo = abilities.ActiveTrap.gameObject;
                Assert.That(Vector3.Distance(abilities.ActiveTrap.transform.position, maxGo.transform.position), Is.LessThan(0.001f),
                    "the dropped trap must sit at Max's own position");
            }
            finally
            {
                // MV645HudLeftColumnTests' own precedent: OnEnable subscribed HudController to
                // WeaponSystemState.Changed by reflection, so OnDisable must unsubscribe the same way
                // before destroying it.
                if (hudGo != null) InvokeLifecycle(hudGo.GetComponent<HudController>(), "OnDisable");
                if (droppedTrapGo != null) Object.DestroyImmediate(droppedTrapGo);
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (hudGo != null) Object.DestroyImmediate(hudGo);
                WeaponSystemState.Reset();
                RigState.Reset();
                RigFusionState.Reset();
                PickupWallet.Reset();
            }
        }

        // ---------------------------------------------------------------- helpers (mirrors MV606HudReshuffleTests/
        // MV676HudPhoneAspectMarginTests)

        private static Camera ConfigureCanvasForCapture(GameObject hudGo, float width, float height, out RenderTexture rt)
        {
            var canvas = hudGo.GetComponentInChildren<Canvas>();
            var scaler = hudGo.GetComponentInChildren<CanvasScaler>();
            scaler.enabled = false;
            canvas.scaleFactor = 1f;

            int w = Mathf.RoundToInt(width);
            int h = Mathf.RoundToInt(height);
            var camGo = new GameObject("MV1068 Capture Cam", typeof(Camera));
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
