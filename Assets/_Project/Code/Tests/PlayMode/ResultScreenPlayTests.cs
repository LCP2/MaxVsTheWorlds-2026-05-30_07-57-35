using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.EventSystems;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// PlayMode coverage for the Result screen (YT-31). Exercises the real runtime construction
    /// path — RunTracker ending the run, ResultScreen building its canvas + EventSystem +
    /// InputSystemUIInputModule and pausing — which the input-less standalone smoke can't reach.
    /// </summary>
    public sealed class ResultScreenPlayTests
    {
        private GameObject _tracker;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            foreach (var name in new[] { "RunTracker Test", "Result Screen" })
            {
                var go = GameObject.Find(name);
                if (go != null) Object.Destroy(go);
            }
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.name == "Result Canvas") Object.Destroy(canvas.gameObject);
            var es = Object.FindFirstObjectByType<EventSystem>();
            if (es != null) Object.Destroy(es.gameObject);
            DevTuning.Reset();
            DifficultyDirector.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator BossDefeated_HoldsTheCardForThePayoff_ThenShowsVictoryAndPauses()
        {
            _tracker = new GameObject("RunTracker Test");
            _tracker.AddComponent<RunTracker>();
            yield return null; // let Awake/OnEnable subscribe

            HudSignals.EmitEnemyKilled(Vector3.zero);
            HudSignals.EmitFactoryDestroyed(Vector3.zero);
            HudSignals.EmitBossDefeated(); // Victory sealed — but the card is held for the payoff (YT-152)
            yield return null;

            bool CardIsUp() => Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Any(c => c.name == "Result Canvas");

            // The win no longer cuts straight to results: the blow-up + flung parts + walk-out play first.
            Assert.IsFalse(CardIsUp(), "Results must be held back until the boss-death payoff finishes.");
            // The world is still live (a brief death hit-stop may dip timeScale, but the results freeze —
            // timeScale 0 — must NOT have landed yet).
            Assert.Greater(Time.timeScale, 0f, "The results freeze must not land until the payoff finishes.");

            // The payoff completes (Max walked through the exit gate, or it timed out) — now the card lands.
            HudSignals.EmitBossPayoffFinished();
            yield return null;

            var canvas = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .FirstOrDefault(c => c.name == "Result Canvas");
            Assert.IsNotNull(canvas, "Result canvas should build once the payoff finishes.");
            Assert.AreEqual(0f, Time.timeScale, "Result screen should pause the game.");
            Assert.IsNotNull(Object.FindFirstObjectByType<EventSystem>(),
                "An EventSystem should exist so the Result buttons are clickable.");

            // The VICTORY banner text should be present somewhere in the card.
            var texts = canvas.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            Assert.IsTrue(texts.Any(t => t.text == "VICTORY"), "Victory banner should read VICTORY.");
        }

        // MV-427: Max dying no longer seals the run or shows this screen at all — death now
        // continues the run (WorldRunner respawns him one area back). The three tests that used to
        // live here (PlayerDeath_ShowsDefeatScreen, the near-miss card, and the CTA-layout proof
        // against a two-button REPLAY/NEXT WORLD row) tested exactly that removed behaviour and were
        // deleted rather than adapted — there is no Defeat card left to assert against.
    }
}
