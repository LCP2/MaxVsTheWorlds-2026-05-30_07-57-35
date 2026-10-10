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
    /// MV-1153 (the one new test, MV-465 Rule 1) — Lee, on his iPhone in World 1: the Water Balloon
    /// joystick's ring ran into the MAP button. Measured cause: the joystick is bottom-anchored
    /// (<see cref="HudController"/>'s old <c>WaterBalloonJoystickRise</c> 678, old ring half 130 at its
    /// top level) while MAP is vertical-MID-anchored (<c>MapButtonRise</c> 354, size 120) — on the
    /// iPhone's CanvasScaler-blended effective canvas (~978 reference units tall, far short of the
    /// 1080x1920 reference MV-1104 checked the old gap against), MAP's bottom edge drops to ~783 and
    /// the joystick's old ring top (808) overlapped it by ~25 units. Fails on base commit 7224f31
    /// (quoted in the fix comment) where the balloon ring's rect overlaps MAP's rect on the phone
    /// aspect. Sole guard on this fix; do not cull (MV-465).
    ///
    /// Reuses MV676HudPhoneAspectMarginTests'/MV1104ControlSizeTests' own ScreenSpaceCamera capture
    /// idiom at the ticket's own two canvases: the iPhone's native 2556x1179 (same effective ~978-tall
    /// reference canvas as MV676's 852x393 points — CanvasScaler's log-blend is scale-invariant under a
    /// uniform resize) and the 1920x1080 reference MV-1104 originally checked.
    /// </summary>
    public sealed class MV1153WaterBalloonMapClearanceTests
    {
        private const float RefW = 1920f, RefH = 1080f;
        private const float MinGap = 12f;

        private static readonly (string name, float w, float h)[] Canvases =
        {
            ("iPhone 2556x1179", 2556f, 1179f),
            ("Reference 1920x1080", 1920f, 1080f),
        };

        [Test]
        public void BalloonRingAndTouchPadClearMapAndFieldAtTopLevel()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();

            // Water Balloon acquired and at its top level (Range track maxed) — same "s_bal" + maxed
            // Range-track idiom UiScreensDirector uses for its own "BALLOON maxed" capture. Force Field
            // acquired too, so FIELD/CAPTURE's own clearance below the ring is also exercised.
            RigState.RestoreSnapshot(new Dictionary<string, int>
            {
                { "s_bal", 1 },
                { "s_lob", WeaponCatalog.MaxLevel(WaterBalloonTrackKind.Range) },
                { "e_ff", 1 },
            }, System.Array.Empty<string>());
            WeaponSystemState.RebuildAcquiredFromRigState();

            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");
            // OnEnable just subscribed OnAbilitiesChanged — fire it again so Force Field/the balloon's
            // top-level rebuild pick up the snapshot above, same as MV676's own fixture.
            WeaponSystemState.RebuildAcquiredFromRigState();

            try
            {
                var balloonRing = FindRect(hudGo, "Water Balloon Joystick");
                var balloonPad = FindRect(hudGo, "Water Balloon Touch");
                var map = FindRect(hudGo, "Map Button");
                var field = FindRect(hudGo, "Force Field Button");

                Assert.That(balloonRing, Is.Not.Null, "fixture: the water balloon joystick must exist");
                Assert.That(balloonRing.gameObject.activeInHierarchy, Is.True,
                    "fixture: the water balloon joystick must be visible once s_bal is acquired");
                Assert.That(balloonPad, Is.Not.Null, "fixture: the water balloon joystick's touch pad must exist");
                Assert.That(map, Is.Not.Null, "fixture: the map button must exist");
                Assert.That(field, Is.Not.Null, "fixture: the force field button must exist");
                Assert.That(field.gameObject.activeInHierarchy, Is.True,
                    "fixture: force field must be visible once e_ff is acquired");

                foreach (var (name, w, h) in Canvases)
                {
                    float logWidth = Mathf.Log(w / RefW, 2f);
                    float logHeight = Mathf.Log(h / RefH, 2f);
                    float scaleFactor = Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, 0.5f));
                    float effectiveWidth = w / scaleFactor;
                    float effectiveHeight = h / scaleFactor;

                    Rect ringRect, padRect, mapRect, fieldRect;
                    var cam = ConfigureCanvasForCapture(hudGo.GetComponentInChildren<Canvas>(),
                        hudGo.GetComponentInChildren<CanvasScaler>(), effectiveWidth, effectiveHeight, out RenderTexture rt);
                    try
                    {
                        ringRect = ScreenRect(balloonRing, cam);
                        padRect = ScreenRect(balloonPad, cam);
                        mapRect = ScreenRect(map, cam);
                        fieldRect = ScreenRect(field, cam);
                    }
                    finally
                    {
                        Object.DestroyImmediate(cam.gameObject);
                        rt.Release();
                        Object.DestroyImmediate(rt);
                    }

                    // MAP sits above the balloon ring (vertical-mid vs bottom anchored) — the ticket's
                    // own defect.
                    float gapToMap = mapRect.yMin - ringRect.yMax;
                    Assert.That(gapToMap, Is.GreaterThanOrEqualTo(MinGap),
                        $"[{name}] balloon ring top {ringRect.yMax:F1} must clear MAP's bottom {mapRect.yMin:F1} " +
                        $"by at least {MinGap} units (gap {gapToMap:F1})");

                    // FIELD/CAPTURE sits below the balloon ring.
                    float gapToField = ringRect.yMin - fieldRect.yMax;
                    Assert.That(gapToField, Is.GreaterThanOrEqualTo(MinGap),
                        $"[{name}] balloon ring bottom {ringRect.yMin:F1} must clear FIELD's top {fieldRect.yMax:F1} " +
                        $"by at least {MinGap} units (gap {gapToField:F1})");

                    Assert.That(padRect.Overlaps(mapRect), Is.False,
                        $"[{name}] the balloon's touch pad {padRect} must not intersect MAP's rect {mapRect} — " +
                        "the pad would swallow taps meant for MAP");
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

            var camGo = new GameObject("MV1153 Capture Cam", typeof(Camera));
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
