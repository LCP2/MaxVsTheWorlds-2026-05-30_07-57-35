using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1322 (the one new test, per CC_AUTONOMY's testing policy). Fails on base commit 1a1b862:
    /// <c>ResultScreen.BuildCanvas</c> never read <see cref="SecretCensus"/> at all, so a world with
    /// secrets authored and some found showed no SECRETS line, and a world with none showed none
    /// either — the two cases were indistinguishable by the assertions below. Tier 2: builds the real
    /// screen and reads the RESOLVED Text components it produced, never an authored constant.
    /// </summary>
    public sealed class MV1322SecretsResultRowTests
    {
        [TearDown]
        public void TearDown()
        {
            SecretCensus.Reset();
            foreach (var rs in UnityEngine.Object.FindObjectsByType<ResultScreen>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(rs.gameObject);
            var es = UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (es != null) UnityEngine.Object.DestroyImmediate(es.gameObject);
            Time.timeScale = 1f; // ResultScreen.Show() freezes the game
            ModalFrameRateGate.ResetForTests();
        }

        [Test]
        public void SixSecretsFourFound_ResolvesSecretsFourOfSix()
        {
            SecretCensus.Reset();
            SecretCensus.SeedTotal(6);
            SecretCensus.MarkFound("a");
            SecretCensus.MarkFound("b");
            SecretCensus.MarkFound("c");
            SecretCensus.MarkFound("d");

            var go = new GameObject("ResultScreen Test");
            var screen = go.AddComponent<ResultScreen>();
            screen.Show(new RunStats());

            var texts = go.GetComponentsInChildren<Text>(true);
            int labelIndex = Array.FindIndex(texts, t => t.text == "SECRETS");
            Assert.GreaterOrEqual(labelIndex, 0, "expected a SECRETS row label when the world authors secrets");
            Assert.AreEqual("4/6", texts[labelIndex + 1].text,
                "SECRETS row must read found/total exactly");
        }

        [Test]
        public void NoSecretsAuthored_NoSecretsRow()
        {
            SecretCensus.Reset();
            SecretCensus.SeedTotal(0);

            var go = new GameObject("ResultScreen Test");
            var screen = go.AddComponent<ResultScreen>();
            screen.Show(new RunStats());

            var texts = go.GetComponentsInChildren<Text>(true);
            Assert.IsFalse(Array.Exists(texts, t => t.text == "SECRETS"),
                "a world with no authored secrets must not show a SECRETS row");
        }
    }
}
