using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Factories;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1402 — a freshly built, UNDAMAGED pylon shows its "PYLON" name label and health bar strip.
    /// Tier 2: asserts the resolved active-in-hierarchy state of the label and bar after a refresh pass,
    /// never an authored flag.
    /// </summary>
    public sealed class MV1402PylonAlwaysShowsBarTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            Pylon.ResetRegistry();
        }

        [Test]
        public void UndamagedPylon_ShowsItsNameLabelAndHealthBar()
        {
            Pylon.ResetRegistry();
            _go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var pylon = _go.AddComponent<Pylon>();
            pylon.Build(); // AddComponent's own Awake never runs outside Play mode
            var bar = _go.GetComponent<WorldHealthBar>();
            Assert.IsNotNull(bar, "setup failure: a built pylon must carry a WorldHealthBar");
            Assert.AreEqual(1f, pylon.Normalized, 0.0001f, "setup failure: pylon must be undamaged");

            bar.RefreshForTests();

            Text label = null;
            foreach (Text t in bar.BarRectTransform.GetComponentsInChildren<Text>(true))
                if (t.text == "PYLON") { label = t; break; }
            Assert.IsNotNull(label, "no PYLON name label exists on the pylon's bar");
            Assert.IsTrue(label.gameObject.activeInHierarchy, "an undamaged pylon's PYLON name label must be visible");
            Assert.IsTrue(bar.BarStripActive, "an undamaged pylon's health bar strip must be active");
            Assert.IsTrue(bar.Showing, "an undamaged pylon's bar must be showing");
            Assert.AreEqual(1f, bar.VisibilityAlpha, 0.0001f, "an undamaged pylon's bar must be fully opaque");
        }
    }
}
