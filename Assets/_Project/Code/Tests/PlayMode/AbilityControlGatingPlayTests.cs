using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using MaxWorlds.UI;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The active-ability on-screen controls (WV-240, spec §6a): each "appears only once
    /// acquired, and becomes more prominent as that ability's level rises", every control "shows a
    /// cooldown sweep and is disabled during cooldown", and the Water Balloon joystick's own
    /// press/drag/release must actually reach <see cref="PlayerAbilities.TryThrowWaterBalloon"/> —
    /// exactly the hand-off points <c>PlayerAbilities</c> and <c>AbilityControlArt</c> both call out
    /// as this ticket's job in their own doc comments.
    ///
    /// Against a real <see cref="HudController"/> and a real Max (not a mock), because the interesting
    /// failure here is a control that LOOKS right — built, positioned, correctly gated in a screenshot
    /// — but whose press never actually reaches the ability underneath it.
    /// </summary>
    public sealed class AbilityControlGatingPlayTests
    {
        private GameObject _max;
        private GameObject _hud;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            WeaponSystemState.Reset();
            DevTuning.Reset();
            PickupWallet.Reset();

            _max = new GameObject("Max", typeof(CharacterController), typeof(PlayerController));
            _max.tag = "Player";
            yield return null;   // PlayerController.Awake self-attaches PlayerAbilities

            _hud = new GameObject("HUD", typeof(HudController));
            yield return null;   // HudController.Awake builds the whole interface
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_hud != null) Object.Destroy(_hud);
            if (_max != null) Object.Destroy(_max);
            WeaponSystemState.Reset();
            DevTuning.Reset();
            PickupWallet.Reset();
            yield return null;
        }

        private RectTransform Find(string name)
        {
            foreach (RectTransform rt in _hud.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt;
            return null;
        }

        private static Rect ScreenRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(Mathf.Min(c[0].x, c[2].x), Mathf.Min(c[0].y, c[2].y),
                                   Mathf.Max(c[0].x, c[2].x), Mathf.Max(c[0].y, c[2].y));
        }

        private static int CountPips(RectTransform root)
        {
            int n = 0;
            foreach (Transform child in root) if (child.name.StartsWith("Pip")) n++;
            return n;
        }

        // ---------------------------------------------------------------- appear-on-acquire

        [UnityTest]
        public IEnumerator BothActiveAbilityControlsAreHiddenUntilAcquired()
        {
            Assert.That(Find("Water Balloon Joystick").gameObject.activeSelf, Is.False);
            Assert.That(Find("Teleport Joystick").gameObject.activeSelf, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EachControlAppearsTheMomentItsAbilityIsAcquired()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            WeaponSystemState.Acquire(AbilityKind.Teleport);
            yield return null;

            Assert.That(Find("Water Balloon Joystick").gameObject.activeSelf, Is.True);
            Assert.That(Find("Teleport Joystick").gameObject.activeSelf, Is.True);
        }

        // ---------------------------------------------------------------- prominence

        [UnityTest]
        public IEnumerator TheWaterBalloonJoystickGrowsWhenItLevelsUp()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            yield return null;
            float sizeAtL1 = Find("Water Balloon Joystick").sizeDelta.x;

            WeaponSystemState.LevelUpAbility(AbilityKind.WaterBalloon);
            yield return null;
            float sizeAtL2 = Find("Water Balloon Joystick").sizeDelta.x;

            Assert.Greater(sizeAtL2, sizeAtL1, "a leveled-up Water Balloon must read bigger (spec §6a)");
        }

        [UnityTest]
        public IEnumerator TheTeleportJoystickGainsADetailPipAtItsAimedSecondLevel()
        {
            WeaponSystemState.Acquire(AbilityKind.Teleport);
            yield return null;
            int pipsAtL1 = CountPips(Find("Teleport Joystick"));

            WeaponSystemState.LevelUpAbility(AbilityKind.Teleport);
            yield return null;
            int pipsAtL2 = CountPips(Find("Teleport Joystick"));

            Assert.AreEqual(0, pipsAtL1, "level 1 shouldn't show a level-2 detail pip");
            Assert.AreEqual(1, pipsAtL2, "level 2 (longer aimed blink) must read as visibly more built-out");
        }

        // ---------------------------------------------------------------- Water Balloon joystick input

        [UnityTest]
        public IEnumerator PressingTheJoystickWhenUnacquiredDoesNothing()
        {
            var control = _hud.GetComponentInChildren<WaterBalloonJoystickControl>(true);
            Assert.IsNotNull(control, "the Water Balloon joystick's control component is missing");

            control.OnPointerDown(new PointerEventData(EventSystem.current) { position = Vector2.zero });
            yield return null;

            Assert.That(control.IsAiming, Is.False, "an unowned control must ignore the press entirely");
        }

        [UnityTest]
        public IEnumerator DraggingAndReleasingThrowsWhenAcquiredAndReady()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            PickupWallet.SetPowerCells(10);
            yield return null;

            var control = _hud.GetComponentInChildren<WaterBalloonJoystickControl>(true);
            var abilities = _max.GetComponent<PlayerAbilities>();

            var down = new PointerEventData(EventSystem.current) { position = new Vector2(0f, 0f) };
            control.OnPointerDown(down);
            Assert.That(control.IsAiming, Is.True, "a press on a ready, owned control must start aiming");

            var drag = new PointerEventData(EventSystem.current) { position = new Vector2(90f, 0f) };
            control.OnDrag(drag);
            control.OnPointerUp(drag);

            Assert.That(control.IsAiming, Is.False, "releasing must close the aim");
            Assert.That(abilities.WaterBalloonReady, Is.False,
                "a real drag-and-release must reach PlayerAbilities.TryThrowWaterBalloon and start its cooldown");
        }

        [UnityTest]
        public IEnumerator ATapWithNoRealDragDoesNotThrow()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            PickupWallet.SetPowerCells(10);
            yield return null;

            var control = _hud.GetComponentInChildren<WaterBalloonJoystickControl>(true);
            var abilities = _max.GetComponent<PlayerAbilities>();

            var at = new PointerEventData(EventSystem.current) { position = new Vector2(100f, 100f) };
            control.OnPointerDown(at);
            control.OnPointerUp(at);   // no OnDrag in between — a plain tap, no aimed direction

            Assert.That(abilities.WaterBalloonReady, Is.True,
                "a tap with no drag has nothing to throw toward and must not spend the ability");
        }

        // ---------------------------------------------------------------- Teleport joystick input

        [UnityTest]
        public IEnumerator PressingTheTeleportJoystickWhenUnacquiredDoesNothing()
        {
            var control = _hud.GetComponentInChildren<TeleportJoystickControl>(true);
            Assert.IsNotNull(control, "the Teleport joystick's control component is missing");

            control.OnPointerDown(new PointerEventData(EventSystem.current) { position = Vector2.zero });
            yield return null;

            Assert.That(control.IsAiming, Is.False, "an unowned control must ignore the press entirely");
        }

        [UnityTest]
        public IEnumerator DraggingAndReleasingBlinksWhenAcquiredAndReady()
        {
            WeaponSystemState.Acquire(AbilityKind.Teleport);
            PickupWallet.SetPowerCells(10);
            yield return null;

            Vector3 before = _max.transform.position;
            var control = _hud.GetComponentInChildren<TeleportJoystickControl>(true);
            var abilities = _max.GetComponent<PlayerAbilities>();

            var down = new PointerEventData(EventSystem.current) { position = new Vector2(0f, 0f) };
            control.OnPointerDown(down);
            Assert.That(control.IsAiming, Is.True, "a press on a ready, owned control must start aiming");

            var drag = new PointerEventData(EventSystem.current) { position = new Vector2(90f, 0f) };
            control.OnDrag(drag);
            control.OnPointerUp(drag);

            Assert.That(control.IsAiming, Is.False, "releasing must close the aim");
            Assert.That(abilities.TeleportReady, Is.False,
                "a real drag-and-release must reach PlayerAbilities.TryTeleport and start its cooldown");
            Assert.That(Vector3.Distance(_max.transform.position, before), Is.GreaterThan(0.5f),
                "dragging and releasing the Teleport joystick while acquired must blink Max");
        }

        [UnityTest]
        public IEnumerator ATapWithNoRealDragDoesNotBlink()
        {
            WeaponSystemState.Acquire(AbilityKind.Teleport);
            PickupWallet.SetPowerCells(10);
            yield return null;

            var control = _hud.GetComponentInChildren<TeleportJoystickControl>(true);
            var abilities = _max.GetComponent<PlayerAbilities>();

            var at = new PointerEventData(EventSystem.current) { position = new Vector2(100f, 100f) };
            control.OnPointerDown(at);
            control.OnPointerUp(at);   // no OnDrag in between — a plain tap, no aimed direction

            Assert.That(abilities.TeleportReady, Is.True,
                "a tap with no drag has nothing to blink toward and must not spend the ability");
        }

        // ---------------------------------------------------------------- repeat use (MV-292)

        [UnityTest]
        public IEnumerator DraggingAndReleasingASecondTimeAfterCooldownThrowsAgain()
        {
            // A short DevTuning cooldown keeps the wait real (Time.deltaTime-driven) without the test
            // sitting through the authored 3s — this is the exact regression MV-292 exists for: prior
            // playtest found Water Balloon "worked once" through the real on-screen control.
            DevTuning.WaterBalloonCooldownSeconds = 0.05f;
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            PickupWallet.SetPowerCells(10);
            yield return null;

            var control = _hud.GetComponentInChildren<WaterBalloonJoystickControl>(true);
            var abilities = _max.GetComponent<PlayerAbilities>();

            var down1 = new PointerEventData(EventSystem.current) { position = Vector2.zero };
            control.OnPointerDown(down1);
            var drag1 = new PointerEventData(EventSystem.current) { position = new Vector2(90f, 0f) };
            control.OnDrag(drag1);
            control.OnPointerUp(drag1);
            Assert.That(abilities.WaterBalloonReady, Is.False, "the first throw must start the cooldown");

            yield return new WaitForSeconds(0.2f);   // outlast the shortened cooldown
            Assert.That(abilities.WaterBalloonReady, Is.True, "the cooldown must actually expire");

            var down2 = new PointerEventData(EventSystem.current) { position = Vector2.zero };
            control.OnPointerDown(down2);
            Assert.That(control.IsAiming, Is.True, "the SAME control instance must accept a second press once ready again");
            var drag2 = new PointerEventData(EventSystem.current) { position = new Vector2(90f, 0f) };
            control.OnDrag(drag2);
            control.OnPointerUp(drag2);

            Assert.That(abilities.WaterBalloonReady, Is.False,
                "a second real drag-release through the on-screen control must throw again, not silently no-op");
        }

        [UnityTest]
        public IEnumerator DraggingAndReleasingASecondTimeAfterCooldownBlinksAgain()
        {
            DevTuning.TeleportCooldownSeconds = 0.05f;
            WeaponSystemState.Acquire(AbilityKind.Teleport);
            PickupWallet.SetPowerCells(10);
            yield return null;

            var control = _hud.GetComponentInChildren<TeleportJoystickControl>(true);
            var abilities = _max.GetComponent<PlayerAbilities>();

            var down1 = new PointerEventData(EventSystem.current) { position = Vector2.zero };
            control.OnPointerDown(down1);
            var drag1 = new PointerEventData(EventSystem.current) { position = new Vector2(90f, 0f) };
            control.OnDrag(drag1);
            control.OnPointerUp(drag1);
            Assert.That(abilities.TeleportReady, Is.False, "the first blink must start the cooldown");

            yield return new WaitForSeconds(0.2f);   // outlast the shortened cooldown
            Assert.That(abilities.TeleportReady, Is.True, "the cooldown must actually expire");

            Vector3 beforeSecond = _max.transform.position;
            var down2 = new PointerEventData(EventSystem.current) { position = Vector2.zero };
            control.OnPointerDown(down2);
            Assert.That(control.IsAiming, Is.True, "the SAME control instance must accept a second press once ready again");
            var drag2 = new PointerEventData(EventSystem.current) { position = new Vector2(90f, 0f) };
            control.OnDrag(drag2);
            control.OnPointerUp(drag2);

            Assert.That(Vector3.Distance(_max.transform.position, beforeSecond), Is.GreaterThan(0.5f),
                "a second real drag-release through the on-screen Teleport joystick must blink again, not silently no-op");
        }

        // ---------------------------------------------------------------- layout

        [UnityTest]
        public IEnumerator TheNewControlsDoNotOverlapTheMoveStickOrTheBossBar()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            WeaponSystemState.Acquire(AbilityKind.Teleport);
            yield return null;

            Rect waterBalloon = ScreenRect(Find("Water Balloon Joystick"));
            Rect teleport = ScreenRect(Find("Teleport Joystick"));
            Rect move = ScreenRect(Find("Move Touch"));
            Rect bossBar = ScreenRect(Find("Boss Bar"));

            Assert.IsFalse(waterBalloon.Overlaps(move),
                "the Water Balloon joystick overlaps the move stick's touch pad");
            Assert.IsFalse(teleport.Overlaps(move),
                "the Teleport joystick overlaps the move stick's touch pad");
            Assert.IsFalse(waterBalloon.Overlaps(bossBar), "the Water Balloon joystick overlaps the boss bar");
            Assert.IsFalse(teleport.Overlaps(bossBar), "the Teleport joystick overlaps the boss bar");
            Assert.IsFalse(waterBalloon.Overlaps(teleport),
                "the Water Balloon joystick overlaps the Teleport joystick");
        }
    }
}
