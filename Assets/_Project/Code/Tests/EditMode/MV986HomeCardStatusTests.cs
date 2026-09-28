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
    /// MV-986 (the one new test, per CC_AUTONOMY's testing policy): a slot card carrying a run in
    /// progress showed only "no finished run yet" / "best: 1 death" and "Run in progress - Area 16" —
    /// it never named which world the run was in, the area-of-N, or how long the run had been played.
    /// Fails on base commit 54b237a: the old <c>HomeScreen.Summarise</c>/<c>BuildCard</c> produced
    /// "no finished run yet\nRun in progress - Area 16" for this exact fixture, not the three-line
    /// World/Played/Best block this ticket adds (quoted in the fix comment).
    ///
    /// Builds the real Home screen (Tier 2: resolved rect/text after
    /// <c>LayoutRebuilder.ForceRebuildLayoutImmediate</c>), same idiom as
    /// <see cref="MV960HomeLayoutTests"/>, then reads slot 0's card status <c>Text</c> by name.
    /// "Stormdrain" and its area count are read from the real World 2 config, not hard-coded, so this
    /// never drifts from the authored data (Testing policy v2: no asserting an authored constant).
    /// </summary>
    public sealed class MV986HomeCardStatusTests
    {
        private string _dir;
        private GameObject _go;
        private HomeScreen _home;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-save-tests-mv986");
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

        [Test]
        public void RunInProgressCard_ShowsWorld_AreaOfN_PlayedTime_AndBest()
        {
            SaveSystem.Save(0, new SaveSlotData
            {
                HasData = true,
                DisplayName = "TEST",
                HasRunInProgress = true,
                CheckpointWorldIndex = 1,      // World 2 (0-based)
                CheckpointAreaIndex = 16,
                CheckpointElapsedSeconds = 1415f,   // 23 min 35s -> "23 min"
                CheckpointDeathsTaken = 4,
                BestDeathsToVictory = 1,
            });

            _go = new GameObject("HomeScreen");
            _home = _go.AddComponent<HomeScreen>();
            _home.Open();

            var safeArea = _home.GetComponentInChildren<SafeArea>(true);
            Assert.That(safeArea, Is.Not.Null, "fixture: HomeScreen.Build must add a SafeArea");

            var stage = safeArea.transform.Find("Stage") as RectTransform;
            Assert.That(stage, Is.Not.Null, "fixture: HomeScreen.Build must parent a 'Stage' rect under Safe Area");

            var card = stage.Find("Card 1") as RectTransform;
            Assert.That(card, Is.Not.Null, "fixture: slot 0's card must be named 'Card 1'");

            var statusRt = card.Find("Status") as RectTransform;
            var status = statusRt != null ? statusRt.GetComponent<Text>() : null;
            Assert.That(status, Is.Not.Null, "the card's status text must be reachable by name 'Status'");

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(statusRt);

            WorldConfig world2 = WorldLibrary.Load(WorldLibrary.KeyForIndex(1));
            Assert.That(world2, Is.Not.Null, "fixture: World 2's config must load");
            string worldName = world2.world.Substring(world2.world.IndexOf('—') + 1).Trim();
            int areaCount = world2.dials.areaCount;

            string expected = $"World 2: {worldName} - Area 16 of {areaCount}\nPlayed 23 min - 4 deaths\nBest: 1 death";
            Assert.That(status.text, Is.EqualTo(expected));

            Assert.That(status.preferredHeight, Is.LessThanOrEqualTo(statusRt.rect.height),
                $"status text ({status.preferredHeight:0.0} tall) overflows its rect ({statusRt.rect.height:0.0} tall)");
        }
    }
}
