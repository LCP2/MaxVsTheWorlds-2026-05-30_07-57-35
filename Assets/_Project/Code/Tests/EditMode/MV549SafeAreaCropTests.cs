using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-549 — THE RIG's left edge clipped on iPhone: the board correctly centres itself on the safe
    /// area (an off-centre rect on a notched device), but phone mode's own content width was a fixed
    /// budget sized against the FULL screen, never the narrower actual safe window, so the outer columns
    /// could still run past the safe area's own edge despite the correct centring.
    /// <see cref="WeaponsScreen.ComputePhoneFitScale"/> is the fix — a phone-only, near-1.0 shrink that
    /// only ever engages when the real safe window is narrower than the content's own envelope. Sole
    /// guard on this defect; do not cull.
    /// </summary>
    public sealed class MV549SafeAreaCropTests
    {
        private GameObject _go;
        private WeaponsScreen _screen;
        private GameObject _camGo;
        private RenderTexture _rt;

        private const float PhoneAspect = 2340f / 1080f;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("WeaponsScreen");
            _screen = _go.AddComponent<WeaponsScreen>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            if (_rt != null) { _rt.Release(); Object.DestroyImmediate(_rt); }
            SafeArea.SimulatedSafeArea = null;
            SafeArea.SimulatedScreenSize = null;
        }

        /// <summary>Builds THE RIG behind a real, controllable-size <c>ScreenSpaceCamera</c> canvas (the
        /// same idiom <c>UiScreensDirector</c> uses for its own real captures) — a <c>ScreenSpaceOverlay</c>
        /// canvas's RectTransform doesn't reliably resize under the EditMode test runner (it stays pinned
        /// to whatever the ambient batchmode window happens to report), which would make an X-axis crop
        /// assertion meaningless. <paramref name="insetPx"/> is applied on a 2340-wide simulated screen —
        /// the same ~6.9% fraction as a real iPhone 14 Pro's own ~59pt notch out of its 852pt landscape
        /// width (59/852*2340 = 162).</summary>
        private void BuildAndMeasure(float insetPx, out float safeMinPx, out float safeMaxPx)
        {
            var simSafeArea = new Rect(insetPx, 0f, 2340f - insetPx, 1080f);
            var simScreen = new Vector2(2340f, 1080f);
            SafeArea.SimulatedSafeArea = simSafeArea;
            SafeArea.SimulatedScreenSize = simScreen;

            _screen.Open();
            var scaler = _screen.RootCanvas.GetComponent<CanvasScaler>();
            scaler.enabled = false;
            _screen.RootCanvas.scaleFactor = 1f;

            _camGo = new GameObject("MV549 Capture Cam", typeof(Camera));
            var cam = _camGo.GetComponent<Camera>();
            _rt = new RenderTexture(2340, 1080, 16);
            cam.targetTexture = _rt;
            cam.aspect = PhoneAspect;

            _screen.RootCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            _screen.RootCanvas.worldCamera = cam;
            _screen.RootCanvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();

            // SafeArea.OnEnable() never fires under the EditMode test runner (no ExecuteAlways, and
            // EditMode tests never enter Play mode) — apply the same pure anchor maths Apply() would
            // have. WeaponsScreen.ApplyBoardScale's own fit-scale reads the simulated statics directly
            // (see ComputePhoneFitScale), so only the RENDERED safe rect needs this manual nudge.
            SafeArea.ComputeAnchors(simSafeArea, simScreen.x, simScreen.y, out var safeMin, out var safeMax);
            _screen.SafeRoot.anchorMin = safeMin;
            _screen.SafeRoot.anchorMax = safeMax;
            _screen.SafeRoot.offsetMin = Vector2.zero;
            _screen.SafeRoot.offsetMax = Vector2.zero;
            Canvas.ForceUpdateCanvases();

            _screen.ApplyBoardScale(PhoneAspect);
            Canvas.ForceUpdateCanvases();

            Vector2 Px(Vector3 world) => RectTransformUtility.WorldToScreenPoint(cam, world);

            var safeCorners = new Vector3[4];
            _screen.SafeRoot.GetWorldCorners(safeCorners);
            safeMinPx = Px(safeCorners[0]).x;
            safeMaxPx = Px(safeCorners[2]).x;
        }

        [Test]
        public void PhoneBoardNeverCropsPastTheSafeAreaOnX_AtAnyInset()
        {
            // ---------------------------------------------------------------- AC2: asymmetric notch
            BuildAndMeasure(162f, out float safeMinAsym, out float safeMaxAsym);
            AssertEveryNodeFitsWithin(safeMinAsym, safeMaxAsym, "asymmetric (162px notch)");

            _screen.Close();
            Object.DestroyImmediate(_go);
            Object.DestroyImmediate(_camGo);
            _rt.Release();
            Object.DestroyImmediate(_rt);

            // ---------------------------------------------------------------- AC3: symmetric/no notch
            _go = new GameObject("WeaponsScreen");
            _screen = _go.AddComponent<WeaponsScreen>();
            BuildAndMeasure(0f, out float safeMinSym, out float safeMaxSym);
            AssertEveryNodeFitsWithin(safeMinSym, safeMaxSym, "symmetric (no notch)");
            Assert.That(_screen.BoardScale, Is.EqualTo(1f).Within(1e-4f),
                "fixture: a full-width safe area must never engage the phone fit-scale");
        }

        private void AssertEveryNodeFitsWithin(float safeMinPx, float safeMaxPx, string label)
        {
            var cam = _camGo.GetComponent<Camera>();
            Vector2 Px(Vector3 world) => RectTransformUtility.WorldToScreenPoint(cam, world);
            const float epsilon = 0.5f;   // sub-pixel slop, same idiom as RigBoardChromeTests' own window-fit checks

            void Check(string id, RectTransform rt)
            {
                if (rt == null) return;
                var c = new Vector3[4];
                rt.GetWorldCorners(c);
                foreach (var corner in c)
                {
                    float px = Px(corner).x;
                    Assert.That(px, Is.GreaterThanOrEqualTo(safeMinPx - epsilon),
                        $"'{id}' left edge at {px:0.0}px crops past the safe area's own left edge ({safeMinPx:0.0}px) — {label}");
                    Assert.That(px, Is.LessThanOrEqualTo(safeMaxPx + epsilon),
                        $"'{id}' right edge at {px:0.0}px crops past the safe area's own right edge ({safeMaxPx:0.0}px) — {label}");
                }
            }

            foreach (var cat in RigBoardLayout.PhoneCategories)
            {
                Check(cat.Id, _screen.BoardNode(cat.Id));
                Check(cat.Id + " panel", _screen.CategoryPanel(cat.Id)?.rectTransform);
            }
            foreach (var ab in RigBoardLayout.PhoneAbilities) Check(ab.Id, _screen.BoardNode(ab.Id));
            foreach (var fu in RigBoardLayout.PhoneFusions) Check(fu.Id, _screen.BoardNode(fu.Id));
        }
    }
}
