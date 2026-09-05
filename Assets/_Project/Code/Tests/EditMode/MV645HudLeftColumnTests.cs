using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Dev;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-645 — the left play-area column: MAP, the Settings/Controls gear, Water Balloon and Force
    /// Field all share X=150 (the Move stick's own X), stacked bottom-to-top MAP/gear/Balloon/
    /// Force-Field->Force-Field-bottom, and the top-left utility icon column drops its two dead "P"/
    /// "S" placeholders, keeping only "?". Sole guard on this reshuffle; do not cull (MV-465).
    /// </summary>
    public sealed class MV645HudLeftColumnTests
    {
        private const int CanvasWidth = 1920;
        private const int CanvasHeight = 1080;

        [Test]
        public void LeftColumnElementsLandOnTheirSharedXAndUtilityIconsDropDeadPlaceholders()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();

            // All four elements visible: Force Field + Water Balloon acquired, Water Balloon's Range
            // track maxed so its joystick renders at its largest (200px) size for the AC2 overlap
            // check. RestoreSnapshot bypasses the draft/reach gating RigState.AcquireCap enforces —
            // the same shortcut MV524ResumeWiringTests/MV597PrimaryRebalanceTests use for fixtures.
            RigState.RestoreSnapshot(new Dictionary<string, int>
            {
                { "s_bal", 1 },
                { "e_ff", 1 },
                { "s_lob", WeaponCatalog.MaxLevel(WaterBalloonTrackKind.Range) },
            }, System.Array.Empty<string>());
            WeaponSystemState.RebuildAcquiredFromRigState();

            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");
            // OnEnable just subscribed OnAbilitiesChanged — fire it again now so Force Field
            // activates and the Water Balloon joystick rebuilds at its maxed (200px) size.
            WeaponSystemState.RebuildAcquiredFromRigState();

            var overlayGo = new GameObject("Mv503 Probe");
            var overlay = overlayGo.AddComponent<Mv503DiagnosticOverlay>();
            InvokeLifecycle(overlay, "Awake");
            InvokeLifecycle(overlay, "OnEnable");

            var settingsGo = new GameObject("SettingsPanel");
            var settings = settingsGo.AddComponent<SettingsPanel>();
            typeof(SettingsPanel).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(settings, null);

            try
            {
                var map = FindRect(hudGo, "Map Button");
                var forceField = FindRect(hudGo, "Force Field Button");
                var balloon = FindRect(hudGo, "Water Balloon Joystick");
                var home = FindRect(hudGo, "Home Button");
                var utility = FindRect(hudGo, "Utility Icons");
                var invasionDial = FindRect(hudGo, "Invasion Dial");
                var arenaLabel = ((Text)typeof(HudController)
                    .GetField("_arenaLabel", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(hud)).rectTransform;
                var spawnLevelBar = (RectTransform)typeof(HudController)
                    .GetField("_spawnLevelRoot", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(hud);
                var gear = FindRect(settingsGo, "Gear");

                Assert.That(map, Is.Not.Null, "fixture: the map button must exist");
                Assert.That(forceField, Is.Not.Null, "fixture: the force field button must exist");
                Assert.That(balloon, Is.Not.Null, "fixture: the water balloon joystick must exist");
                Assert.That(home, Is.Not.Null, "fixture: the home button must exist");
                Assert.That(utility, Is.Not.Null, "fixture: the utility icon column must exist");
                Assert.That(invasionDial, Is.Not.Null, "fixture: the invasion dial must exist");
                Assert.That(arenaLabel, Is.Not.Null, "fixture: the arena label must exist");
                Assert.That(spawnLevelBar, Is.Not.Null, "fixture: the spawn level bar must exist");
                Assert.That(gear, Is.Not.Null, "fixture: the settings gear button must exist");

                Assert.That(forceField.gameObject.activeInHierarchy, Is.True,
                    "fixture: Force Field must be visible once acquired");
                Assert.That(balloon.sizeDelta.x, Is.EqualTo(200f).Within(0.01f),
                    "fixture: Water Balloon must be at its maxed (200px) size for the overlap check");

                // ---------------------------------------------------------------- AC1: resolved centres.
                Rect mapRect, ffRect, balloonRect, homeRect, utilityRect, dialRect, arenaRect, spawnRect;
                var hudCam = ConfigureCanvasForCapture(hudGo.GetComponentInChildren<Canvas>(),
                    hudGo.GetComponentInChildren<CanvasScaler>(), out RenderTexture hudRt);
                try
                {
                    mapRect = ScreenRect(map, hudCam);
                    ffRect = ScreenRect(forceField, hudCam);
                    balloonRect = ScreenRect(balloon, hudCam);
                    homeRect = ScreenRect(home, hudCam);
                    utilityRect = ScreenRect(utility, hudCam);
                    dialRect = ScreenRect(invasionDial, hudCam);
                    arenaRect = ScreenRect(arenaLabel, hudCam);
                    spawnRect = ScreenRect(spawnLevelBar, hudCam);
                }
                finally
                {
                    Object.DestroyImmediate(hudCam.gameObject);
                    hudRt.Release();
                    Object.DestroyImmediate(hudRt);
                }

                Rect gearRect;
                var gearCam = ConfigureCanvasForCapture(settingsGo.GetComponentInChildren<Canvas>(),
                    settingsGo.GetComponentInChildren<CanvasScaler>(), out RenderTexture gearRt);
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

                // MV-676: centres raised (357/554/744/894) as part of widening this column's gaps —
                // see HudController.ForceFieldRise/WaterBalloonJoystickRise/MapButtonRise and
                // SettingsPanel.GearRise.
                AssertCentre(ffRect, 150f, 357f, "Force Field");
                AssertCentre(balloonRect, 150f, 554f, "Water Balloon");
                AssertCentre(gearRect, 150f, 744f, "Settings gear");
                AssertCentre(mapRect, 150f, 894f, "MAP");

                // ---------------------------------------------------------------- AC2: no overlap, stack fits.
                var column = new (string id, Rect rect)[]
                {
                    ("MAP", mapRect), ("Gear", gearRect), ("Balloon", balloonRect), ("ForceField", ffRect),
                };
                var others = new (string id, Rect rect)[]
                {
                    ("HOME", homeRect), ("Utility", utilityRect), ("InvasionDial", dialRect),
                    ("Arena", arenaRect), ("SpawnLevelBar", spawnRect),
                };
                for (int i = 0; i < column.Length; i++)
                    for (int j = i + 1; j < column.Length; j++)
                        Assert.That(column[i].rect.Overlaps(column[j].rect), Is.False,
                            $"'{column[i].id}' {column[i].rect} overlaps '{column[j].id}' {column[j].rect}");
                foreach (var (aId, aRect) in column)
                    foreach (var (bId, bRect) in others)
                        Assert.That(aRect.Overlaps(bRect), Is.False,
                            $"'{aId}' {aRect} overlaps '{bId}' {bRect}");

                Assert.That(mapRect.yMax, Is.LessThanOrEqualTo(CanvasHeight + 1f),
                    "the stack's top edge (MAP's top) must not exceed the canvas height");

                // ---------------------------------------------------------------- AC3: utility icons.
                var iconNames = new List<string>();
                foreach (Transform child in utility.transform) iconNames.Add(child.name);
                Assert.That(iconNames, Is.EqualTo(new[] { "Icon ?" }),
                    "the utility icon column must contain exactly one slot, 'Icon ?' — 'P' and 'S' must be gone");

                var helpButton = utility.GetComponentInChildren<Button>(true);
                Assert.That(helpButton, Is.Not.Null, "fixture: the '?' icon must still be a button");
                Assert.That(overlay.Visible, Is.False, "fixture: the diagnostic overlay starts hidden");
                helpButton.onClick.Invoke();
                Assert.That(overlay.Visible, Is.True,
                    "the '?' icon must still toggle the MV-503 diagnostic overlay");
            }
            finally
            {
                InvokeLifecycle(hud, "OnDisable");
                InvokeLifecycle(overlay, "OnDisable");
                Object.DestroyImmediate(hudGo);
                Object.DestroyImmediate(overlayGo);
                Object.DestroyImmediate(settingsGo);
                WeaponSystemState.Reset();
                RigState.Reset();
                RigFusionState.Reset();
                PickupWallet.Reset();
            }
        }

        private static void AssertCentre(Rect rect, float expectedX, float expectedY, string id)
        {
            Assert.That(rect.center.x, Is.EqualTo(expectedX).Within(2f), $"{id} centre X");
            Assert.That(rect.center.y, Is.EqualTo(expectedY).Within(2f), $"{id} centre Y");
        }

        private static Camera ConfigureCanvasForCapture(Canvas canvas, CanvasScaler scaler, out RenderTexture rt)
        {
            scaler.enabled = false;
            canvas.scaleFactor = 1f;

            var camGo = new GameObject("MV645 Capture Cam", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            rt = new RenderTexture(CanvasWidth, CanvasHeight, 16);
            cam.targetTexture = rt;
            cam.aspect = (float)CanvasWidth / CanvasHeight;

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
