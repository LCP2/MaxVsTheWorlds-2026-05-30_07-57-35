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
    /// MV-676 — HudController's CanvasScaler (ScaleWithScreenSize, reference 1920x1080,
    /// matchWidthOrHeight=0.5) blends width- and height-matching on a log scale. On an iPhone-standard
    /// landscape aspect (~852x393pt, far more elongated than the 1080p reference) that blend compresses
    /// the effective visible canvas to ~978 reference units tall — HudController has no phone-aspect-
    /// aware repositioning anywhere, so an element placed near the "safe" 1080 ceiling clips on a real
    /// device (the Attack Mode toggle did, at a resolved top edge of 962). This test replicates
    /// CanvasScaler's own log-blend formula to compute that effective height directly, rather than
    /// assuming the full 1080 is visible, and asserts every element this ticket repositioned clears it
    /// with a 20-unit margin. Sole guard on this fix; do not cull (MV-465).
    /// </summary>
    public sealed class MV676HudPhoneAspectMarginTests
    {
        private const float RefW = 1920f, RefH = 1080f;
        private const float PhoneW = 852f, PhoneH = 393f;
        private const float MinMargin = 20f;

        [Test]
        public void RepositionedElementsClearThePhoneAspectEffectiveCeiling()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();

            // Force Field + Water Balloon acquired, u_mov unlocked so the Attack Mode toggle shows.
            // RestoreSnapshot bypasses the draft/reach gating RigState.AcquireCap enforces — the same
            // shortcut MV645HudLeftColumnTests uses for its fixture.
            RigState.RestoreSnapshot(new Dictionary<string, int>
            {
                { "s_bal", 1 },
                { "e_ff", 1 },
                { "u_mov", 1 },
            }, System.Array.Empty<string>());
            WeaponSystemState.RebuildAcquiredFromRigState();

            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");
            // OnEnable just subscribed OnAbilitiesChanged — fire it again so Force Field/the Attack
            // Mode toggle pick up the snapshot above.
            WeaponSystemState.RebuildAcquiredFromRigState();

            var settingsGo = new GameObject("SettingsPanel");
            var settings = settingsGo.AddComponent<SettingsPanel>();
            typeof(SettingsPanel).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(settings, null);

            try
            {
                var attackToggle = FindRect(hudGo, "Sentinel Attack Mode Toggle");
                var forceField = FindRect(hudGo, "Force Field Button");
                var balloon = FindRect(hudGo, "Water Balloon Joystick");
                var map = FindRect(hudGo, "Map Button");
                var gear = FindRect(settingsGo, "Gear");

                Assert.That(attackToggle, Is.Not.Null, "fixture: the attack mode toggle must exist");
                Assert.That(attackToggle.gameObject.activeInHierarchy, Is.True,
                    "fixture: the attack mode toggle must be visible once u_mov is unlocked");
                Assert.That(forceField, Is.Not.Null, "fixture: the force field button must exist");
                Assert.That(forceField.gameObject.activeInHierarchy, Is.True,
                    "fixture: Force Field must be visible once acquired");
                Assert.That(balloon, Is.Not.Null, "fixture: the water balloon joystick must exist");
                Assert.That(map, Is.Not.Null, "fixture: the map button must exist");
                Assert.That(gear, Is.Not.Null, "fixture: the settings gear button must exist");

                // Replicates CanvasScaler.ScaleWithScreenSize's own log-blend (matchWidthOrHeight=0.5):
                // the effective canvas is the physical screen size divided back out by that factor.
                float logWidth = Mathf.Log(PhoneW / RefW, 2f);
                float logHeight = Mathf.Log(PhoneH / RefH, 2f);
                float scaleFactor = Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, 0.5f));
                float effectiveWidth = PhoneW / scaleFactor;
                float effectiveHeight = PhoneH / scaleFactor;

                Rect toggleRect, ffRect, balloonRect, mapRect;
                var hudCam = ConfigureCanvasForCapture(hudGo.GetComponentInChildren<Canvas>(),
                    hudGo.GetComponentInChildren<CanvasScaler>(), effectiveWidth, effectiveHeight, out RenderTexture hudRt);
                try
                {
                    toggleRect = ScreenRect(attackToggle, hudCam);
                    ffRect = ScreenRect(forceField, hudCam);
                    balloonRect = ScreenRect(balloon, hudCam);
                    mapRect = ScreenRect(map, hudCam);
                }
                finally
                {
                    Object.DestroyImmediate(hudCam.gameObject);
                    hudRt.Release();
                    Object.DestroyImmediate(hudRt);
                }

                Rect gearRect;
                var gearCam = ConfigureCanvasForCapture(settingsGo.GetComponentInChildren<Canvas>(),
                    settingsGo.GetComponentInChildren<CanvasScaler>(), effectiveWidth, effectiveHeight, out RenderTexture gearRt);
                try
                {
                    gearRect = ScreenRect(gear, gearCam);
                }
                finally
                {
                    Object.DestroyImmediate(gearCam.gameObject);
                    gearRt.Release();
                    Object.DestroyImmediate(gearRt);
                }

                var elements = new (string id, Rect rect)[]
                {
                    ("Attack Mode Toggle", toggleRect),
                    ("Force Field", ffRect),
                    ("Water Balloon", balloonRect),
                    ("Settings Gear", gearRect),
                    ("MAP", mapRect),
                };

                foreach (var (id, rect) in elements)
                    Assert.That(rect.yMax, Is.LessThanOrEqualTo(effectiveHeight - MinMargin),
                        $"'{id}' top edge {rect.yMax:F1} must clear the iPhone-standard effective " +
                        $"ceiling ({effectiveHeight:F1}) with a {MinMargin}-unit margin");
            }
            finally
            {
                InvokeLifecycle(hud, "OnDisable");
                Object.DestroyImmediate(hudGo);
                Object.DestroyImmediate(settingsGo);
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

            var camGo = new GameObject("MV676 Capture Cam", typeof(Camera));
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
