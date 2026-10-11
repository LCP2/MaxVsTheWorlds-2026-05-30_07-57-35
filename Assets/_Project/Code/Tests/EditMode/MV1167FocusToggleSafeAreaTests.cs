using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1167 (the one new test, MV-465 Rule 1) — Lee, 11 Oct, on an iPhone Pro Max: the FOCUS
    /// pill was sliced off the top of the screen. Measured cause: <see cref="HudController.BuildFocusToggle"/>
    /// placed the pill at centre SentinelButtonRise + FocusToggleRise = 720 + 210 = 930, checked only
    /// against the 1080 reference and the phone-aspect-blended ~978-tall canvas
    /// (<see cref="MV676HudPhoneAspectMarginTests"/>) — neither check applied the real
    /// <see cref="SafeArea"/> inset the HUD actually renders inside, which shrinks the usable area
    /// further on every notched iPhone. This test applies the real per-device landscape SafeArea
    /// (Apple's published safe-area insets, converted to each device's native pixel scale) at the four
    /// device canvases the ticket names, and asserts every visible named HUD control's rect sits inside
    /// that safe area with &gt;=12 units to spare.
    ///
    /// Covers MV676HudPhoneAspectMarginTests' own four elements (Focus Toggle, Force Field, Water
    /// Balloon, MAP) plus Sentinel, Teleport, the Weapons/RIG mark and its two counters (MV-1167 AC2).
    /// MV676 is updated (not deleted/replaced) to apply the real SafeArea too, at its own single
    /// device/margin — kept alongside rather than removed because deleting it was found, while
    /// shaping this ticket, to trip an unrelated pre-existing test-order fragility in a different test
    /// class (filed separately as MV-1172); the two tests' coverage overlaps but neither subsumes the
    /// other's exact assertions.
    ///
    /// Fails on base commit 093e1b7 (the worker clone's HEAD when this ticket was picked up; quoted in
    /// the fix comment): the FOCUS pill's rect runs outside the safe area on the 16 Pro Max / 16-15 /
    /// 13 mini canvases. Sole guard on this fix; do not cull (MV-465).
    ///
    /// Safe-area insets used (Apple's published per-device landscape safe area, in points, converted to
    /// each device's own native pixel scale — left/right applied symmetrically since the notch/Dynamic
    /// Island can land on either edge depending on which way the device is rotated):
    ///   16 Pro Max (2868x1320, 3x): left/right 59pt (177px), bottom 21pt (63px), top 0
    ///   16 / 15    (2556x1179, 3x): left/right 47pt (141px), bottom 21pt (63px), top 0
    ///   13 mini    (2340x1080, 3x): left/right 50pt (150px), bottom 21pt (63px), top 0
    ///   SE         (1334x750,  2x): home-button device, no notch/home-indicator inset — 0 all sides
    /// </summary>
    public sealed class MV1167FocusToggleSafeAreaTests
    {
        private const float RefW = 1920f, RefH = 1080f;
        private const float MinMargin = 12f;

        private struct DeviceScreen
        {
            public string Name;
            public float W, H;
            public float InsetLeftPx, InsetRightPx, InsetBottomPx, InsetTopPx;
        }

        private static readonly DeviceScreen[] Devices =
        {
            new DeviceScreen { Name = "16 Pro Max 2868x1320", W = 2868f, H = 1320f, InsetLeftPx = 177f, InsetRightPx = 177f, InsetBottomPx = 63f, InsetTopPx = 0f },
            new DeviceScreen { Name = "16/15 2556x1179",      W = 2556f, H = 1179f, InsetLeftPx = 141f, InsetRightPx = 141f, InsetBottomPx = 63f, InsetTopPx = 0f },
            new DeviceScreen { Name = "13 mini 2340x1080",    W = 2340f, H = 1080f, InsetLeftPx = 150f, InsetRightPx = 150f, InsetBottomPx = 63f, InsetTopPx = 0f },
            new DeviceScreen { Name = "SE 1334x750",          W = 1334f, H = 750f,  InsetLeftPx = 0f,   InsetRightPx = 0f,   InsetBottomPx = 0f,  InsetTopPx = 0f },
        };

        private static readonly string[] ControlNames =
        {
            "Sentinel Button", "Sentinel Focus Toggle", "Water Balloon Joystick",
            "Teleport Joystick", "Force Field Button", "Map Button",
            "Weapons Button", "Parts", "Power Cells",
        };

        [Test]
        public void EveryHudControlClearsTheSafeAreaOnEveryPhoneCanvas()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();

            // Every control the ticket names visible at once: Sentinel acquired with Move level 1
            // (so FOCUS shows), Water Balloon at its top level, Teleport and Force Field acquired —
            // same RestoreSnapshot shortcut MV676's/MV1153's own fixtures use.
            RigState.RestoreSnapshot(new Dictionary<string, int>
            {
                { "u_sen", 1 },
                { "u_mov", 1 },
                { "s_bal", 1 },
                { "s_lob", WeaponCatalog.MaxLevel(WaterBalloonTrackKind.Range) },
                { "m_tp", 1 },
                { "e_ff", 1 },
            }, System.Array.Empty<string>());
            WeaponSystemState.RebuildAcquiredFromRigState();

            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");
            // OnEnable just subscribed OnAbilitiesChanged — fire it again so Force Field/Teleport/the
            // Focus toggle and the balloon's top-level rebuild pick up the snapshot above.
            WeaponSystemState.RebuildAcquiredFromRigState();

            try
            {
                var safeRoot = FindRect(hudGo, "Safe Area");
                Assert.That(safeRoot, Is.Not.Null, "fixture: the Safe Area root must exist");

                var controls = new Dictionary<string, RectTransform>();
                foreach (var name in ControlNames)
                {
                    var rt = FindRect(hudGo, name);
                    Assert.That(rt, Is.Not.Null, $"fixture: '{name}' must exist");
                    Assert.That(rt.gameObject.activeInHierarchy, Is.True, $"fixture: '{name}' must be visible");
                    controls[name] = rt;
                }

                foreach (var device in Devices)
                {
                    var safeAreaPx = new Rect(
                        device.InsetLeftPx, device.InsetBottomPx,
                        device.W - device.InsetLeftPx - device.InsetRightPx,
                        device.H - device.InsetBottomPx - device.InsetTopPx);
                    SafeArea.ComputeAnchors(safeAreaPx, device.W, device.H, out Vector2 anchorMin, out Vector2 anchorMax);
                    safeRoot.anchorMin = anchorMin;
                    safeRoot.anchorMax = anchorMax;
                    safeRoot.offsetMin = Vector2.zero;
                    safeRoot.offsetMax = Vector2.zero;

                    // Replicates CanvasScaler.ScaleWithScreenSize's own log-blend (matchWidthOrHeight=0.5),
                    // same idiom MV676/MV1153 use, over this device's own native pixel resolution.
                    float logWidth = Mathf.Log(device.W / RefW, 2f);
                    float logHeight = Mathf.Log(device.H / RefH, 2f);
                    float scaleFactor = Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, 0.5f));
                    float effectiveWidth = device.W / scaleFactor;
                    float effectiveHeight = device.H / scaleFactor;

                    var safeRect = new Rect(
                        anchorMin.x * effectiveWidth, anchorMin.y * effectiveHeight,
                        (anchorMax.x - anchorMin.x) * effectiveWidth,
                        (anchorMax.y - anchorMin.y) * effectiveHeight);

                    var cam = ConfigureCanvasForCapture(hudGo.GetComponentInChildren<Canvas>(),
                        hudGo.GetComponentInChildren<CanvasScaler>(), effectiveWidth, effectiveHeight, out RenderTexture rt);
                    try
                    {
                        foreach (var name in ControlNames)
                        {
                            Rect r = ScreenRect(controls[name], cam);
                            Assert.That(r.xMin, Is.GreaterThanOrEqualTo(safeRect.xMin + MinMargin),
                                $"[{device.Name}] '{name}' left {r.xMin:F1} must clear the safe area's left {safeRect.xMin:F1} by {MinMargin}");
                            Assert.That(r.xMax, Is.LessThanOrEqualTo(safeRect.xMax - MinMargin),
                                $"[{device.Name}] '{name}' right {r.xMax:F1} must clear the safe area's right {safeRect.xMax:F1} by {MinMargin}");
                            Assert.That(r.yMin, Is.GreaterThanOrEqualTo(safeRect.yMin + MinMargin),
                                $"[{device.Name}] '{name}' bottom {r.yMin:F1} must clear the safe area's bottom {safeRect.yMin:F1} by {MinMargin}");
                            Assert.That(r.yMax, Is.LessThanOrEqualTo(safeRect.yMax - MinMargin),
                                $"[{device.Name}] '{name}' top {r.yMax:F1} must clear the safe area's top {safeRect.yMax:F1} by {MinMargin}");
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
                InvokeLifecycle(hud, "OnDisable");
                Object.DestroyImmediate(hudGo);
                WeaponSystemState.Reset();
                RigState.Reset();
                RigFusionState.Reset();
                PickupWallet.Reset();
            }
        }

        private static Camera ConfigureCanvasForCapture(Canvas canvas, CanvasScaler scaler, float width, float height, out RenderTexture rt)
        {
            scaler.enabled = false;
            canvas.scaleFactor = 1f;

            var camGo = new GameObject("MV1167 Capture Cam", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            int w = Mathf.RoundToInt(width);
            int h = Mathf.RoundToInt(height);
            rt = new RenderTexture(w, h, 16);
            cam.targetTexture = rt;
            cam.aspect = width / height;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            return cam;
        }

        private static Rect ScreenRect(RectTransform rt, Camera cam)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, c[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
            return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
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
