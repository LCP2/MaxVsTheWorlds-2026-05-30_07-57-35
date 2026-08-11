using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The three active-ability controls (WV-241, spec §6a): each has to "appear only once acquired,
    /// and become more prominent as that ability's level rises (bigger / brighter / more detailed)".
    /// These pin the prominence curve and that the two builders (button, joystick) actually apply it,
    /// rather than drawing the same picture at every level.
    /// </summary>
    public sealed class AbilityControlArtTests
    {
        private RectTransform _canvas;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("canvas", typeof(RectTransform), typeof(Canvas));
            _canvas = (RectTransform)go.transform;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_canvas.gameObject);

        // ---------- Prominence ----------

        [Test]
        public void ASingleLevelAbilityIsAlwaysFullyProminent()
        {
            // Dash: a single unlock, maxLevel 1 — there is no "half-built" state to read as.
            Assert.That(AbilityControlArt.Prominence(1, 1), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void ProminenceRisesFromAFloorAtLevelOneToFullAtTheCap()
        {
            float l1 = AbilityControlArt.Prominence(1, 3);
            float l3 = AbilityControlArt.Prominence(3, 3);

            Assert.That(l1, Is.EqualTo(AbilityControlArt.MinProminence).Within(1e-5f),
                "a freshly-acquired ability must still read at the floor, not invisible");
            Assert.That(l3, Is.EqualTo(1f).Within(1e-5f), "the level cap must be fully prominent");
            Assert.Greater(l3, l1, "levelling up must visibly increase prominence");
        }

        [Test]
        public void ProminenceNeverDropsBelowTheFloor_EvenForALevelZeroInput()
        {
            Assert.That(AbilityControlArt.Prominence(0, 3), Is.EqualTo(AbilityControlArt.MinProminence).Within(1e-5f));
        }

        // ---------- Button (Dash/Teleport) ----------

        [Test]
        public void AHigherLevelButtonIsBiggerAndBrighter()
        {
            var low = AbilityControlArt.BuildButton(_canvas, "Low", Vector2.zero, 140f, Color.cyan, "T", 1, 2);
            var high = AbilityControlArt.BuildButton(_canvas, "High", Vector2.zero, 140f, Color.cyan, "T", 2, 2);

            Assert.Greater(high.Root.sizeDelta.x, low.Root.sizeDelta.x,
                "a higher-level control must read bigger");
            Assert.Greater(high.Ring.color.a, low.Ring.color.a,
                "a higher-level control must read brighter");
        }

        [Test]
        public void TheTeleportButtonGainsADetailPipAtItsSecondLevel()
        {
            var root1 = AbilityControlArt.BuildButton(_canvas, "L1", Vector2.zero, 140f, Color.cyan, "T", 1, 2).Root;
            var root2 = AbilityControlArt.BuildButton(_canvas, "L2", Vector2.zero, 140f, Color.cyan, "T", 2, 2).Root;

            int pips1 = root1.Cast<Transform>().Count(t => t.name.StartsWith("Pip"));
            int pips2 = root2.Cast<Transform>().Count(t => t.name.StartsWith("Pip"));

            Assert.AreEqual(0, pips1, "level 1 (random blink) shouldn't show a level-2 detail pip");
            Assert.AreEqual(1, pips2, "level 2 (aimed blink) must read as visibly more built-out");
        }

        [Test]
        public void ADashSingleUnlockGetsNoDetailPips()
        {
            var root = AbilityControlArt.BuildButton(_canvas, "Dash", Vector2.zero, 140f, Color.yellow, "DASH", 1, 1).Root;
            int pips = root.Cast<Transform>().Count(t => t.name.StartsWith("Pip"));
            Assert.AreEqual(0, pips, "a single-level ability has no further level to signal with a pip");
        }

        [Test]
        public void TheButtonHasAWorkingCooldownRadial()
        {
            var v = AbilityControlArt.BuildButton(_canvas, "Btn", Vector2.zero, 140f, Color.cyan, "T", 1, 2);
            Assert.AreEqual(Image.Type.Filled, v.Radial.type);
            Assert.AreEqual(Image.FillMethod.Radial360, v.Radial.fillMethod);
        }

        // ---------- Joystick (Water Balloon) ----------

        [Test]
        public void AHigherLevelJoystickIsBiggerAndBrighter()
        {
            var low = AbilityControlArt.BuildJoystick(_canvas, "Low", Vector2.zero, Color.blue, "Balloon", 1, 3);
            var high = AbilityControlArt.BuildJoystick(_canvas, "High", Vector2.zero, Color.blue, "Balloon", 3, 3);

            Assert.Greater(high.Root.sizeDelta.x, low.Root.sizeDelta.x,
                "a maxed Water Balloon joystick must read bigger than a freshly-acquired one");
            Assert.Greater(high.Rings.color.a, low.Rings.color.a,
                "a maxed Water Balloon joystick must read brighter");
        }

        [Test]
        public void TheJoystickKnobStartsCentred()
        {
            // WV-240 drives the knob from drag input; the art must not pre-bake an offset.
            var v = AbilityControlArt.BuildJoystick(_canvas, "Joy", Vector2.zero, Color.blue, "Balloon", 1, 3);
            Assert.AreEqual(Vector2.zero, v.Knob.anchoredPosition);
        }

        [Test]
        public void TheJoystickShowsItsGivenLabel()
        {
            // MV-337: the Water Balloon joystick must name itself, unlike the unlabelled move/aim sticks.
            var v = AbilityControlArt.BuildJoystick(_canvas, "Joy", Vector2.zero, Color.blue, "Balloon", 1, 3);
            Assert.AreEqual("Balloon", v.Label.text);
        }

        [Test]
        public void DetailPipsStayInsideTheControlsOwnRadius()
        {
            var v = AbilityControlArt.BuildJoystick(_canvas, "Joy", Vector2.zero, Color.blue, "Balloon", 3, 3);
            float maxRadius = v.Root.sizeDelta.x * 0.5f;

            foreach (Transform child in v.Root)
            {
                if (!child.name.StartsWith("Pip")) continue;
                var rt = (RectTransform)child;
                Assert.LessOrEqual(rt.anchoredPosition.magnitude, maxRadius + 0.5f,
                    "a detail pip has drifted outside the control it's decorating");
            }
        }
    }
}
