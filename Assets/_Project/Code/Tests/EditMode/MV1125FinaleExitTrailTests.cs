using System.Collections.Generic;
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
using MaxWorlds.Intro;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1125 (the one new test, per CC_AUTONOMY's testing policy): drives the real
    /// <see cref="WorldFinaleGate"/> through a World 1 final area, Max 5 m from the exit door when the
    /// exit beat starts, carrying every AC1 sub-check as one flowing narrative against resolved runtime
    /// values — the real <see cref="CameraTargetRig"/>'s own travel weight, the real exit door lamp's own
    /// rendered colour (read back off its MaterialPropertyBlock), the real <see cref="FinaleBanner"/>'s
    /// own resolved glyph height on a real scaled Canvas, the real <see cref="HudController"/>'s own
    /// objective strip, and the real chevron trail <see cref="WorldFinaleGate.RefreshExitTrail"/> lays
    /// down (which itself asks <see cref="EnemyNavigation.Waypoint"/>, same as any robot would).
    ///
    /// Fails on base commit 7e1dacf (the tip before this ticket): none of
    /// <see cref="CameraTargetRig.FocusWeight"/>, <see cref="WorldFinaleGate.ExitDoorLampColor"/>,
    /// <see cref="WorldFinaleGate.RefreshExitTrail"/>, <see cref="WorldFinaleGate.ExitTrailChevronCount"/>,
    /// <see cref="WorldFinaleGate.ExitTrailChevronPosition"/>, <see cref="WorldFinaleGate.ExitTrailChevronForward"/>,
    /// <see cref="FinaleBanner.ShowExitOpen"/>, or <see cref="HudController.ObjectiveBorderColor"/> exist
    /// on that commit at all — this test does not even compile there (the compiler's own missing-member
    /// errors are the fail-first proof, quoted in the fix comment). On a hand-reverted tree carrying only
    /// this test file against the base commit's own API shape, the pre-existing 8 m near-door camera skip
    /// and the bolt's own double-tick would additionally make AC(a)/AC(b) fail outright even if the test
    /// were trimmed to compile.
    /// </summary>
    public sealed class MV1125FinaleExitTrailTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static void InvokeLifecycle(Object c, string method) =>
            c.GetType().GetMethod(method, NonPublicInstance)?.Invoke(c, null);

        private static void SetPrivateField(object target, string field, object value) =>
            target.GetType().GetField(field, NonPublicInstance).SetValue(target, value);

        private static void InvokeOnDeath(Component boss) =>
            boss.GetType().GetMethod("OnDeath", NonPublicInstance).Invoke(boss, null);

        private static void InvokeCollect(PickupDirector director, Pickup pickup)
        {
            var liveField = typeof(PickupDirector).GetField("_live", NonPublicInstance);
            var live = (System.Collections.IList)liveField.GetValue(director);
            int index = live.IndexOf(pickup);
            Assert.GreaterOrEqual(index, 0, "the collected pickup must still be live on the director");
            typeof(PickupDirector).GetMethod("Collect", NonPublicInstance)
                .Invoke(director, new object[] { index, pickup });
        }

        /// <summary>Replicates <c>CanvasScaler.HandleScaleWithScreenSize</c>'s own published formula —
        /// same helper MV960HomeLayoutTests/MV1131FinaleWeaponSignalingTests established for proving a
        /// resolved pixel size under the EditMode test runner.</summary>
        private static Vector2 ExpectedCanvasRefSize(Vector2 screenSize, Vector2 refRes, float match)
        {
            float logWidth = Mathf.Log(screenSize.x / refRes.x, 2f);
            float logHeight = Mathf.Log(screenSize.y / refRes.y, 2f);
            float logAverage = Mathf.Lerp(logWidth, logHeight, match);
            float scaleFactor = Mathf.Pow(2f, logAverage);
            return screenSize / scaleFactor;
        }

        [SetUp]
        public void SetUp()
        {
            foreach (var stray in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            EnemyNavigation.Reset();
            DevMode.Reset();
            DevTuning.Reset();
            HudController.SkipTouchControlsForTests = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var stray in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            foreach (var ring in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(ring.gameObject);
            foreach (var bolt in Object.FindObjectsByType<SentinelBolt>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(bolt.gameObject);
            foreach (var banner in Object.FindObjectsByType<FinaleBanner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(banner.gameObject);
            foreach (var flare in Object.FindObjectsByType<CameraFacingFlare>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(flare.gameObject);
            foreach (var chevron in Object.FindObjectsByType<GroundChevron>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(chevron.gameObject);
            foreach (var wedge in Object.FindObjectsByType<GroundWedge>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(wedge.gameObject);

            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            EnemyNavigation.Reset();
            DevMode.Reset();
            DevTuning.Reset();
            HudController.SkipTouchControlsForTests = false;
        }

        [Test]
        public void ExitBeat_CameraLampBannerThenChevronTrail_MatchTheTicket()
        {
            if (!CoverLayer.Exists) Assert.Ignore("no Cover layer in this project");

            GameObject root = null, pathGo = null, gateGo = null, payoffGo = null, playerGo = null;
            GameObject rigGo = null, hudGo = null;
            try
            {
                // ---------- arrange: World 1's real final area, through the real production pipeline ----------
                WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
                Assert.IsNotNull(cfg, "World 1 failed to load — see the error log above.");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
                WorldTransitions.ApplyExitDoorway(map, cfg, 0);

                root = new GameObject("MV-1125 Root");
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms();

                AreaGate exitGate = built.ExitGate;
                Assert.IsNotNull(exitGate, "World 1 must build a real exit AreaGate");
                InvokeLifecycle(exitGate, "Awake");

                pathGo = new GameObject("MV-1125 BackyardPath");
                var path = pathGo.AddComponent<BackyardPath>();
                SetPrivateField(path, "_cfg", cfg);
                SetPrivateField(path, "_map", map);
                SetPrivateField(path, "_build", built);

                var areaDirector = pathGo.AddComponent<AreaAccumulationDirector>();
                areaDirector.ConfigureWorld(cfg, 0);

                Vector3 doorPosition = exitGate.transform.position;
                Vector3 approach = exitGate.AwayFromPlayerDirection.sqrMagnitude > 1e-4f
                    ? exitGate.AwayFromPlayerDirection.normalized
                    : Vector3.back;

                // AC: Max 5 m from the exit door when the exit beat starts.
                playerGo = new GameObject("MV-1125 Max");
                playerGo.transform.position = doorPosition - approach * 5f;
                playerGo.tag = "Player";
                playerGo.AddComponent<CharacterController>();
                playerGo.AddComponent<PlayerController>();

                rigGo = new GameObject("MV-1125 CameraTargetRig");
                var rig = rigGo.AddComponent<CameraTargetRig>();
                rig.SetSubject(playerGo.transform);

                hudGo = new GameObject("MV-1125 HUD");
                var hud = hudGo.AddComponent<HudController>();
                InvokeLifecycle(hud, "Awake");
                InvokeLifecycle(hud, "OnEnable");

                gateGo = new GameObject("MV-1125 WorldFinaleGate");
                var gate = gateGo.AddComponent<WorldFinaleGate>();
                InvokeLifecycle(gate, "Awake");
                InvokeLifecycle(gate, "OnEnable");

                payoffGo = new GameObject("MV-1125 BossVictoryPayoff");
                var payoff = payoffGo.AddComponent<BossVictoryPayoff>();
                InvokeLifecycle(payoff, "OnEnable");

                int finalAreaIndex = cfg.dials.areaCount;
                List<MonoBehaviour> finalBosses = built.Bosses
                    .Where(b => b != null && map.ZoneAt(b.transform.position.x, b.transform.position.z)?.AreaIndex == finalAreaIndex)
                    .ToList();
                Assert.IsNotEmpty(finalBosses, "World 1 built no boss inside its own final area");
                foreach (MonoBehaviour b in finalBosses)
                    BossCensus.Register(b, "TEST BOSS", phases: 1, current: 100f, max: 100f, areaIndex: finalAreaIndex);

                foreach (MonoBehaviour b in finalBosses) InvokeOnDeath(b);

                var pickupDirector = PickupDirector.EnsureInstalled();
                Pickup core = Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None)
                    .Single(p => p.Kind == PickupKind.WeaponCore);
                InvokeCollect(pickupDirector, core);

                // ---------- Beat A (2.5s, no next-weapon morph assertions needed) -> clean-up (0 robots
                // in this bare fixture) -> straight into Beat B, Max already 5 m from the door ----------
                gate.TickWeaponBeat(2.5f);
                Assert.IsTrue(gate.ExitBeatActive, "fixture: the exit beat must have started by now");

                // ---------- AC(b): the bolt within 0.3 m of the door at t=0.95s (inside 0.9-1.1s), and
                // AC(c)'s "before 1.0s" half: the lamp still reads locked (red) ----------
                gate.TickExitBeat(0.95f);
                SentinelBolt bolt = Object.FindObjectsByType<SentinelBolt>(FindObjectsSortMode.None).SingleOrDefault();
                Assert.IsNotNull(bolt, "AC(b): the exit bolt must still be in flight at t=0.95s");
                float boltDist = Vector3.Distance(bolt.transform.position, doorPosition);
                Assert.That(boltDist, Is.LessThanOrEqualTo(0.3f),
                    $"AC(b): the bolt was {boltDist}m from the door at t=0.95s, over the 0.3m floor");
                Assert.IsFalse(gate.IsOpen, "AC(c): the door must still be shut before t=1.0s");
                Color lampBefore = gate.ExitDoorLampColor;
                Assert.That(lampBefore.r, Is.GreaterThan(lampBefore.g),
                    $"AC(c): the door lamp must read red (locked) before t=1.0s, was {lampBefore}");

                // ---------- AC(a)/(c): at t=1.0s -- camera weight, door open, lamp green ----------
                gate.TickExitBeat(0.05f);
                Assert.IsTrue(gate.IsOpen, "AC(c): the door must open at t=1.0s");
                Assert.That(rig.FocusWeight, Is.GreaterThanOrEqualTo(0.9f),
                    $"AC(a): the camera's own focus weight at t=1.0s was {rig.FocusWeight}, under the 0.9 floor");
                Color lampAfter = gate.ExitDoorLampColor;
                Assert.That(lampAfter.g, Is.GreaterThan(lampAfter.r),
                    $"AC(c): the door lamp must read green (open) after t=1.0s, was {lampAfter}");

                // ---------- AC(d): "EXIT OPEN" on the real scaled HUD canvas, glyph height >= 90px @ 2556x1179 ----------
                var banner = Object.FindObjectsByType<FinaleBanner>(FindObjectsSortMode.None).SingleOrDefault();
                Assert.IsNotNull(banner, "AC(d): the EXIT OPEN banner must be live at t=1.0s");
                Canvas bannerCanvas = banner.Line2.GetComponentInParent<Canvas>();
                Assert.IsNotNull(bannerCanvas, "AC(d): the banner's text must sit under a Canvas");
                Assert.AreEqual(HudController.ActiveCanvas, bannerCanvas,
                    "AC(d): the banner must land on the live HUD's own canvas");
                var scaler = bannerCanvas.GetComponent<CanvasScaler>();
                Assert.IsNotNull(scaler, "AC(d): that Canvas must carry a CanvasScaler");
                Assert.AreEqual("EXIT OPEN", banner.Line2.text, "AC(d): line 2 must read EXIT OPEN");

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
                banner.ShowExitOpen(1f); // re-layout against the resized canvas

                float resolvedPx = banner.Line2.fontSize * scaleFactor;
                Assert.That(resolvedPx, Is.GreaterThanOrEqualTo(90f),
                    $"AC(d): EXIT OPEN's resolved glyph height at 2556x1179 was {resolvedPx}px, under the 90px floor");

                bannerCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                scaler.enabled = true;

                // ---------- AC(e): once the 3.0s beat ends, the objective strip reads GO TO THE EXIT ----------
                gate.TickExitBeat(2.0f); // cumulative t=3.0s -- the beat ends
                Assert.IsFalse(gate.ExitBeatActive, "fixture: the exit beat must have ended by t=3.0s");
                Assert.AreEqual("GO TO THE EXIT", hud.ObjectiveText, "AC(e): the objective strip's own text");
                Color border = hud.ObjectiveBorderColor;
                Assert.That(border.g, Is.GreaterThan(border.r).And.GreaterThan(border.b),
                    $"AC(e): the objective strip's border must read green, was {border}");

                // ---------- AC(f): Max moved to 11 m from the door -> five chevrons on the route line, facing it ----------
                playerGo.transform.position = doorPosition - approach * 11f;
                gate.RefreshExitTrail();
                Assert.AreEqual(5, gate.ExitTrailChevronCount,
                    $"AC(f): expected 5 chevrons at 11m spacing 2m, got {gate.ExitTrailChevronCount}");
                for (int i = 0; i < gate.ExitTrailChevronCount; i++)
                {
                    Vector3 expected = playerGo.transform.position + approach * (2f * (i + 1));
                    float onLine = Vector3.Distance(gate.ExitTrailChevronPosition(i), expected);
                    Assert.That(onLine, Is.LessThanOrEqualTo(0.3f),
                        $"AC(f): chevron {i} was {onLine}m off the route line");
                    float facing = Vector3.Dot(gate.ExitTrailChevronForward(i), approach);
                    Assert.That(facing, Is.GreaterThanOrEqualTo(0.9f),
                        $"AC(f): chevron {i}'s own facing/door-direction dot was {facing}, under the 0.9 floor");
                }

                // ---------- AC(g): once Max crosses the door line, the strip and every chevron are gone ----------
                HudSignals.EmitFinaleGateCrossed();
                Assert.IsFalse(hud.ObjectiveVisible, "AC(g): the objective strip must clear once Max crosses");
                Assert.AreEqual(0, gate.ExitTrailChevronCount, "AC(g): every chevron must clear once Max crosses");
            }
            finally
            {
                if (gateGo != null) { InvokeLifecycle(gateGo.GetComponent<WorldFinaleGate>(), "OnDisable"); Object.DestroyImmediate(gateGo); }
                if (payoffGo != null) { InvokeLifecycle(payoffGo.GetComponent<BossVictoryPayoff>(), "OnDisable"); Object.DestroyImmediate(payoffGo); }
                if (hudGo != null) { InvokeLifecycle(hudGo.GetComponent<HudController>(), "OnDisable"); Object.DestroyImmediate(hudGo); }
                if (rigGo != null) Object.DestroyImmediate(rigGo);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (pathGo != null) Object.DestroyImmediate(pathGo);
                if (root != null) Object.DestroyImmediate(root);
                foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                    Object.DestroyImmediate(stray.gameObject);
                WorldJoinDressing.Clear();
                StormdrainKit.Clear();
                MaterialLibrary.Clear();
                BossCensus.Reset();
            }
        }
    }
}
