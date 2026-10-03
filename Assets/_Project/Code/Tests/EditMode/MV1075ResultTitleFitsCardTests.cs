using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1075 (the one new test, per CC_AUTONOMY's testing policy). Read from base b04b39b,
    /// <c>Assets/_Project/Code/Runtime/UI/ResultScreen.cs</c>: the title is a fixed 78pt in a 680-wide
    /// box with <c>horizontalOverflow = Overflow</c>, so "WORLD 1 — BACKYARD SAVED" never shrinks or
    /// wraps — it just renders wider than the card and hangs off both sides of it. Testing policy
    /// Tier 2: asserts the title's RESOLVED <see cref="Text.preferredWidth"/> after the screen is
    /// actually built, never the authored 78pt/680px literals themselves.
    /// </summary>
    public sealed class MV1075ResultTitleFitsCardTests
    {
        [Test]
        public void TitleResolvedWidthFitsInsideTheCard_ForWorld1Name()
        {
            var worldGo = new GameObject("BackyardPath Probe");
            var path = worldGo.AddComponent<BackyardPath>();
            typeof(BackyardPath)
                .GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(path, new MapData { name = "World 1 — Backyard" });

            var go = new GameObject("ResultScreen Test");
            try
            {
                var screen = go.AddComponent<ResultScreen>();
                screen.Show(new RunStats());

                Canvas.ForceUpdateCanvases();
                var title = go.GetComponentsInChildren<Text>(true)[0];

                float cardWidth = ResultLayout.PanelWidth;
                Assert.That(title.preferredWidth, Is.LessThanOrEqualTo(cardWidth - 40f),
                    $"title '{title.text}' resolved width {title.preferredWidth:0.0}px exceeds the card " +
                    $"({cardWidth:0}px) minus 40 — it will hang off the card's edges");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(worldGo);
                var es = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
                if (es != null) Object.DestroyImmediate(es.gameObject);
                Time.timeScale = 1f;
                ModalFrameRateGate.ResetForTests();
            }
        }
    }
}
