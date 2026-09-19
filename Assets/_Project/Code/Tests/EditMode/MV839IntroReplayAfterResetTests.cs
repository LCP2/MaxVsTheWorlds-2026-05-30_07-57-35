using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Intro;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-839: QUIT TO MENU → RESET → PLAY on the same slot must replay the intro film. Quit-to-menu
    /// is a same-process scene reload (<see cref="MaxWorlds.UI.RunFlow.QuitToMenu"/>), so
    /// <see cref="IntroCinematic"/>'s process-lifetime <c>s_consumed</c> static survives it while the
    /// instance itself is destroyed by the reload — exactly what this test simulates. On base
    /// <c>10fce02</c>, <see cref="IntroCinematic.TryPlay"/> refused the second forced call because it
    /// checked <c>s_consumed</c> unconditionally, before <c>force</c> got a say.
    /// </summary>
    public sealed class MV839IntroReplayAfterResetTests
    {
        private GameObject _camGo;

        [SetUp]
        public void SetUp()
        {
            foreach (var stray in Object.FindObjectsByType<IntroCinematic>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            _camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            _camGo.AddComponent<Camera>();

            IntroCinematic.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var stray in Object.FindObjectsByType<IntroCinematic>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            IntroCinematic.ResetForTests();
        }

        [Test]
        public void ForcedPlaySucceedsAgainAfterASameProcessSceneReload()
        {
            Assert.IsTrue(IntroCinematic.TryPlay(force: true),
                "sanity: the first forced play, on an empty/reset slot, must start.");

            // Simulate QUIT TO MENU's scene reload: the instance is torn down (as LoadScene would do
            // to every scene object), but nothing here touches the process-lifetime s_consumed static
            // — only ResetForTests does, and a real scene reload never calls that.
            var instance = Object.FindFirstObjectByType<IntroCinematic>();
            Assert.IsNotNull(instance, "sanity: TryPlay did not create an instance to simulate the reload against.");
            Object.DestroyImmediate(instance.gameObject);

            Assert.IsTrue(IntroCinematic.TryPlay(force: true),
                "a forced replay after a same-process scene reload (RESET then PLAY) must start — " +
                "s_consumed surviving the reload must not block a forced call.");
        }
    }
}
