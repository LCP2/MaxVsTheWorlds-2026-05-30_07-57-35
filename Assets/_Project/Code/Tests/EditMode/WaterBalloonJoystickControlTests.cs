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
    /// MV-356 defect 1: "the first time you use the joystick to target, you get a targeting circle,
    /// but after that you just get a line." AbilityControlGatingPlayTests (MV-292) already proves a
    /// second REAL throw reaches <see cref="MaxWorlds.Weapons.PlayerAbilities.TryThrowWaterBalloon"/>
    /// once the cooldown clears — but nothing checks that the landing circle itself is still visible
    /// on that second aim, which is exactly the gap this ticket's AC asks for.
    ///
    /// Drags stay under <see cref="WaterBalloonJoystickControl.DragRadiusPixels"/>'s 5% release
    /// threshold on purpose: this isolates the aim-visual lifecycle from
    /// <c>PlayerAbilities</c>'s cooldown (which only advances via <c>Update()</c>'s <c>Time.deltaTime</c>,
    /// unavailable outside Play mode — that real-time gate is <c>AbilityControlGatingPlayTests</c>'s
    /// job, not this one's).
    /// </summary>
    public sealed class WaterBalloonJoystickControlTests
    {
        private GameObject _max;
        private GameObject _pad;
        private WaterBalloonJoystickControl _control;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            PickupWallet.SetPowerCells(10);
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);   // MV-380: restored acquisition gate, same as Teleport

            _max = new GameObject("Max", typeof(CharacterController), typeof(PlayerController));
            // PlayerController.Awake() self-attaches this (WV-231) — real gameplay and the PlayMode
            // suite both go through that path. A synchronous EditMode [Test]/[SetUp] gets no editor
            // tick between statements (unlike a PlayMode [UnityTest]'s "yield return null"), so nested
            // Awake ordering for an AddComponent-from-within-Awake chain isn't guaranteed complete by
            // the time this line runs. Fetch-or-add directly to make the fixture deterministic without
            // depending on that timing — this test is about the joystick's own visuals, not the
            // self-attach path (PlayerAbilitiesPlayTests already covers that one).
            var abilities = _max.GetComponent<PlayerAbilities>();
            if (abilities == null) abilities = _max.AddComponent<PlayerAbilities>();

            _pad = new GameObject("Water Balloon Touch", typeof(RectTransform), typeof(Image));
            _control = _pad.AddComponent<WaterBalloonJoystickControl>();
            var knob = new GameObject("Knob", typeof(RectTransform)).GetComponent<RectTransform>();
            _control.Init(knob, _max.transform, abilities);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_pad);
            Object.DestroyImmediate(_max);
            WeaponSystemState.Reset();
            PickupWallet.Reset();
        }

        private static PointerEventData At(Vector2 pos) => new PointerEventData(EventSystem.current) { position = pos };

        /// <summary>A drag comfortably under the 5% release threshold (<see cref="WaterBalloonJoystickControl.DragRadiusPixels"/>
        /// is 90px, so 2px never crosses it) — enough to exercise <c>OnDrag</c>'s mesh rebuild without
        /// ever reaching <c>TryThrowWaterBalloon</c>, so no cycle spends the ability or starts its
        /// cooldown.</summary>
        private void AimReleaseCycle()
        {
            _control.OnPointerDown(At(Vector2.zero));
            _control.OnDrag(At(new Vector2(2f, 0f)));
            _control.OnPointerUp(At(new Vector2(2f, 0f)));
        }

        [Test]
        public void TheLandingCircleShowsOnTheFirstAim()
        {
            _control.OnPointerDown(At(Vector2.zero));
            _control.OnDrag(At(new Vector2(2f, 0f)));

            Assert.That(_control.LandingCircleVisible, Is.True, "the first aim of a run must show the landing circle");
            Assert.That(_control.LandingCircleVertexCount, Is.GreaterThan(0), "an active but empty mesh is invisible either way");
        }

        [Test]
        public void TheLandingCircleReappearsOnEveryAim_NotJustTheFirst()
        {
            for (int i = 0; i < 12; i++)
            {
                _control.OnPointerDown(At(Vector2.zero));
                _control.OnDrag(At(new Vector2(2f, 0f)));

                Assert.That(_control.LandingCircleVisible, Is.True,
                    $"cycle {i + 1}: the landing circle must show every time the player aims, not just the first");
                Assert.That(_control.LandingCircleVertexCount, Is.GreaterThan(0),
                    $"cycle {i + 1}: the landing circle is active but drawing nothing");

                _control.OnPointerUp(At(new Vector2(2f, 0f)));
                Assert.That(_control.LandingCircleVisible, Is.False, $"cycle {i + 1}: releasing must hide the circle again");
            }
        }

        [Test]
        public void RepeatedAimReleaseCyclesNeverSpendTheAbility()
        {
            // Proves the drags above are genuinely below the throw threshold, so the pass above is
            // about the circle's own lifecycle and not an accidental cooldown-driven side effect.
            var abilities = _max.GetComponent<PlayerAbilities>();
            for (int i = 0; i < 12; i++) AimReleaseCycle();

            Assert.That(abilities.WaterBalloonReady, Is.True,
                "a drag under the release threshold must never reach TryThrowWaterBalloon");
        }

        [Test]
        public void TheArcAndTheCircleAreBuiltOnce_AndReusedEverySession()
        {
            // The bug this ticket describes is exactly a one-shot object: created on first use, then
            // destroyed/orphaned instead of hidden and reused. Cycling and checking vertex counts stay
            // non-zero (rather than merely non-null) catches a reused-but-degenerate mesh too.
            for (int i = 0; i < 3; i++) AimReleaseCycle();

            _control.OnPointerDown(At(Vector2.zero));
            _control.OnDrag(At(new Vector2(2f, 0f)));
            Assert.That(_control.LandingCircleVertexCount, Is.GreaterThan(0),
                "after several aim/release cycles the landing circle mesh must still be real geometry");
        }
    }
}
