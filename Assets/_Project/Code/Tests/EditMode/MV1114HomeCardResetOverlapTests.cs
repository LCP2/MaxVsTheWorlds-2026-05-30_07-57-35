using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Save;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1114 (the one new test, per CC_AUTONOMY's testing policy): on each slot card, RESET's own
    /// rect (top-right, stage-local y 20..120 ref units) and the old single "Status" Text's rect
    /// (y 96..204) overlapped by 24 ref units — Lee's device screenshot (MV-1114.png) shows RESET
    /// sitting over the end of line 1, clipping "...of 30".
    ///
    /// The ticket names base commit d30d293, but that commit predates <c>HomeScreen.StartDevWorld</c>
    /// (MV-1180) and no longer compiles against today's tree — <c>DevStartWalletFloorTests.cs</c> and
    /// <c>CityDevStartTests.cs</c> both call it. Measured instead, fail-first, against this branch's own
    /// pre-fix tip (ce72c24, the actual "bug present" state, which does compile): one failure,
    /// <c>SlotCardStatusLines_NeverOverlapReset_AndFloorAtEleven_AcrossDeviceSizes</c> —
    /// "fixture: slot 0's Status block must carry a 'Status Line 1' text / Expected: not null / But
    /// was: null" — <c>HomeScreen.BuildCard</c> there still parents one "Status" Text directly (no
    /// "Status Line N" children), so this test's own structural lookup fails outright; the old layout
    /// has nothing resembling the three independent, below-RESET lines this ticket adds.
    ///
    /// Builds the real Home screen at three device screen sizes (2556x1179, 2340x1080, 1334x750 — the
    /// ticket's own resolutions), same World Space hand-sizing idiom as <see cref="MV960HomeLayoutTests"/>
    /// (a Screen Space Overlay canvas's RectTransform does not reliably resize under the EditMode test
    /// runner). No per-device safe-area inset is authored for the two non-iPhone sizes, so the safe
    /// area is treated as the full canvas (0 inset) at every size — the loosest bound available, not a
    /// fabricated notch.
    ///
    /// Feeds worst-case-but-real data through the real entry points (<c>SaveSystem.Save</c> +
    /// <c>HomeScreen.Open</c>/<c>Build</c>) rather than the ticket's own literal example strings: the
    /// ticket was filed before MV-1176 inserted the City as World 3 (bumping the Reef to World 4), and
    /// its "Played 999 min" example is not what <c>FormatPlayedTime</c> actually emits past 59 minutes
    /// (it switches to "H h MM min") — both are Hypothesis-labelled prose, not observation, so this
    /// test measures the real resolved rects/text instead of asserting either literal (Testing policy
    /// v2: no asserting an authored constant). Slot 0 exercises the "run in progress" status branch
    /// with the longest real world name + area count (Stormdrain, MV-1141's own catalog) and two-digit
    /// deaths/hours; slot 1 exercises the "no run in progress" branch with the same long world name and
    /// the furthest-reachable world (the Reef); slot 2 is left Empty, the shortest/lightest real case.
    /// </summary>
    public sealed class MV1114HomeCardResetOverlapTests
    {
        private string _dir;
        private GameObject _go;
        private HomeScreen _home;

        private static readonly (float W, float H)[] ScreenSizes =
        {
            (2556f, 1179f),
            (2340f, 1080f),
            (1334f, 750f),
        };
        private static readonly Vector2 RefRes = new Vector2(1920f, 1080f);
        private const float Match = 0.5f;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-save-tests-mv1114");
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

        /// <summary>Replicates <c>CanvasScaler.HandleScaleWithScreenSize</c>'s own published formula,
        /// same helper as <see cref="MV960HomeLayoutTests"/>.</summary>
        private static Vector2 ExpectedCanvasRefSize(Vector2 screenSize, Vector2 refRes, float match)
        {
            float logWidth = Mathf.Log(screenSize.x / refRes.x, 2f);
            float logHeight = Mathf.Log(screenSize.y / refRes.y, 2f);
            float logAverage = Mathf.Lerp(logWidth, logHeight, match);
            float scaleFactor = Mathf.Pow(2f, logAverage);
            return screenSize / scaleFactor;
        }

        private static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
        }

        private static bool Overlaps(Rect a, Rect b, float epsilon) =>
            a.xMin < b.xMax - epsilon && a.xMax > b.xMin + epsilon &&
            a.yMin < b.yMax - epsilon && a.yMax > b.yMin + epsilon;

        [Test]
        public void SlotCardStatusLines_NeverOverlapReset_AndFloorAtEleven_AcrossDeviceSizes()
        {
            int stormdrainIndex = WorldCatalog.IndexOf(WorldIds.Stormdrain);
            int reefIndex = WorldCatalog.IndexOf(WorldIds.Reef);
            WorldConfig stormdrain = WorldLibrary.Load(WorldLibrary.KeyForIndex(stormdrainIndex));
            Assert.That(stormdrain, Is.Not.Null, "fixture: Stormdrain's config must load");
            int stormdrainAreaCount = stormdrain.dials.areaCount;

            SaveSystem.Save(0, new SaveSlotData
            {
                HasData = true,
                DisplayName = "TEST",
                HasRunInProgress = true,
                CheckpointWorldIndex = stormdrainIndex,
                CheckpointAreaIndex = stormdrainAreaCount - 1,   // two-digit, the longest real area-of-N
                CheckpointElapsedSeconds = (99 * 3600) + (59 * 60),   // real FormatPlayedTime -> "99 h 59 min"
                CheckpointDeathsTaken = 99,
                BestDeathsToVictory = 99,
            });
            SaveSystem.Save(1, new SaveSlotData
            {
                HasData = true,
                DisplayName = "TEST",
                HasRunInProgress = false,
                WorldIndex = stormdrainIndex,
                BestDeathsToVictory = 99,
                FurthestWorldIndex = reefIndex,
            });
            // Slot 2 stays Empty (SetUp already deleted every slot) — the lightest real case.

            foreach ((float screenW, float screenH) in ScreenSizes)
            {
                _go = new GameObject("HomeScreen");
                _home = _go.AddComponent<HomeScreen>();
                _home.Open();

                var canvas = _home.GetComponentInChildren<Canvas>(true);
                var scaler = canvas.GetComponent<CanvasScaler>();
                scaler.enabled = false;   // stop it re-driving the canvas rect once we hand-size it below

                canvas.renderMode = RenderMode.WorldSpace;
                var canvasRt = (RectTransform)canvas.transform;
                canvasRt.anchorMin = canvasRt.anchorMax = new Vector2(0.5f, 0.5f);
                canvasRt.pivot = new Vector2(0.5f, 0.5f);
                var screenSize = new Vector2(screenW, screenH);
                canvasRt.sizeDelta = ExpectedCanvasRefSize(screenSize, RefRes, Match);

                var safeArea = _home.GetComponentInChildren<SafeArea>(true);
                var safeRoot = (RectTransform)safeArea.transform;
                // No authored per-device inset for 2340x1080/1334x750 — the safe area is the full
                // canvas (0 inset) at every size here, the loosest bound available rather than a guess.
                safeRoot.anchorMin = Vector2.zero;
                safeRoot.anchorMax = Vector2.one;
                safeRoot.offsetMin = Vector2.zero;
                safeRoot.offsetMax = Vector2.zero;

                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRt);

                var stage = safeRoot.Find("Stage") as RectTransform;
                Assert.That(stage, Is.Not.Null, "fixture: HomeScreen.Build must parent a 'Stage' rect directly under Safe Area");

                // Build() computed the stage's own scale against whatever safe rect existed at
                // construction time (the ambient EditMode window) — re-derive and re-apply it now that
                // the real safe rect is in place, exactly as a live Apply()-driven resize would.
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
                            $"[{screenW}x{screenH}] '{label}' left edge at {corner.x:0.0} crops past the safe area's left edge ({safeMin.x:0.0})");
                        Assert.That(corner.x, Is.LessThanOrEqualTo(safeMax.x + epsilon),
                            $"[{screenW}x{screenH}] '{label}' right edge at {corner.x:0.0} crops past the safe area's right edge ({safeMax.x:0.0})");
                        Assert.That(corner.y, Is.GreaterThanOrEqualTo(safeMin.y - epsilon),
                            $"[{screenW}x{screenH}] '{label}' bottom edge at {corner.y:0.0} crops past the safe area's bottom edge ({safeMin.y:0.0})");
                        Assert.That(corner.y, Is.LessThanOrEqualTo(safeMax.y + epsilon),
                            $"[{screenW}x{screenH}] '{label}' top edge at {corner.y:0.0} crops past the safe area's top edge ({safeMax.y:0.0})");
                    }
                }

                for (int slot = 0; slot < 3; slot++)
                {
                    var card = stage.Find($"Card {slot + 1}") as RectTransform;
                    Assert.That(card, Is.Not.Null, $"fixture: slot {slot}'s card must be named 'Card {slot + 1}'");

                    var resetRt = card.Find("RESET") as RectTransform;
                    Assert.That(resetRt, Is.Not.Null, $"fixture: slot {slot}'s card must carry a 'RESET' button");
                    AssertInsideSafeArea($"Card {slot + 1} RESET", resetRt);
                    Rect resetWorld = WorldRect(resetRt);

                    var statusRoot = card.Find("Status") as RectTransform;
                    Assert.That(statusRoot, Is.Not.Null, $"fixture: slot {slot}'s card must carry a 'Status' block");

                    for (int li = 1; li <= 3; li++)
                    {
                        var lineRt = statusRoot.Find($"Status Line {li}") as RectTransform;
                        Assert.That(lineRt, Is.Not.Null,
                            $"fixture: slot {slot}'s Status block must carry a 'Status Line {li}' text");
                        var lineText = lineRt.GetComponent<Text>();
                        Assert.That(lineText, Is.Not.Null, $"slot {slot}'s 'Status Line {li}' must carry a Text");

                        AssertInsideSafeArea($"Card {slot + 1} Status Line {li}", lineRt);

                        Assert.That(Overlaps(resetWorld, WorldRect(lineRt), epsilon), Is.False,
                            $"[{screenW}x{screenH}] slot {slot}'s 'Status Line {li}' ({WorldRect(lineRt)}) intersects RESET ({resetWorld}) — text: \"{lineText.text}\"");

                        Assert.That(lineText.fontSize, Is.GreaterThanOrEqualTo(11),
                            $"[{screenW}x{screenH}] slot {slot}'s 'Status Line {li}' resolved fontSize {lineText.fontSize} is under the 11pt best-fit floor");
                    }
                }

                Object.DestroyImmediate(_go);
                _go = null;
            }
        }
    }
}
