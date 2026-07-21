using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.EventSystems;
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

        [UnityTest]
        public IEnumerator PlayerDeath_ShowsDefeatScreen()
        {
            _tracker = new GameObject("RunTracker Test");
            var health = _tracker.AddComponent<MaxWorlds.Player.PlayerHealth>();
            _tracker.AddComponent<RunTracker>();
            yield return null;

            // Kill Max: full damage from an enemy attacker.
            health.TakeDamage(new MaxWorlds.Core.DamageInfo(9999f, Vector3.zero, Vector3.forward,
                MaxWorlds.Core.Team.Enemy));
            yield return null;

            var canvas = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .FirstOrDefault(c => c.name == "Result Canvas");
            Assert.IsNotNull(canvas, "Result canvas should appear on death.");
            var texts = canvas.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            Assert.IsTrue(texts.Any(t => t.text == "DEFEAT"), "Death should read DEFEAT.");
        }

        /// <summary>
        /// YT-81: on the DEFEAT card the REPLAY button hung outside the panel's left edge. Asserted
        /// against the REAL built canvas in rendered world space — not against the layout arithmetic,
        /// which is capable of being perfectly self-consistent while the screen still builds itself
        /// wrong. This is the test that would have caught the original bug.
        /// </summary>
        [UnityTest]
        public IEnumerator DefeatScreen_BothCtasAreContainedByThePanel()
        {
            _tracker = new GameObject("RunTracker Test");
            var health = _tracker.AddComponent<MaxWorlds.Player.PlayerHealth>();
            _tracker.AddComponent<RunTracker>();
            yield return null;

            health.TakeDamage(new MaxWorlds.Core.DamageInfo(9999f, Vector3.zero, Vector3.forward,
                MaxWorlds.Core.Team.Enemy));
            yield return null;

            Canvas.ForceUpdateCanvases();
            yield return null;

            var canvas = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .FirstOrDefault(c => c.name == "Result Canvas");
            Assert.IsNotNull(canvas, "Result canvas should appear on death.");

            var panel = canvas.GetComponentsInChildren<RectTransform>(true)
                .FirstOrDefault(r => r.name == "Panel");
            Assert.IsNotNull(panel, "the results panel should exist");

            var buttons = canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            Assert.AreEqual(2, buttons.Length, "expected exactly the REPLAY and NEXT WORLD CTAs");

            var panelCorners = new Vector3[4];
            panel.GetWorldCorners(panelCorners);   // 0 = bottom-left, 2 = top-right
            float panelLeft = panelCorners[0].x, panelRight = panelCorners[2].x;
            float panelBottom = panelCorners[0].y, panelTop = panelCorners[2].y;

            foreach (var button in buttons)
            {
                var corners = new Vector3[4];
                ((RectTransform)button.transform).GetWorldCorners(corners);
                float left = corners[0].x, right = corners[2].x;
                float bottom = corners[0].y, top = corners[2].y;

                Assert.GreaterOrEqual(left, panelLeft - 0.01f,
                    $"'{button.name}' overhangs the panel's LEFT edge — YT-81 is back");
                Assert.LessOrEqual(right, panelRight + 0.01f,
                    $"'{button.name}' overhangs the panel's RIGHT edge");
                Assert.GreaterOrEqual(bottom, panelBottom - 0.01f,
                    $"'{button.name}' hangs below the panel");
                Assert.LessOrEqual(top, panelTop + 0.01f,
                    $"'{button.name}' pokes out of the top of the panel");
            }

            // …and they line up with each other, rather than merely both being inside.
            var a = new Vector3[4]; var b = new Vector3[4];
            ((RectTransform)buttons[0].transform).GetWorldCorners(a);
            ((RectTransform)buttons[1].transform).GetWorldCorners(b);
            Assert.AreEqual(a[0].y, b[0].y, 0.01f, "the two CTAs sit on different baselines");
            Assert.AreEqual(a[2].y - a[0].y, b[2].y - b[0].y, 0.01f, "the CTAs are different heights");

            // Even margins: the gap from each button to its nearest panel edge should match.
            float leftInset = Mathf.Min(a[0].x, b[0].x) - panelLeft;
            float rightInset = panelRight - Mathf.Max(a[2].x, b[2].x);
            Assert.AreEqual(leftInset, rightInset, 0.5f,
                "the CTA row isn't centred in the panel — one side has more air than the other");
        }
    }
}
