using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1120 — Lee's device playthrough, World 2: "Replicators have no health bar." A Mower Hutch
    /// carries a named world health bar that drops as it is shot; a Replicator showed nothing, so the
    /// player had no feedback that shooting it was working. Root cause: <see cref="WorldHealthBar.Attach"/>
    /// was wired for gates, sentinels, robots and Max, but never called from <see cref="Replicator"/>.
    ///
    /// Real entry points throughout: the real World 2 config/map (<see cref="WorldLibrary"/>/
    /// <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/>), the first real built Replicator
    /// (<see cref="MapBuild.Replicators"/>), one real hit through <see cref="WaterBlaster"/>'s own
    /// private <c>FireTick</c> (the same reflection idiom <c>WaterBlasterGateDamageTests</c> already
    /// uses for "the real weapon hit path"), and the real <see cref="HudController"/>'s own built
    /// arena-label RectTransform (same idiom <c>MV741WorldWordingTests</c>/<c>MV645HudLeftColumnTests</c>
    /// already use). <see cref="WorldHealthBar"/>'s own private <c>LateUpdate</c> is driven directly by
    /// reflection — the same "an EditMode test can drive this directly" idiom <c>MV1040HitBarTests</c>
    /// already uses, since nothing else ticks a MonoBehaviour's per-frame callbacks in EditMode.
    ///
    /// Must fail on d30d293 (this ticket's own base commit) — see the fix comment for this test's own
    /// captured failure output.
    ///
    /// ONE new test (MV-465 Rule 1), every assertion below a sub-check of the one ticket AC. Tier 2
    /// throughout (Rule 2): every assertion reads a resolved runtime value (the bar's own Showing/
    /// fillAmount/HeightAboveCentre, the box's own Normalized, real renderer bounds, real projected
    /// screen rects) — never an authored constant, never a rendered pixel.
    /// // Guards MV-1120
    /// </summary>
    public sealed class MV1120ReplicatorHealthBarTests
    {
        private const int CanvasWidth = 1920;
        private const int CanvasHeight = 1080;

        private static readonly MethodInfo FireTickMethod =
            typeof(WaterBlaster).GetMethod("FireTick", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo BarLateUpdateMethod =
            typeof(WorldHealthBar).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo ArenaLabelField =
            typeof(HudController).GetField("_arenaLabel", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _host;
        private GameObject _blasterGo;
        private GameObject _hudGo;
        private GameObject _camGo;
        private RenderTexture _rt;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            EnemyNavigation.Reset();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (_hudGo != null)
            {
                var hud = _hudGo.GetComponent<HudController>();
                if (hud != null) InvokeLifecycle(hud, "OnDisable");
                Object.DestroyImmediate(_hudGo);
            }
            if (_blasterGo != null) Object.DestroyImmediate(_blasterGo);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            if (_rt != null) { _rt.Release(); Object.DestroyImmediate(_rt); }
            if (_host != null) Object.DestroyImmediate(_host);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            EnemyNavigation.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void ReplicatorHealthBar_HiddenThenVisibleThenGone_ClearOfRoofAndHudLabel()
        {
            // ---------------------------------------------------------------- real World 2, real Replicator.
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "setup failure: World 2's own shipped config must load");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            _host = new GameObject("MV1120 Host");
            MapBuild built = MapRuntime.Build(map, _host.transform);
            Assert.That(built.Replicators.Count, Is.GreaterThan(0),
                "setup failure: World 2 must still author at least one Replicator");
            Replicator box = built.Replicators[0];

            // AddComponent's own Awake never runs outside Play mode (Replicator.Build's own doc comment).
            LogAssert.ignoreFailingMessages = true; // same BuildBody collider-strip [Error] ReplicatorTests masks
            box.Build();
            LogAssert.ignoreFailingMessages = false;

            var bar = box.GetComponent<WorldHealthBar>();
            Assert.IsNotNull(bar, "MV-1120: Replicator.Build must attach a WorldHealthBar, the same way " +
                "every gate/sentinel/robot/Max already does");

            // ---------------------------------------------------------------- AC: hidden before damage.
            Assert.IsFalse(bar.Showing, "AC: before damage the Replicator's health bar must not be visible");

            // ---------------------------------------------------------------- capture camera, tagged MainCamera
            // so WorldHealthBar's own Refresh (Camera.main) resolves the real billboard/screen-lift, and
            // reused to project the HUD's own screen-space-overlay label into the same pixel space (see
            // MV645HudLeftColumnTests' own ConfigureCanvasForCapture/ScreenRect idiom).
            Vector3 boxPos = box.transform.position;
            _camGo = new GameObject("MV1120 Capture Cam");
            Camera cam = _camGo.AddComponent<Camera>();
            _camGo.tag = "MainCamera";
            cam.transform.position = boxPos + new Vector3(0f, 9f, -7f);
            cam.transform.LookAt(boxPos + Vector3.up * 1f, Vector3.up);
            _rt = new RenderTexture(CanvasWidth, CanvasHeight, 16);
            cam.targetTexture = _rt;

            _hudGo = new GameObject("MV1120 Hud");
            var hud = _hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");

            var arenaLabel = (Text)ArenaLabelField.GetValue(hud);
            Assert.IsNotNull(arenaLabel, "setup failure: HudController built no arena label");
            RectTransform arenaLabelRect = arenaLabel.rectTransform;

            Canvas hudCanvas = _hudGo.GetComponentInChildren<Canvas>();
            CanvasScaler hudScaler = _hudGo.GetComponentInChildren<CanvasScaler>();
            Assert.IsNotNull(hudCanvas, "setup failure: HudController built no Canvas");
            hudScaler.enabled = false;
            hudCanvas.scaleFactor = 1f;
            hudCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            hudCanvas.worldCamera = cam;
            hudCanvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();

            // ---------------------------------------------------------------- one hit, the real weapon path.
            // Aimed along the box's own authored hatch-outward direction (MV-775/MV-860) rather than an
            // arbitrary world axis — that lane is guaranteed clear of Cover-layer geometry by level design,
            // so the shot's own line-of-sight can never be blocked by a wall this fixture doesn't control.
            Vector3 outward = ((IFactoryDoorFacing)box).AuthoredDoorOutward ?? Vector3.back;
            _blasterGo = new GameObject("MV1120 Blaster");
            _blasterGo.transform.position = boxPos + outward * 3f;
            _blasterGo.transform.rotation = Quaternion.LookRotation(-outward, Vector3.up);
            var blaster = _blasterGo.AddComponent<WaterBlaster>();

            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)
            FireTickMethod.Invoke(blaster, null);

            Assert.Less(box.Normalized, 1f,
                "setup failure: the shot must land and deal real damage — it looks like it missed or was " +
                "blocked (wrong aim or an unexpected Cover-layer obstruction)");

            BarLateUpdateMethod.Invoke(bar, null);

            // ---------------------------------------------------------------- AC: visible, fill resolved.
            Assert.IsTrue(bar.Showing, "AC: after one real hit the bar must become visible");

            Image fillImage = null;
            foreach (Image img in bar.BarRectTransform.GetComponentsInChildren<Image>(true))
                if (img.name == "Fill") { fillImage = img; break; }
            Assert.IsNotNull(fillImage, "setup failure: the bar built no Fill image");
            Assert.AreEqual(box.Normalized, fillImage.fillAmount, 0.01f,
                "AC: the bar's resolved fill must equal the box's own current/max health");

            // ---------------------------------------------------------------- AC: clear of the roof/beacon.
            Renderer[] bodyRenderers = box.GetComponentsInChildren<Renderer>(true);
            Assert.That(bodyRenderers.Length, Is.GreaterThan(0), "setup failure: the replicator built no renderers");
            float roofTopY = float.NegativeInfinity;
            foreach (Renderer r in bodyRenderers) roofTopY = Mathf.Max(roofTopY, r.bounds.max.y);
            float barAnchorY = boxPos.y + bar.HeightAboveCentre;
            Assert.Greater(barAnchorY, roofTopY,
                "AC: the bar must float above the replicator's own rendered bounds (roof/beacon), not through them");

            // ---------------------------------------------------------------- AC: clear of the HUD's own
            // "REPLICATORS n/m" label (resolved screen rects, projected through the same camera/pixel space).
            Rect barScreenRect = ScreenRect(bar.BarRectTransform, cam);
            Rect labelScreenRect = ScreenRect(arenaLabelRect, cam);
            Assert.IsFalse(barScreenRect.Overlaps(labelScreenRect),
                $"AC: the bar's resolved screen rect {barScreenRect} overlaps the HUD's REPLICATORS label " +
                $"region {labelScreenRect}");

            // ---------------------------------------------------------------- AC: destroyed, no bar remains.
            box.TakeDamage(new DamageInfo(100000f, boxPos, Vector3.down, Team.Player));
            Assert.IsFalse(box.IsAlive, "setup failure: the finishing hit must destroy the replicator");

            BarLateUpdateMethod.Invoke(bar, null);
            Assert.IsFalse(bar.Showing, "AC: once the replicator is destroyed, no bar must remain visible");
        }

        /// <summary>Same safe axis-wrapped bounding-rect idiom MV645HudLeftColumnTests' own ScreenRect
        /// uses, except the min/max corners are re-sorted after projection — a world-space billboard (the
        /// health bar) can come back rotated relative to the screen's own axes depending on camera angle,
        /// unlike the screen-space-overlay elements that idiom was written for, where corner ordering is
        /// already axis-aligned.</summary>
        private static Rect ScreenRect(RectTransform rt, Camera cam)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            float xMin = Mathf.Min(a.x, b.x), xMax = Mathf.Max(a.x, b.x);
            float yMin = Mathf.Min(a.y, b.y), yMax = Mathf.Max(a.y, b.y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static void InvokeLifecycle(Object component, string methodName) =>
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
    }
}
