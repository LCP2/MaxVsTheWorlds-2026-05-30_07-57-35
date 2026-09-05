using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-606 — the HUD reshuffle: the B/U ability slots are retired outright, THE RIG moves into the
    /// top-right corner they vacated, Teleport moves from the left (above Move) to the right (above
    /// Aim), and the Force Field button moves from the right (above Hydro) to the far left. Sole guard
    /// on this reshuffle; do not cull (MV-465).
    /// </summary>
    public sealed class MV606HudReshuffleTests
    {
        [Test]
        public void ReshuffledHudRetiresSlotsMovesElementsAndNeverOverlaps()
        {
            // ---------------------------------------------------------------- AC1: B/U retired, source-level.
            string path = Path.Combine(Application.dataPath, "_Project", "Code", "Runtime", "UI", "HudController.cs");
            string source = File.ReadAllText(path);
            Assert.That(Regex.Matches(source, "BuildAbilitySlots").Count, Is.EqualTo(0),
                "MV-606: BuildAbilitySlots must be deleted outright, not just left uncalled");
            foreach (var field in new[] { "_slotRadial", "_slotGlow", "_slotIcon", "_slotLetter", "_slotLocked" })
                Assert.That(source.Contains(field), Is.False, $"MV-606: retired field '{field}' must not remain");

            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();

            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");

            var screenGo = new GameObject("WeaponsScreen");
            var screen = screenGo.AddComponent<WeaponsScreen>();

            try
            {
                var tapRoot = FindRect(hudGo, "Weapons Tap Target");
                var hexRoot = FindRect(hudGo, "Weapons Button");
                var cellRoot = FindRect(hudGo, "Parts");
                var badgeRoot = FindRect(hudGo, "Module Badge");
                var forceField = FindRect(hudGo, "Force Field Button");
                var teleport = FindRect(hudGo, "Teleport Joystick");
                var balloon = FindRect(hudGo, "Water Balloon Joystick");
                var moveStick = FindRect(hudGo, "Move Joystick");
                var aimStick = FindRect(hudGo, "Aim Joystick");
                var map = FindRect(hudGo, "Map Button");
                var home = FindRect(hudGo, "Home Button");
                var utility = FindRect(hudGo, "Utility Icons");

                Assert.That(tapRoot, Is.Not.Null, "fixture: the RIG tap root must exist");
                Assert.That(hexRoot, Is.Not.Null, "fixture: the hex mark must exist");
                Assert.That(cellRoot, Is.Not.Null, "fixture: the cell readout must exist");
                Assert.That(badgeRoot, Is.Not.Null, "fixture: the module badge must exist");
                Assert.That(forceField, Is.Not.Null, "fixture: the force field button must exist");
                Assert.That(teleport, Is.Not.Null, "fixture: the teleport joystick must exist");
                Assert.That(balloon, Is.Not.Null, "fixture: the water balloon joystick must exist");
                Assert.That(moveStick, Is.Not.Null, "fixture: the move stick must exist");
                Assert.That(aimStick, Is.Not.Null, "fixture: the aim stick must exist");
                Assert.That(map, Is.Not.Null, "fixture: the map button must exist");
                Assert.That(home, Is.Not.Null, "fixture: the home button must exist");
                Assert.That(utility, Is.Not.Null, "fixture: the utility icon column must exist");

                // ---------------------------------------------------------------- AC5: the cell readout and the
                // module badge are children of the hex, so they move WITH it — their offset from the hex must
                // be exactly what it was before the RIG moved. Measured before AC2-4 below deliberately (see
                // that loop's own note on why GetWorldCorners stops being meaningful once the canvas flips to
                // ScreenSpaceCamera).
                float cellReadoutGap = (float)typeof(HudController)
                    .GetField("CellReadoutGap", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

                var hexCorners = new Vector3[4]; hexRoot.GetWorldCorners(hexCorners);
                var cellCorners = new Vector3[4]; cellRoot.GetWorldCorners(cellCorners);
                var badgeCorners = new Vector3[4]; badgeRoot.GetWorldCorners(badgeCorners);
                // GetWorldCorners order: 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right.
                Vector3 hexTopRight = hexCorners[2];
                Vector3 hexBottomCentre = (hexCorners[0] + hexCorners[3]) * 0.5f;
                Vector3 cellTopCentre = (cellCorners[1] + cellCorners[2]) * 0.5f;
                Assert.That(cellTopCentre.x, Is.EqualTo(hexBottomCentre.x).Within(0.01f),
                    "the cell readout must stay horizontally centred under the hex");
                Assert.That(hexBottomCentre.y - cellTopCentre.y, Is.EqualTo(cellReadoutGap).Within(0.01f),
                    "the cell readout must keep its fixed gap below the hex (CellReadoutGap)");
                Vector3 badgeCentre = (badgeCorners[0] + badgeCorners[2]) * 0.5f;
                // BuildWeaponsButtonBadges anchors the module badge to the hex's top-right corner at offset
                // (10, 12) with a 40x40 box (BuildCornerBadge) — its centre sits (10 - 20, 12 - 20) from that
                // corner, i.e. (-10, -8).
                Assert.That(hexTopRight.x - badgeCentre.x, Is.EqualTo(10f).Within(0.01f),
                    "the module badge must keep its fixed X offset from the hex's own corner");
                Assert.That(hexTopRight.y - badgeCentre.y, Is.EqualTo(8f).Within(0.01f),
                    "the module badge must keep its fixed Y offset from the hex's own corner");

                // ---------------------------------------------------------------- AC6 (MV-581 regression): the
                // combined tap target still encloses the cell readout, and still opens THE RIG, at its new
                // top-right position.
                Assert.That(WorldRectContainsCentreOf(tapRoot, cellRoot), Is.True,
                    "the combined tap rect must still enclose the cell readout's centre at the RIG's new position");
                var button = tapRoot.GetComponentInChildren<Button>(true);
                Assert.That(screen.IsOpen, Is.False, "fixture: THE RIG starts closed");
                button.onClick.Invoke();
                Assert.That(screen.IsOpen, Is.True,
                    "a tap on the combined target must still open THE RIG at its new position");

                // ---------------------------------------------------------------- AC2/AC3/AC4: aspect-driven checks.
                // Move/Aim get their 30px fat-finger touch-pad margin added manually (AddOnScreenStick builds
                // that pad as a separate child, so the roots found above don't already include it).
                var elements = new (string id, RectTransform rt, float pad)[]
                {
                    ("RIG", tapRoot, 0f),
                    ("ForceField", forceField, 0f),
                    ("Teleport", teleport, 0f),
                    ("Balloon", balloon, 0f),
                    ("MoveStick", moveStick, 30f),
                    ("AimStick", aimStick, 30f),
                    ("MAP", map, 0f),
                    ("HOME", home, 0f),
                    ("Utility", utility, 0f),
                };

                foreach (float aspect in new[] { 2.13f, 1.78f, 1.33f })
                {
                    ConfigureCanvasForAspect(hudGo, aspect, out Camera cam, out RenderTexture rt);
                    try
                    {
                        float width = 1080f * aspect;
                        float height = 1080f;
                        var rects = new (string id, Rect rect)[elements.Length];
                        for (int i = 0; i < elements.Length; i++)
                            rects[i] = (elements[i].id, ScreenRect(elements[i].rt, cam, elements[i].pad));

                        // AC3: every element lies entirely inside the safe area (== full screen; no notch
                        // simulated here).
                        foreach (var (id, r) in rects)
                        {
                            Assert.That(r.xMin, Is.GreaterThanOrEqualTo(-1f), $"'{id}' crops past the safe area's left edge at aspect {aspect}");
                            Assert.That(r.xMax, Is.LessThanOrEqualTo(width + 1f), $"'{id}' crops past the safe area's right edge at aspect {aspect}");
                            Assert.That(r.yMin, Is.GreaterThanOrEqualTo(-1f), $"'{id}' crops past the safe area's bottom edge at aspect {aspect}");
                            Assert.That(r.yMax, Is.LessThanOrEqualTo(height + 1f), $"'{id}' crops past the safe area's top edge at aspect {aspect}");
                        }

                        // AC2: no two elements' rects intersect, pairwise over the full set.
                        for (int i = 0; i < rects.Length; i++)
                            for (int j = i + 1; j < rects.Length; j++)
                                Assert.That(rects[i].rect.Overlaps(rects[j].rect), Is.False,
                                    $"'{rects[i].id}' {rects[i].rect} overlaps '{rects[j].id}' {rects[j].rect} at aspect {aspect}");

                        // AC4: quadrant / third / alignment checks.
                        Rect rigRect = rects[0].rect;
                        Assert.That(rigRect.xMin, Is.GreaterThan(width * 0.5f), $"RIG must sit in the top-right quadrant at aspect {aspect}");
                        Assert.That(rigRect.yMin, Is.GreaterThan(height * 0.5f), $"RIG must sit in the top-right quadrant at aspect {aspect}");

                        Rect ffRect = rects[1].rect;
                        Assert.That(ffRect.xMax, Is.LessThan(width / 3f), $"Force Field must sit in the left third at aspect {aspect}");

                        Rect teleRect = rects[2].rect;
                        Rect aimRect = rects[5].rect;
                        Assert.That(Mathf.Abs(teleRect.center.x - aimRect.center.x), Is.LessThanOrEqualTo(40f),
                            $"Teleport's centre must sit within 40 units of the aim stick's centre on X at aspect {aspect}");
                        Assert.That(teleRect.center.y, Is.GreaterThan(aimRect.yMax),
                            $"Teleport's centre must sit above the aim stick's top edge at aspect {aspect}");
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
                Object.DestroyImmediate(screenGo);
                WeaponSystemState.Reset();
                RigState.Reset();
                RigFusionState.Reset();
                PickupWallet.Reset();
            }
        }

        // ---------------------------------------------------------------- helpers (mirrors MV581WeaponsTapTargetTests)

        private static bool WorldRectContainsCentreOf(RectTransform container, RectTransform target)
        {
            var containerCorners = new Vector3[4];
            container.GetWorldCorners(containerCorners);
            var targetCorners = new Vector3[4];
            target.GetWorldCorners(targetCorners);
            Vector3 centre = (targetCorners[0] + targetCorners[2]) * 0.5f;
            return centre.x >= containerCorners[0].x && centre.x <= containerCorners[2].x
                && centre.y >= containerCorners[0].y && centre.y <= containerCorners[2].y;
        }

        private static void ConfigureCanvasForAspect(GameObject hudGo, float aspect, out Camera cam, out RenderTexture rt)
        {
            var canvas = hudGo.GetComponentInChildren<Canvas>();
            var scaler = hudGo.GetComponentInChildren<CanvasScaler>();
            scaler.enabled = false;
            canvas.scaleFactor = 1f;

            const int height = 1080;
            int width = Mathf.RoundToInt(height * aspect);

            var camGo = new GameObject("MV606 Capture Cam", typeof(Camera));
            cam = camGo.GetComponent<Camera>();
            rt = new RenderTexture(width, height, 16);
            cam.targetTexture = rt;
            cam.aspect = aspect;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
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
