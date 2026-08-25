using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-543 — the Force Field button's percentage label was drawn in the exact same colour as the
    /// rings behind it (both hard-coded to <c>HudController.ForceFieldColor</c>), so the number was
    /// invisible at every value from 100% to 1%. Fails on 3d220c9 (pre-fix): label colour equals the
    /// ring colour exactly, a 1:1 contrast ratio.
    /// </summary>
    public sealed class MV543ForceFieldLabelContrastTests
    {
        [Test]
        public void ForceFieldLabel_ClearsContrastAgainstTheRingColourAndCarriesABackingTreatment()
        {
            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            try
            {
                var buttonRoot = FindRect(hudGo, "Force Field Button");
                Assert.That(buttonRoot, Is.Not.Null, "fixture: Force Field Button must exist");

                var label = buttonRoot.GetComponentInChildren<Text>(true);
                Assert.That(label, Is.Not.Null, "fixture: Force Field Button must carry a label");

                Color ring = HudController.ForceFieldColorForTest;

                // AC1
                float ringContrast = ContrastRatio(label.color, ring);
                Assert.That(ringContrast, Is.GreaterThanOrEqualTo(4.5f),
                    $"AC1: label vs ring contrast is {ringContrast:0.00}:1 — under WCAG's 4.5:1 floor");

                // AC2
                Assert.That(label.color, Is.Not.EqualTo(ring), "AC2: label colour must not equal the ring colour");

                var outline = label.GetComponent<Outline>();
                var shadow = label.GetComponent<Shadow>();
                Assert.That(outline != null || shadow != null, Is.True,
                    "AC2: label must carry an Outline or Shadow component");

                Color backingColor = outline != null ? outline.effectColor : shadow.effectColor;
                float backingContrast = ContrastRatio(label.color, backingColor);
                Assert.That(backingContrast, Is.GreaterThanOrEqualTo(4.5f),
                    $"AC2: label vs backing contrast is {backingContrast:0.00}:1 — under WCAG's 4.5:1 floor");
            }
            finally
            {
                Object.DestroyImmediate(hudGo);
            }
        }

        private static RectTransform FindRect(GameObject go, string name)
        {
            foreach (var rt in go.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt;
            return null;
        }

        private static void InvokeLifecycle(Object component, string methodName)
        {
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
        }

        private static float ContrastRatio(Color a, Color b)
        {
            float la = RelativeLuminance(a) + 0.05f;
            float lb = RelativeLuminance(b) + 0.05f;
            return la > lb ? la / lb : lb / la;
        }

        private static float RelativeLuminance(Color c) =>
            0.2126f * LinearChannel(c.r) + 0.7152f * LinearChannel(c.g) + 0.0722f * LinearChannel(c.b);

        private static float LinearChannel(float c) =>
            c <= 0.03928f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
    }
}
