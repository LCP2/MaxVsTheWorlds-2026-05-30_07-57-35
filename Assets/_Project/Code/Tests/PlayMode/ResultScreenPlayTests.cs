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
        public IEnumerator BossDefeated_ShowsVictoryScreenAndPauses()
        {
            _tracker = new GameObject("RunTracker Test");
            _tracker.AddComponent<RunTracker>();
            yield return null; // let Awake/OnEnable subscribe

            HudSignals.EmitEnemyKilled(Vector3.zero);
            HudSignals.EmitFactoryDestroyed(Vector3.zero);
            HudSignals.EmitBossDefeated(); // Victory
            yield return null; // let End() build the screen

            var canvas = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .FirstOrDefault(c => c.name == "Result Canvas");
            Assert.IsNotNull(canvas, "Result canvas should be built when the run ends.");
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
    }
}
