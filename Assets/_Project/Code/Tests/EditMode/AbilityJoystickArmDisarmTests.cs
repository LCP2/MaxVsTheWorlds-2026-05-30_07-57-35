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
            PickupWallet.SetPowerCells(10);
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);   // MV-380: restored acquisition gate, same as Teleport
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
        public void WaterBalloon_PressWithNoCellsBankedStillShowsThePreview()
        {
            // MV-381: MV-370 used to make a press on an owned-but-unspendable control a total no-op,
            // which read as "there's no aim indicator at all" to a player pressing a control that's
            // visibly on screen. The preview must still show; only the throw stays gated.
            PickupWallet.SetPowerCells(0);
            var control = NewWaterBalloonControl();

            control.OnPointerDown(At(Vector2.zero));

            Assert.That(control.IsAiming, Is.True, "an owned control with no cell to spend must still preview the aim");
            Assert.That(control.LandingCircleVisible, Is.True, "the landing circle itself must show, not just the aiming flag");
        }

        [Test]
        public void WaterBalloon_ReleasingArmedWithNoCellsBankedNeverThrows()
        {
            PickupWallet.SetPowerCells(0);
            var control = NewWaterBalloonControl();

            control.OnPointerDown(At(Vector2.zero));
            control.OnDrag(At(new Vector2(OverThresholdPx, 0f)));
            Assert.That(control.IsArmed, Is.True, "precondition: the drag armed the control");

            control.OnPointerUp(At(new Vector2(OverThresholdPx, 0f)));

            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0),
                "releasing armed with no cell banked must never spend a cell it doesn't have");
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

        [Test]
        public void Teleport_PressWhileOnCooldownStillShowsThePreview()
        {
            // MV-381: same fix as Water Balloon, through the shared base — an owned control that's
            // merely unspendable right now (on cooldown) must still preview on press, not go silent.
            // MV-385: Lee's playtest found no landing-target indicator during Teleport aim at all — this
            // now checks the circle itself (LandingCircleVisible/VertexCount), not just IsAiming, the
            // same MV-356-style gap Water Balloon's own preview test already closed.
            var control = NewTeleportControl();
            control.OnPointerDown(At(Vector2.zero));
            control.OnDrag(At(new Vector2(OverThresholdPx, 0f)));
            control.OnPointerUp(At(new Vector2(OverThresholdPx, 0f)));   // first blink starts the cooldown
            Assert.That(Abilities.TeleportReady, Is.False, "precondition: the first blink started the cooldown");

            control.OnPointerDown(At(Vector2.zero));

            Assert.That(control.IsAiming, Is.True,
                "a press while on cooldown must still preview the aim, not answer with silence");
            Assert.That(control.LandingCircleVisible, Is.True,
                "the landing circle itself must show, not just the aiming flag");
            Assert.That(control.LandingCircleVertexCount, Is.GreaterThan(0),
                "an active but empty mesh reads as invisible to the player exactly like not active does");
        }

        [Test]
        public void Teleport_PressAloneShowsAVisibleNonEmptyLandingCircle()
        {
            // MV-385 AC(a): "a visible landing-target indicator appears during Teleport aim, before
            // commit" — the ordinary ready-and-owned case, not just the on-cooldown preview above.
            var control = NewTeleportControl();

            control.OnPointerDown(At(Vector2.zero));

            Assert.That(control.LandingCircleVisible, Is.True, "pressing an owned, ready Teleport control must show a landing circle");
            Assert.That(control.LandingCircleVertexCount, Is.GreaterThan(0), "the landing circle mesh must not be empty");
        }

        [Test]
        public void Teleport_ReleasingHidesTheLandingCircle()
        {
            var control = NewTeleportControl();
            control.OnPointerDown(At(Vector2.zero));
            Assert.That(control.LandingCircleVisible, Is.True, "precondition: the circle is showing mid-aim");

            control.OnPointerUp(At(Vector2.zero));

            Assert.That(control.LandingCircleVisible, Is.False, "releasing must hide the landing circle again");
        }
    }
}
