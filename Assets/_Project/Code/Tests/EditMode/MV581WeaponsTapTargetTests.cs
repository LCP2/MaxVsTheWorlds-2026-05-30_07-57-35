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
    /// MV-581 — the RIG button's tap target used to be just the hex mark
    /// (<see cref="HudController"/>'s old bg-image Button); the cell readout <c>BuildPowerCellCounter</c>
    /// sits beneath it had no tap handling of its own, so the two read as one control but behaved as
    /// two. The fix is <c>_weaponsTapRoot</c> ("Weapons Tap Target"), an invisible wrapper sized to
    /// enclose both, carrying the sole Button. Sole guard on this defect; do not cull (MV-465).
    /// </summary>
    public sealed class MV581WeaponsTapTargetTests
    {
        [Test]
        public void CombinedTapTargetCoversBothZonesClearsTheAimStickAndLeavesTheHexUnchanged()
        {
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
                Assert.That(tapRoot, Is.Not.Null, "MV-581: HudController must build a combined 'Weapons Tap Target' wrapper");
                Assert.That(hexRoot, Is.Not.Null, "fixture: the hex mark must still exist");
                Assert.That(cellRoot, Is.Not.Null, "fixture: the cell readout must still exist");

                // ---------------------------------------------------------------- AC1
                var button = tapRoot.GetComponentInChildren<Button>(true);
                Assert.That(button, Is.Not.Null, "the combined tap target must carry a Button");

                Assert.That(WorldRectContainsCentreOf(tapRoot, hexRoot), Is.True,
                    "the combined tap rect must enclose the hex mark's centre");
                Assert.That(WorldRectContainsCentreOf(tapRoot, cellRoot), Is.True,
                    "the combined tap rect must enclose the cell readout's centre");

                Assert.That(screen.IsOpen, Is.False, "fixture: THE RIG starts closed");
                button.onClick.Invoke();
                Assert.That(screen.IsOpen, Is.True,
                    "a tap anywhere in the combined target (hex or cell readout) must open THE RIG");

                // ---------------------------------------------------------------- AC3
                // Measured before AC2 below deliberately: AC2 flips the canvas to ScreenSpaceCamera
                // against a camera it then destroys, which leaves GetWorldCorners meaningless for
                // anything read afterwards on the same canvas.
                var weaponsButtonSize = (float)typeof(HudController)
                    .GetField("WeaponsButtonSize", BindingFlags.NonPublic | BindingFlags.Static)
                    .GetValue(null);

                var hexCorners = new Vector3[4];
                hexRoot.GetWorldCorners(hexCorners);
                float hexWidth = hexCorners[2].x - hexCorners[0].x;
                float hexHeight = hexCorners[1].y - hexCorners[0].y;
                Assert.That(hexWidth, Is.EqualTo(weaponsButtonSize).Within(0.01f),
                    "the hex mark's own rendered width must be unchanged by the tap-target wrapper");
                Assert.That(hexHeight, Is.EqualTo(weaponsButtonSize).Within(0.01f),
                    "the hex mark's own rendered height must be unchanged by the tap-target wrapper");

                // ---------------------------------------------------------------- AC2
                var aimRoot = FindRect(hudGo, "Aim Joystick");
                Assert.That(aimRoot, Is.Not.Null, "fixture: the aim joystick must exist");

                foreach (float aspect in new[] { 2.13f, 1.78f, 1.323f })
                {
                    ConfigureCanvasForAspect(hudGo, aspect, out Camera cam, out RenderTexture rt);
                    try
                    {
                        Rect tapScreenRect = ScreenRect(tapRoot, cam);
                        Rect aimScreenRect = ScreenRect(aimRoot, cam);
                        Assert.That(tapScreenRect.Overlaps(aimScreenRect), Is.False,
                            $"the combined tap rect {tapScreenRect} overlaps the aim joystick's rect {aimScreenRect} at aspect {aspect}");
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

        // ---------------------------------------------------------------- helpers

        /// <summary>True when <paramref name="target"/>'s world-rect centre falls inside
        /// <paramref name="container"/>'s world rect — read on the ambient (unmodified overlay) canvas,
        /// which is safe here because both rects are corner-anchored inside the same wrapper and their
        /// relative geometry doesn't depend on the canvas's actual pixel size (see AC2 below, which
        /// does depend on it and simulates real aspects for that reason).</summary>
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

        /// <summary>Forces the HUD's overlay canvas onto a controllable-size <c>ScreenSpaceCamera</c>
        /// rig at the given aspect (same idiom <c>MV549SafeAreaCropTests</c>/<c>MV516RigBoardFixTests</c>
        /// already use for aspect-driven layout checks) — a <c>ScreenSpaceOverlay</c> canvas's
        /// RectTransform doesn't reliably resize under the EditMode test runner, which would make an
        /// aspect-driven overlap check meaningless. Height is pinned to <c>HudController.RefH</c> (1080)
        /// with the scaler disabled and <c>scaleFactor</c> pinned to 1, so <see cref="ScreenRect"/> reads
        /// back directly in the same reference-pixel units the HUD's own anchored positions are authored
        /// in, and only the width varies with the requested aspect.</summary>
        private static void ConfigureCanvasForAspect(GameObject hudGo, float aspect, out Camera cam, out RenderTexture rt)
        {
            var canvas = hudGo.GetComponentInChildren<Canvas>();
            var scaler = hudGo.GetComponentInChildren<CanvasScaler>();
            scaler.enabled = false;
            canvas.scaleFactor = 1f;

            const int height = 1080;
            int width = Mathf.RoundToInt(height * aspect);

            var camGo = new GameObject("MV581 Capture Cam", typeof(Camera));
            cam = camGo.GetComponent<Camera>();
            rt = new RenderTexture(width, height, 16);
            cam.targetTexture = rt;
            cam.aspect = aspect;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
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
