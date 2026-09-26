using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Save;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-960 — the Home panel (<c>HomeScreen.Build</c>, <c>Center(panel, 1200f, 990f)</c>) was taller
    /// than an iPhone landscape safe area (2556x1179px, CanvasScaler 1920x1080 match 0.5, canvas ~979
    /// ref units tall before insets): the panel overflowed, and RESUME/PLAY/RESET/WORLD 2/WORLD 3 all
    /// resolved under the 100-ref-unit (44pt) tap-target floor. <see cref="HomeScreen.StageScale"/> plus
    /// the new 1880x920 stage fix both — <c>StageScale</c> did not exist on the base commit, so it and
    /// every layout assertion below were unreachable there (same "new API is its own base-commit
    /// failure" bar MV-524's own test used, Testing policy v2 Rule 1); the resolved-rect assertions
    /// independently fail against the OLD 990-tall/56-tall-button layout at this exact safe size (RESUME
    /// 56 short of 100, PLAY 40 short, RESET 40 short — quoted in the fix comment). Sole guard on this
    /// defect; do not cull.
    ///
    /// Builds the real Home screen (Tier 2: resolved rects after <c>LayoutRebuilder.ForceRebuildLayoutImmediate</c>),
    /// then switches its canvas to World Space and hand-sizes it to the exact ref-unit rect
    /// <c>CanvasScaler.ScaleWithScreenSize</c> would compute for a 2556x1179px screen (1920x1080 ref,
    /// match 0.5) — a Screen Space Overlay canvas's own RectTransform does not reliably resize under the
    /// EditMode test runner (see <c>MV549SafeAreaCropTests</c>), so this sidesteps that entirely rather
    /// than fighting it. <see cref="SafeArea.ComputeAnchors"/> (the same pure helper production code
    /// uses) then derives the safe-area anchors for a 141px left/right, 63px bottom inset.
    /// </summary>
    public sealed class MV960HomeLayoutTests
    {
        private string _dir;
        private GameObject _go;
        private HomeScreen _home;

        private static readonly Vector2 ScreenSize = new Vector2(2556f, 1179f);
        private static readonly Vector2 RefRes = new Vector2(1920f, 1080f);
        private const float Match = 0.5f;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-save-tests-mv960");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            for (int i = 0; i < SaveSystem.SlotCount; i++) SaveSystem.Delete(i);   // deterministic: every card starts Empty
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            SaveSystem.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            Time.timeScale = 1f;
        }

        /// <summary>Replicates <c>CanvasScaler.HandleScaleWithScreenSize</c>'s own published formula —
        /// the exact ref-unit size the real canvas would resolve to on this screen/reference/match,
        /// had CanvasScaler been able to run against it under EditMode.</summary>
        private static Vector2 ExpectedCanvasRefSize(Vector2 screenSize, Vector2 refRes, float match)
        {
            float logWidth = Mathf.Log(screenSize.x / refRes.x, 2f);
            float logHeight = Mathf.Log(screenSize.y / refRes.y, 2f);
            float logAverage = Mathf.Lerp(logWidth, logHeight, match);
            float scaleFactor = Mathf.Pow(2f, logAverage);
            return screenSize / scaleFactor;
        }

        [Test]
        public void StageScale_Is1WhenSafeAreaFitsTheStage_AndShrinksWhenItDoesNot()
        {
            Assert.That(HomeScreen.StageScale(new Vector2(1880f, 920f)), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(HomeScreen.StageScale(new Vector2(3000f, 2000f)), Is.EqualTo(1f).Within(1e-4f),
                "never upscaled past 1 even when the safe area is much bigger than the stage");

            float narrow = HomeScreen.StageScale(new Vector2(940f, 920f));
            Assert.That(narrow, Is.EqualTo(940f / 1880f).Within(1e-4f));

            float short_ = HomeScreen.StageScale(new Vector2(1880f, 460f));
            Assert.That(short_, Is.EqualTo(460f / 920f).Within(1e-4f));

            Assert.That(HomeScreen.StageScale(Vector2.zero), Is.EqualTo(1f),
                "degenerate zero-size input must not divide by zero or collapse the stage");
        }

        [Test]
        public void HomeStageAndButtonsFitAnIPhoneLandscapeSafeArea()
        {
            _go = new GameObject("HomeScreen");
            _home = _go.AddComponent<HomeScreen>();
            _home.Open();

            var canvas = _home.GetComponentInChildren<Canvas>(true);
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.enabled = false;   // stop it re-driving the canvas rect once we hand-size it below

            // World Space, unlike Screen Space Overlay/Camera, is never auto-driven to the ambient
            // window size — its RectTransform behaves like any other, so it can be hand-sized to
            // exactly what CanvasScaler would have produced on the real device.
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRt = (RectTransform)canvas.transform;
            canvasRt.anchorMin = canvasRt.anchorMax = new Vector2(0.5f, 0.5f);
            canvasRt.pivot = new Vector2(0.5f, 0.5f);
            canvasRt.sizeDelta = ExpectedCanvasRefSize(ScreenSize, RefRes, Match);

            var safeArea = _home.GetComponentInChildren<SafeArea>(true);
            var safeRoot = (RectTransform)safeArea.transform;
            var safeAreaPx = new Rect(141f, 63f, ScreenSize.x - 141f * 2f, ScreenSize.y - 63f);
            SafeArea.ComputeAnchors(safeAreaPx, ScreenSize.x, ScreenSize.y, out var anchorMin, out var anchorMax);
            safeRoot.anchorMin = anchorMin;
            safeRoot.anchorMax = anchorMax;
            safeRoot.offsetMin = Vector2.zero;
            safeRoot.offsetMax = Vector2.zero;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRt);

            var stage = safeRoot.Find("Stage") as RectTransform;
            Assert.That(stage, Is.Not.Null, "fixture: HomeScreen.Build must parent a 'Stage' rect directly under Safe Area");

            // Build() computed the stage's own scale against whatever safe rect existed at construction
            // time (the ambient EditMode window, not our simulated iPhone) — re-derive and re-apply it
            // now that the real safe rect is in place, exactly as a live Apply()-driven resize would.
            float scale = HomeScreen.StageScale(safeRoot.rect.size);
            stage.localScale = new Vector3(scale, scale, 1f);
            Canvas.ForceUpdateCanvases();

            var safeCorners = new Vector3[4];
            safeRoot.GetWorldCorners(safeCorners);
            Vector3 safeMin = safeCorners[0], safeMax = safeCorners[2];
            const float epsilon = 0.5f;

            void AssertInsideSafeArea(string label, RectTransform rt)
            {
                var c = new Vector3[4];
                rt.GetWorldCorners(c);
                foreach (var corner in c)
                {
                    Assert.That(corner.x, Is.GreaterThanOrEqualTo(safeMin.x - epsilon),
                        $"'{label}' left edge at {corner.x:0.0} crops past the safe area's left edge ({safeMin.x:0.0})");
                    Assert.That(corner.x, Is.LessThanOrEqualTo(safeMax.x + epsilon),
                        $"'{label}' right edge at {corner.x:0.0} crops past the safe area's right edge ({safeMax.x:0.0})");
                    Assert.That(corner.y, Is.GreaterThanOrEqualTo(safeMin.y - epsilon),
                        $"'{label}' bottom edge at {corner.y:0.0} crops past the safe area's bottom edge ({safeMin.y:0.0})");
                    Assert.That(corner.y, Is.LessThanOrEqualTo(safeMax.y + epsilon),
                        $"'{label}' top edge at {corner.y:0.0} crops past the safe area's top edge ({safeMax.y:0.0})");
                }
            }

            AssertInsideSafeArea("Stage", stage);

            var buttons = safeRoot.GetComponentsInChildren<Button>(true);
            Assert.That(buttons.Length, Is.GreaterThan(0), "fixture: expected at least SETTINGS/PLAY/RESET/dev buttons");

            foreach (var b in buttons) AssertInsideSafeArea(b.name, (RectTransform)b.transform);

            foreach (var b in buttons)
            {
                if (b.name != "RESUME" && b.name != "PLAY" && b.name != "RESET") continue;
                var c = new Vector3[4];
                ((RectTransform)b.transform).GetWorldCorners(c);
                float height = c[1].y - c[0].y;
                Assert.That(height, Is.GreaterThanOrEqualTo(100f - epsilon),
                    $"'{b.name}' resolves {height:0.0} ref units tall, under the 100-unit (44pt) tap-target floor");
            }

            Rect WorldRect(RectTransform rt)
            {
                var c = new Vector3[4];
                rt.GetWorldCorners(c);
                return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
            }

            for (int i = 0; i < buttons.Length; i++)
            for (int j = i + 1; j < buttons.Length; j++)
            {
                Rect a = WorldRect((RectTransform)buttons[i].transform);
                Rect b = WorldRect((RectTransform)buttons[j].transform);
                bool overlap = a.xMin < b.xMax - epsilon && a.xMax > b.xMin + epsilon &&
                               a.yMin < b.yMax - epsilon && a.yMax > b.yMin + epsilon;
                Assert.That(overlap, Is.False,
                    $"'{buttons[i].name}' overlaps '{buttons[j].name}' ({a} vs {b})");
            }
        }
    }
}
