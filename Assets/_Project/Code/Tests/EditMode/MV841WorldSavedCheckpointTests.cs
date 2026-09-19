using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Save;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-841 (the one new test, per CC_AUTONOMY's testing policy): the World-complete screen's
    /// stats cover the WHOLE WORLD, across a death and a checkpoint restore — not just what happened
    /// since the checkpoint bridge was last seeded. Fails on base commit 10fce02:
    /// <c>RunStats.Elapsed</c>/<c>Kills</c> were never checkpointed (only
    /// <c>DeathRunState.DeathsTaken</c> was), so a checkpoint restore silently reset the run clock
    /// and kill count to zero even though DEATHS correctly carried over, and the banner still read
    /// "VICTORY" rather than "WORLD &lt;name&gt; SAVED".
    /// </summary>
    public sealed class MV841WorldSavedCheckpointTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv841-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 0 });

            RunProgressState.Reset();
            DeathRunState.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var rs in UnityEngine.Object.FindObjectsByType<MaxWorlds.UI.ResultScreen>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(rs.gameObject);
            var es = UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (es != null) UnityEngine.Object.DestroyImmediate(es.gameObject);

            RunProgressState.Reset();
            DeathRunState.Reset();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;   // ResultScreen.Show() freezes the game
            ModalFrameRateGate.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void CheckpointRestore_CarriesElapsedKillsAndDeaths_IntoTheResultScreen()
        {
            // Kills=5, elapsed=60s, then a death — the whole-world tally a checkpoint must preserve.
            var live = new MaxWorlds.UI.RunStats();
            live.Tick(60f);
            for (int i = 0; i < 5; i++) live.AddKill();
            RunProgressState.Sync(live.Elapsed, live.Kills);
            DeathRunState.RecordDeath();

            SaveSystem.CaptureCheckpoint(0, areaIndex: 3);

            // Disturb live state the same way a fresh process / scene reload would, so a passing
            // restore is provably doing the work, not reading state that never actually reset.
            RunProgressState.Reset();
            DeathRunState.Reset();

            bool restored = SaveSystem.RestoreCheckpoint(0);
            Assert.IsTrue(restored, "a captured checkpoint must report as restored");

            // A freshly constructed RunStats — exactly what a new RunTracker.OnEnable seeds itself
            // with after a checkpoint restore — must read back the world's whole tally, not zero.
            var resumed = new MaxWorlds.UI.RunStats();
            resumed.Restore(RunProgressState.Elapsed, RunProgressState.Kills);
            resumed.MarkFactoryDestroyed();

            var go = new GameObject("ResultScreen Test");
            var screen = go.AddComponent<MaxWorlds.UI.ResultScreen>();
            screen.Show(resumed);

            var texts = go.GetComponentsInChildren<Text>(true);

            string ValueAfterLabel(string label)
            {
                int i = Array.FindIndex(texts, t => t.text == label);
                Assert.GreaterOrEqual(i, 0, $"expected a '{label}' row label");
                return texts[i + 1].text;
            }

            Assert.AreEqual("1", ValueAfterLabel("DEATHS"), "the death taken before the restore must survive it");
            Assert.AreEqual("5", ValueAfterLabel("ROBOTS DESTROYED"), "kills must round-trip through the checkpoint");

            int seconds = ParseSeconds(ValueAfterLabel("TIME"));
            Assert.GreaterOrEqual(seconds, 60, "elapsed time must round-trip through the checkpoint");

            // The banner is the first Text built (before any stat row), and now names the world saved.
            string title = texts[0].text;
            Assert.IsTrue(title.StartsWith("WORLD ") && title.EndsWith(" SAVED"),
                $"title should read WORLD <name> SAVED, was '{title}'");
        }

        private static int ParseSeconds(string mmss)
        {
            string[] parts = mmss.Split(':');
            return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
        }
    }
}
