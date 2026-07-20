using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.InputSystem.OnScreen;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Dash has to be where the thumb already is (YT-116). It used to be the top-right slot, which
    /// on a phone held in two hands is the one corner the thumbs cannot reach without letting go of
    /// aim.
    ///
    /// The interesting failure is not "the button is missing" — it is a button drawn on top of the
    /// aim stick's INVISIBLE touch pad, which is 30 px larger than the stick's artwork on every
    /// side. That overlap would look perfect in a screenshot and steal drags meant for aiming, so
    /// these measure rendered corners rather than the numbers that were authored.
    /// </summary>
    public sealed class DashButtonPlayTests
    {
        private GameObject _hud;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _hud = new GameObject("HUD", typeof(HudController));
            yield return null;   // Awake builds the whole interface
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_hud != null) Object.Destroy(_hud);
            yield return null;
        }

        private RectTransform Find(string name)
        {
            foreach (RectTransform rt in _hud.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt;
            return null;
        }

        /// <summary>Screen-space rect of what is actually drawn, corners and all.</summary>
        private static Rect ScreenRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(Mathf.Min(c[0].x, c[2].x), Mathf.Min(c[0].y, c[2].y),
                                   Mathf.Max(c[0].x, c[2].x), Mathf.Max(c[0].y, c[2].y));
        }

        [UnityTest]
        public IEnumerator TheDashButtonExistsDownWhereTheThumbIs()
        {
            RectTransform dash = Find("Dash Button");
            Assert.IsNotNull(dash, "there is no dash button");

            Rect r = ScreenRect(dash);
            Assert.That(r.center.y, Is.LessThan(Screen.height * 0.5f),
                        "the dash button is in the upper half of the screen — out of thumb reach");
            Assert.That(r.center.x, Is.GreaterThan(Screen.width * 0.5f),
                        "the dash button is not on the right-hand side");
            yield return null;
        }

        /// <summary>
        /// The one that matters. The pad is invisible and bigger than the stick, so an overlap is
        /// invisible in a screenshot and shows up only as aim drags that mysteriously dash.
        /// </summary>
        [UnityTest]
        public IEnumerator TheDashButtonDoesNotSitOnTheAimSticksTouchPad()
        {
            RectTransform dash = Find("Dash Button");
            RectTransform aimPad = Find("Aim Touch");
            Assert.IsNotNull(dash, "there is no dash button");
            Assert.IsNotNull(aimPad, "the aim stick's touch pad is gone");

            Assert.That(ScreenRect(dash).Overlaps(ScreenRect(aimPad)), Is.False,
                        "the dash button overlaps the aim stick's touch pad — drags meant for " +
                        "aiming would land on dash");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheDashButtonDoesNotSitOnTheMoveStickEither()
        {
            RectTransform dash = Find("Dash Button");
            RectTransform movePad = Find("Move Touch");
            Assert.IsNotNull(movePad, "the move stick's touch pad is gone");

            Assert.That(ScreenRect(dash).Overlaps(ScreenRect(movePad)), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PressingItActuallyDashes()
        {
            RectTransform dash = Find("Dash Button");
            var button = dash.GetComponentInChildren<OnScreenButton>(true);

            Assert.IsNotNull(button, "the dash button has no OnScreenButton — it is a picture");
            Assert.That(button.controlPath, Is.EqualTo("<Gamepad>/buttonSouth"),
                        "the dash button is not bound to the control PlayerController reads");
            yield return null;
        }

        /// <summary>The old top-right D slot is gone; B and U stay, and say what they are.</summary>
        [UnityTest]
        public IEnumerator TheOldTopRightDashSlotIsGone()
        {
            Assert.IsNull(Find("Slot D"), "dash is still drawn in the unreachable top-right corner");
            Assert.IsNotNull(Find("Slot B"), "the bomb slot vanished");
            Assert.IsNotNull(Find("Slot U"), "the ultimate slot vanished");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheUnbuiltAbilitiesSayTheyAreLocked()
        {
            foreach (string slot in new[] { "Slot B", "Slot U" })
            {
                RectTransform rt = Find(slot);
                bool captioned = false;
                foreach (UnityEngine.UI.Text t in rt.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                    if (t.text == "LOCKED") captioned = true;

                Assert.That(captioned, Is.True,
                            $"{slot} does not say it is locked — it reads as a button you are " +
                            "failing to find, which is exactly what got it reported");
            }
            yield return null;
        }
    }
}
