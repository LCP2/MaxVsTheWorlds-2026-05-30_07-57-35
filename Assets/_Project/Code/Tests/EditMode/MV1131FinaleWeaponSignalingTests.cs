using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.CameraRig;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1131 (the one new test, per CC_AUTONOMY's testing policy): drives the real
    /// <see cref="WorldFinaleGate"/> through its real signals in a World 1 final area, carrying every
    /// AC1 sub-check as one flowing narrative against resolved runtime values — never an authored
    /// constant, a set field, or a private reflected into for the assertions themselves (reflection here
    /// is only ever lifecycle plumbing: invoking Awake/OnEnable, which Unity does not reliably call on
    /// AddComponent outside Play mode, same as every sibling finale test in this file's own family,
    /// e.g. MV1078FinaleWeaponAndCleanupTests/MV1129FinaleResumeTests).
    ///
    /// Fails on base commit 77736a3 (the tip before this ticket): none of
    /// <see cref="HudSignals.Objective"/>, <see cref="LightPillar"/>, <see cref="CameraFacingFlare"/>,
    /// <see cref="HudController.ArenaLabelVisible"/>/<see cref="HudController.ObjectiveVisible"/>/
    /// <see cref="HudController.ObjectiveText"/>/<see cref="HudController.SetControlsHidden"/>/
    /// <see cref="HudController.ActiveCanvas"/>, or <see cref="FinaleBanner.Line1"/>/<see cref="FinaleBanner.Line2"/>/
    /// <see cref="FinaleBanner.Line3"/>/its three-line <c>Show</c> overload exist on that commit at all —
    /// this test does not even compile there (the compiler's own missing-member errors are the fail-first
    /// proof, quoted in the fix comment).
    /// </summary>
    public sealed class MV1131FinaleWeaponSignalingTests
    {
        private string _dir;
        private GameObject _root;
        private GameObject _camGo;
        private GameObject _hudGo;
        private GameObject _cameraRigGo;
        private GameObject _gateGo;
        private GameObject _payoffGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1131-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 0 });

            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
            BossCensus.Reset();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            Time.timeScale = 1f;

            // AC1d's dot-product check and the beacon/flare's own billboard math both need a live
            // Camera.main, matching this project's fixed 60 deg top-down pitch.
            _camGo = new GameObject("MV-1131 Camera Probe", typeof(Camera)) { tag = "MainCamera" };
            _camGo.transform.position = new Vector3(0f, 20f, -11.5f);
            _camGo.transform.rotation = Quaternion.Euler(60f, 0f, 0f);

            _root = new GameObject("MV-1131 Probe Root");

            _cameraRigGo = new GameObject("MV-1131 FixedAngleCameraRig Probe");
            _cameraRigGo.AddComponent<FixedAngleCameraRig>();

            HudController.SkipTouchControlsForTests = true;
            _hudGo = new GameObject("HUD");
            _hudGo.AddComponent<HudController>();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);

            // OnDisable isn't reliably invoked on DestroyImmediate outside Play mode either (same note
            // every HudController-building test in this project carries, e.g. MV645HudLeftColumnTests/
            // WeaponsButtonAlertTests) -- drive it directly, or the static HudSignals/WeaponSystemState
            // subscriptions it set up in OnEnable outlive this test and corrupt whichever one runs next.
            if (_gateGo != null) { InvokeLifecycle(_gateGo.GetComponent<WorldFinaleGate>(), "OnDisable"); Object.DestroyImmediate(_gateGo); }
            if (_payoffGo != null) { InvokeLifecycle(_payoffGo.GetComponent<BossVictoryPayoff>(), "OnDisable"); Object.DestroyImmediate(_payoffGo); }
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_cameraRigGo);
            Object.DestroyImmediate(_camGo);
            if (_hudGo != null)
            {
                InvokeLifecycle(_hudGo.GetComponent<HudController>(), "OnDisable");
                Object.DestroyImmediate(_hudGo);
            }
            HudController.SkipTouchControlsForTests = false;

            foreach (var rs in Object.FindObjectsByType<ResultScreen>(FindObjectsSortMode.None))
                Object.DestroyImmediate(rs.gameObject);
            foreach (var ring in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(ring.gameObject);
            foreach (var pillar in Object.FindObjectsByType<LightPillar>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(pillar.gameObject);
            foreach (var flare in Object.FindObjectsByType<CameraFacingFlare>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(flare.gameObject);
            foreach (var shards in Object.FindObjectsByType<ShardBurst>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(shards.gameObject);
            foreach (var bolt in Object.FindObjectsByType<SentinelBolt>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(bolt.gameObject);
            foreach (var banner in Object.FindObjectsByType<FinaleBanner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(banner.gameObject);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
            BossCensus.Reset();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;
            ModalFrameRateGate.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        // Lifecycle plumbing only (MV1078/MV1129's own established idiom) -- Unity does not reliably run
        // Awake/OnEnable for AddComponent outside Play mode, so every sibling finale test in this project
        // drives them directly. Never used to read or set the state an assertion below depends on.
        private static void InvokeLifecycle(Component c, string method) =>
            c.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(c, null);

        private static Pickup[] LivePickups() => Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None);

        private static void InvokeCollect(PickupDirector director, Pickup pickup)
        {
            var liveField = typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);
            var live = (System.Collections.IList)liveField.GetValue(director);
            int index = live.IndexOf(pickup);
            Assert.GreaterOrEqual(index, 0, "the collected pickup must still be live on the director");
            typeof(PickupDirector).GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { index, pickup });
        }

        /// <summary>Replicates <c>CanvasScaler.HandleScaleWithScreenSize</c>'s own published formula —
        /// same helper <c>MV960HomeLayoutTests</c> established for proving a resolved pixel size under
        /// the EditMode test runner, where a Screen Space Overlay canvas's own RectTransform does not
        /// reliably resize.</summary>
        private static Vector2 ExpectedCanvasRefSize(Vector2 screenSize, Vector2 refRes, float match)
        {
            float logWidth = Mathf.Log(screenSize.x / refRes.x, 2f);
            float logHeight = Mathf.Log(screenSize.y / refRes.y, 2f);
            float logAverage = Mathf.Lerp(logWidth, logHeight, match);
            float scaleFactor = Mathf.Pow(2f, logAverage);
            return screenSize / scaleFactor;
        }

        [Test]
        public void NewWeaponMoment_ObjectiveBeaconCameraAndFlare_MatchTheTicket()
        {
            // ---------- arrange: World 1's real final area, through to the Core on the ground ----------
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            MapBuild built = MapRuntime.Build(map, _root.transform);

            var areaDirector = _root.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg);
            areaDirector.Configure(map, System.Array.Empty<CoverPiece>());
            areaDirector.EnterArea(30);

            _gateGo = new GameObject("MV-1131 WorldFinaleGate Test");
            var gate = _gateGo.AddComponent<WorldFinaleGate>();
            InvokeLifecycle(gate, "Awake");
            InvokeLifecycle(gate, "OnEnable");

            // BossVictoryPayoff -- not WorldFinaleGate -- is what actually spawns the Weapon Core off
            // HudSignals.BossDefeated (MaybeDropWeaponCore); same two-component fixture MV1078 uses.
            _payoffGo = new GameObject("MV-1131 BossVictoryPayoff Test");
            var payoff = _payoffGo.AddComponent<BossVictoryPayoff>();
            InvokeLifecycle(payoff, "OnEnable");

            var hud = _hudGo.GetComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");

            var rig = _cameraRigGo.GetComponent<FixedAngleCameraRig>();

            Assert.IsTrue(built.Actors.TryGetValue("a30_boss1", out GameObject boss1Go) && boss1Go != null,
                "world1_config.json's a30_boss1 was not built");
            Assert.IsTrue(built.Actors.TryGetValue("a30_boss2", out GameObject boss2Go) && boss2Go != null,
                "world1_config.json's a30_boss2 was not built");
            var boss1 = boss1Go.GetComponent<BigBermudaBoss>();
            var boss2 = boss2Go.GetComponent<BigBermudaBoss>();
            BossCensus.Register(boss1, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
            BossCensus.Register(boss2, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
            BossCensus.ReportDefeated(boss1);
            BossCensus.ReportDefeated(boss2); // a30's last boss -- fires BossDefeated, drops the Core

            var pickupDirector = PickupDirector.EnsureInstalled();
            Pickup core = LivePickups().SingleOrDefault(p => p.Kind == PickupKind.WeaponCore);
            Assert.IsNotNull(core, "fixture: the final boss's death must drop the Weapon Core");

            // ---------- AC1a: the Core on the ground ----------
            Assert.IsTrue(hud.ObjectiveVisible, "AC1a: the objective strip must be showing with the Core on the ground");
            Assert.AreEqual("TAKE THE CORE", hud.ObjectiveText, "AC1a: the objective strip's own text");
            Assert.IsFalse(hud.ArenaLabelVisible, "AC1a: the bottom arena label must be hidden while the strip shows");

            InvokeLifecycle(gate, "Update"); // places the beacon pillar at the Core's own live position
            var pillar = Object.FindObjectsByType<LightPillar>(FindObjectsSortMode.None).SingleOrDefault();
            Assert.IsNotNull(pillar, "AC1a: a light pillar must stand over the Core while it waits");
            Assert.That(pillar.ResolvedHeight, Is.GreaterThanOrEqualTo(5.5f),
                $"AC1a: the pillar's resolved height was {pillar.ResolvedHeight}m, under the 5.5m floor");

            // ---------- collect the Core -- Beat A (WEAPON TAKEN) begins ----------
            float restDistance = rig.Distance;
            InvokeCollect(pickupDirector, core);
            Assert.IsFalse(hud.ObjectiveVisible, "the objective strip must clear the instant the Core is collected");

            gate.TickWeaponBeat(0.6f); // cumulative t=0.6s -- inside the flare's own 0.5-0.75s window

            // ---------- AC1d: the flare at the gun ----------
            var flare = Object.FindObjectsByType<CameraFacingFlare>(FindObjectsSortMode.None)
                .FirstOrDefault(f => f.name.Contains("Weapon Flash"));
            Assert.IsNotNull(flare, "AC1d: the gun flare must be live inside its own 0.5-0.75s window");
            Assert.That(flare.ResolvedDiameter, Is.GreaterThanOrEqualTo(2.4f),
                $"AC1d: the flare's resolved diameter was {flare.ResolvedDiameter}m, under the 2.4m floor");
            float facingDot = Mathf.Abs(Vector3.Dot(flare.transform.forward, _camGo.transform.forward));
            Assert.That(facingDot, Is.GreaterThanOrEqualTo(0.95f),
                $"AC1d: the flare's forward/camera-forward dot product was {facingDot}, under the 0.95 floor");

            gate.TickWeaponBeat(0.4f); // cumulative t=1.0s -- the AC1b/AC1c checkpoint

            // ---------- AC1c: the camera's own push-in ----------
            float ratio = rig.Distance / restDistance;
            Assert.That(ratio, Is.EqualTo(0.83f).Within(0.03f),
                $"AC1c: at 1.0s the camera's follow-distance ratio was {ratio}, outside 0.83+-0.03");

            // ---------- AC1b: the banner, on the real scaled HUD canvas ----------
            var banner = Object.FindObjectsByType<FinaleBanner>(FindObjectsSortMode.None).SingleOrDefault();
            Assert.IsNotNull(banner, "AC1b: the new-weapon banner must be live 1.0s into the beat");
            Canvas bannerCanvas = banner.Line2.GetComponentInParent<Canvas>();
            Assert.IsNotNull(bannerCanvas, "AC1b: the banner's text must sit under a Canvas");
            Assert.AreEqual(HudController.ActiveCanvas, bannerCanvas,
                "AC1b: the banner must land on the live HUD's own canvas, not a standalone one");
            var scaler = bannerCanvas.GetComponent<CanvasScaler>();
            Assert.IsNotNull(scaler, "AC1b: that Canvas must carry a CanvasScaler");
            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode,
                "AC1b: the CanvasScaler must be in scale-with-screen-size mode");
            Assert.AreEqual("PULSE EMITTER", banner.Line2.text,
                "AC1b: line 2 must read the next weapon's own banner short name");

            // Simulate CanvasScaler.ScaleWithScreenSize at 2556x1179 -- the same WorldSpace hand-sizing
            // idiom MV960HomeLayoutTests established, since a Screen Space Overlay canvas's own
            // RectTransform does not reliably resize under the EditMode test runner.
            var screenSize = new Vector2(2556f, 1179f);
            var refRes = new Vector2(1920f, 1080f);
            const float match = 0.5f;
            scaler.enabled = false;
            bannerCanvas.renderMode = RenderMode.WorldSpace;
            var canvasRt = (RectTransform)bannerCanvas.transform;
            canvasRt.anchorMin = canvasRt.anchorMax = new Vector2(0.5f, 0.5f);
            canvasRt.pivot = new Vector2(0.5f, 0.5f);
            Vector2 refSize = ExpectedCanvasRefSize(screenSize, refRes, match);
            canvasRt.sizeDelta = refSize;
            float scaleFactor = screenSize.y / refSize.y;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRt);
            banner.Show("NEW WEAPON", "PULSE EMITTER", "Fires pulses that home in on robots", 1f); // re-layout against the resized canvas

            // The resolved glyph height -- Text.fontSize re-resolved every Show() call against the real
            // canvas rect height, not a copy of the authored fraction -- converted to this screen's real
            // device pixels via the same scale factor CanvasScaler itself would apply.
            float resolvedPx = banner.Line2.fontSize * scaleFactor;
            Assert.That(resolvedPx, Is.GreaterThanOrEqualTo(100f),
                $"AC1b: line 2's resolved glyph height at 2556x1179 was {resolvedPx}px, under the 100px floor");

            bannerCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler.enabled = true;

            // ---------- the camera eases back once the beat ends ----------
            gate.TickWeaponBeat(1.6f); // cumulative t=2.6s -- the beat (2.5s) has already ended
            float endRatio = rig.Distance / restDistance;
            Assert.That(endRatio, Is.EqualTo(1f).Within(0.01f),
                $"at 2.6s the camera must be back within 1% of its pre-beat distance, ratio was {endRatio}");
        }
    }
}
