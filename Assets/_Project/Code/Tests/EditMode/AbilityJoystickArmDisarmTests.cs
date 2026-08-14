using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-372, Lee's design direction 12 Aug 2026: "touch and move away from centre → armed [and
    /// bright]. Return the thumb to centre → disarmed [and normal brightness]. Release while disarmed →
    /// nothing happens — no fire, no cooldown, no cell, no cost. Release while armed → fires as normal.
    /// The player can arm and disarm repeatedly within a single touch." This exercises the shared state
    /// machine in <see cref="AbilityJoystickControlBase"/> through both concrete controls, since AC1
    /// requires it work "for both the Water Balloon and Teleport, from the shared layer" — a bug in the
    /// base class would otherwise show up in only one control's suite and read as control-specific.
    ///
    /// <see cref="AbilityJoystickControlBase.ArmDeadZoneFraction"/> is 15% of
    /// <see cref="AbilityJoystickControlBase.DragRadiusPixels"/> (90px), i.e. 13.5px — drags below use
    /// "under" positions, drags above use "over" positions, matching
    /// <see cref="WaterBalloonJoystickControlTests"/>'s own convention of picking drags deliberately on
    /// one side of a threshold rather than near it.
    /// </summary>
    public sealed class AbilityJoystickArmDisarmTests
    {
        private const float UnderThresholdPx = 4f;   // well below the 13.5px arm dead-zone
        private const float OverThresholdPx = 30f;    // well above it

        private GameObject _max;
        private GameObject _pad;
        private Image _rings;
        private const float RestingRingAlpha = 0.7f;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            PickupWallet.SetPowerCells(10);   // MV-370: Water Balloon is a primary add-on now, gated on cells not acquisition
            WeaponSystemState.Acquire(AbilityKind.Teleport);

            _max = new GameObject("Max", typeof(CharacterController), typeof(PlayerController));
            var abilities = _max.GetComponent<PlayerAbilities>();
            if (abilities == null) abilities = _max.AddComponent<PlayerAbilities>();

            _pad = new GameObject("Ability Touch", typeof(RectTransform), typeof(Image));
            var ringsGo = new GameObject("Rings", typeof(RectTransform), typeof(Image));
            _rings = ringsGo.GetComponent<Image>();
            _rings.color = new Color(0.35f, 0.65f, 0.98f, RestingRingAlpha);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_pad);
            Object.DestroyImmediate(_max);
            Object.DestroyImmediate(_rings.gameObject);
            WeaponSystemState.Reset();
            PickupWallet.Reset();
        }

        private static PointerEventData At(Vector2 pos) => new PointerEventData(EventSystem.current) { position = pos };

        private PlayerAbilities Abilities => _max.GetComponent<PlayerAbilities>();

        private WaterBalloonJoystickControl NewWaterBalloonControl()
        {
            var control = _pad.AddComponent<WaterBalloonJoystickControl>();
            var knob = new GameObject("Knob", typeof(RectTransform)).GetComponent<RectTransform>();
            control.Init(knob, _max.transform, Abilities, _rings);
            return control;
        }

        private TeleportJoystickControl NewTeleportControl()
        {
            var control = _pad.AddComponent<TeleportJoystickControl>();
            var knob = new GameObject("Knob", typeof(RectTransform)).GetComponent<RectTransform>();
            control.Init(knob, _max.transform, Abilities, _rings);
            return control;
        }

        // ---------- Water Balloon ----------

        [Test]
        public void WaterBalloon_PressAloneIsDisarmed()
        {
            var control = NewWaterBalloonControl();
            control.OnPointerDown(At(Vector2.zero));

            Assert.That(control.IsArmed, Is.False, "a bare press, no drag yet, must not read as armed");
        }

        [Test]
        public void WaterBalloon_DraggingPastTheDeadZoneArms()
        {
            var control = NewWaterBalloonControl();
            control.OnPointerDown(At(Vector2.zero));
            control.OnDrag(At(new Vector2(OverThresholdPx, 0f)));

            Assert.That(control.IsArmed, Is.True, "a drag well clear of the centre dead-zone must arm");
        }

        [Test]
        public void WaterBalloon_DraggingBackToCentreDisarms()
        {
            var control = NewWaterBalloonControl();
            control.OnPointerDown(At(Vector2.zero));
            control.OnDrag(At(new Vector2(OverThresholdPx, 0f)));
            Assert.That(control.IsArmed, Is.True, "precondition: the drag armed the control");

            control.OnDrag(At(new Vector2(UnderThresholdPx, 0f)));

            Assert.That(control.IsArmed, Is.False, "returning the thumb to the centre dead-zone must disarm");
        }

        [Test]
        public void WaterBalloon_ReleasingWhileDisarmedThrowsNothingAndSpendsNoCooldown()
        {
            var control = NewWaterBalloonControl();
            control.OnPointerDown(At(Vector2.zero));
            control.OnDrag(At(new Vector2(UnderThresholdPx, 0f)));
            Assert.That(control.IsArmed, Is.False, "precondition: the drag never left the dead-zone");

            control.OnPointerUp(At(new Vector2(UnderThresholdPx, 0f)));

            Assert.That(Abilities.WaterBalloonReady, Is.True,
                "releasing while disarmed must not throw — no cooldown may start");
        }

        [Test]
        public void WaterBalloon_ReleasingWhileArmedFiresAndStartsCooldown()
        {
            var control = NewWaterBalloonControl();
            control.OnPointerDown(At(Vector2.zero));
            control.OnDrag(At(new Vector2(OverThresholdPx, 0f)));
            Assert.That(control.IsArmed, Is.True, "precondition: the drag armed the control");

            control.OnPointerUp(At(new Vector2(OverThresholdPx, 0f)));

            Assert.That(Abilities.WaterBalloonReady, Is.False,
                "releasing while armed must throw and start the cooldown, same as before MV-372");
        }

        [Test]
        public void WaterBalloon_ArmingAndDisarmingCanRepeatWithinOneTouchAndFinalReleaseStillHonoursIt()
        {
            var control = NewWaterBalloonControl();
            control.OnPointerDown(At(Vector2.zero));

            for (int i = 0; i < 3; i++)
            {
                control.OnDrag(At(new Vector2(OverThresholdPx, 0f)));
                Assert.That(control.IsArmed, Is.True, $"cycle {i}: drag out must arm");

                control.OnDrag(At(new Vector2(UnderThresholdPx, 0f)));
                Assert.That(control.IsArmed, Is.False, $"cycle {i}: drag back to centre must disarm");
            }

            control.OnPointerUp(At(new Vector2(UnderThresholdPx, 0f)));

            Assert.That(Abilities.WaterBalloonReady, Is.True,
                "the touch ended disarmed after several toggles — releasing must still abort, not fire");
        }

        [Test]
        public void WaterBalloon_RingsBrightenWhenArmedAndRestoreExactRestingAlphaWhenDisarmed()
        {
            var control = NewWaterBalloonControl();
            Assert.That(_rings.color.a, Is.EqualTo(RestingRingAlpha).Within(0.001f),
                "precondition: the rings start at their built resting alpha");

            control.OnPointerDown(At(Vector2.zero));
            control.OnDrag(At(new Vector2(OverThresholdPx, 0f)));
            Assert.That(_rings.color.a, Is.GreaterThan(RestingRingAlpha),
                "arming must brighten the rings — unmistakably readable in peripheral vision");

            control.OnDrag(At(new Vector2(UnderThresholdPx, 0f)));
            Assert.That(_rings.color.a, Is.EqualTo(RestingRingAlpha).Within(0.001f),
                "disarming must restore the exact resting alpha, not just \"dimmer than armed\"");
        }

        // ---------- Teleport (same shared base — proves AC1's \"from the shared layer\") ----------

        [Test]
        public void Teleport_DraggingPastTheDeadZoneArmsAndBackToCentreDisarms()
        {
            var control = NewTeleportControl();
            control.OnPointerDown(At(Vector2.zero));
            control.OnDrag(At(new Vector2(OverThresholdPx, 0f)));
            Assert.That(control.IsArmed, Is.True, "Teleport must arm through the same shared logic Water Balloon uses");

            control.OnDrag(At(new Vector2(UnderThresholdPx, 0f)));
            Assert.That(control.IsArmed, Is.False, "Teleport must disarm through the same shared logic Water Balloon uses");
        }

        [Test]
        public void Teleport_ReleasingWhileDisarmedBlinksNothingAndSpendsNoCooldown()
        {
            var control = NewTeleportControl();
            control.OnPointerDown(At(Vector2.zero));
            control.OnDrag(At(new Vector2(UnderThresholdPx, 0f)));

            control.OnPointerUp(At(new Vector2(UnderThresholdPx, 0f)));

            Assert.That(Abilities.TeleportReady, Is.True,
                "releasing while disarmed must not blink — no cooldown may start");
        }

        [Test]
        public void Teleport_ReleasingWhileArmedBlinksAndStartsCooldown()
        {
            var control = NewTeleportControl();
            control.OnPointerDown(At(Vector2.zero));
            control.OnDrag(At(new Vector2(OverThresholdPx, 0f)));

            control.OnPointerUp(At(new Vector2(OverThresholdPx, 0f)));

            Assert.That(Abilities.TeleportReady, Is.False,
                "releasing while armed must blink and start the cooldown, same as before MV-372");
        }
    }
}
