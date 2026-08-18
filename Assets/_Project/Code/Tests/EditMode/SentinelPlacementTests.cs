using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-399: Sentinel deployment moves from "always at Max's feet" (MV-362) to an aimed placement
    /// joystick, reusing Teleport/Water Balloon's shared press/drag/release layer (MV-372). Covers the
    /// new pure math (<see cref="MapZone.Clamp"/>), the aimed-position overload's cost/cap/overlap
    /// gating, and the joystick control's own arm/disarm + placement-reticle lifecycle — the same shape
    /// <c>AbilityJoystickArmDisarmTests</c> already proves for Teleport/Water Balloon. MV-422 collapses
    /// the Wall/Gunner split to one sentinel, one <see cref="SentinelJoystickControl"/>.
    /// </summary>
    public sealed class SentinelPlacementTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            DevTuning.Reset();
            Sentinel.DestroyAllActive();
            Sentinel.ResetRegistry();
            RobotEnemy.ResetRegistry();
        }

        // ---------------------------------------------------------------- MapZone.Clamp

        [Test]
        public void ClampLeavesAPointInsideTheRoomUntouched()
        {
            var zone = new MapZone { x = 0f, z = 0f, width = 10f, depth = 10f };
            Vector3 clamped = zone.Clamp(new Vector3(1f, 0f, -2f), 1f);
            Assert.That(clamped, Is.EqualTo(new Vector3(1f, 0f, -2f)));
        }

        [Test]
        public void ClampPullsAPointBeyondTheWallBackInsideByTheMargin()
        {
            var zone = new MapZone { x = 0f, z = 0f, width = 10f, depth = 10f };
            Vector3 clamped = zone.Clamp(new Vector3(20f, 0f, 0f), 1.5f);
            Assert.That(clamped.x, Is.EqualTo(3.5f).Within(1e-4f),
                "a 10m room's edge is at x=5; a 1.5m margin must pull the point back to x=3.5");
        }

        [Test]
        public void ClampDegradesToTheRoomsCentreWhenTheMarginExceedsHalfTheRoom()
        {
            var zone = new MapZone { x = 2f, z = -1f, width = 2f, depth = 2f };
            Vector3 clamped = zone.Clamp(new Vector3(50f, 0f, 50f), 5f);
            Assert.That(clamped.x, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(clamped.z, Is.EqualTo(-1f).Within(1e-4f));
        }

        // ---------------------------------------------------------------- PlayerAbilities aimed overload

        private static GameObject NewMax()
        {
            var go = new GameObject("Max");
            go.AddComponent<PlayerAbilities>();
            return go;
        }

        [Test]
        public void AimedDeployPlacesTheSentinelAtTheAimedPointNotMaxsFeet()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            PickupWallet.SetPowerCells(100);
            var maxGo = NewMax();
            var abilities = maxGo.GetComponent<PlayerAbilities>();
            try
            {
                var aimedPoint = new Vector3(5f, 0f, -3f);
                Assert.That(abilities.TryDeploySentinel(aimedPoint), Is.True);

                Assert.That(Sentinel.Active.Count, Is.EqualTo(1));
                Assert.That(Sentinel.Active[0].transform.position, Is.EqualTo(aimedPoint),
                    "MV-399: the aimed overload must deploy where the player aimed, not at Max's own position");
            }
            finally { Sentinel.DestroyAllActive(); Object.DestroyImmediate(maxGo); }
        }

        [Test]
        public void ParameterlessDeployStillDeploysAtMaxsOwnPosition()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            PickupWallet.SetPowerCells(100);
            var maxGo = NewMax();
            maxGo.transform.position = new Vector3(4f, 0f, 4f);
            var abilities = maxGo.GetComponent<PlayerAbilities>();
            try
            {
                Assert.That(abilities.TryDeploySentinel(), Is.True,
                    "the zero-arg convenience overload must keep working for existing callers/tests");
                Assert.That(Sentinel.Active[0].transform.position, Is.EqualTo(maxGo.transform.position));
            }
            finally { Sentinel.DestroyAllActive(); Object.DestroyImmediate(maxGo); }
        }

        [Test]
        public void AimedDeployIsRejectedWhenThePointAlreadyHoldsASentinel()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            RigState.AcquireCap("u_hp"); // reaches u_slt (its own RIG child)
            RigState.AcquireCap("u_slt"); // u_slt to L1
            RigState.TrySpendPart("u_slt"); // u_slt to L2 -> 2 slots free (Mathf.Max(1, level)) — proves this is an overlap rejection, not a cap rejection
            PickupWallet.SetPowerCells(100);
            var maxGo = NewMax();
            var abilities = maxGo.GetComponent<PlayerAbilities>();
            try
            {
                var point = new Vector3(2f, 0f, 2f);
                Assert.That(abilities.TryDeploySentinel(point), Is.True);

                int cellsBefore = PickupWallet.PowerCells;
                Assert.That(abilities.TryDeploySentinel(new Vector3(2.1f, 0f, 2f)), Is.False,
                    "a second sentinel must not be allowed to land on top of the first — the slot cap " +
                    "alone (2 free) would not have caught this");
                Assert.That(PickupWallet.PowerCells, Is.EqualTo(cellsBefore),
                    "a rejected placement must not spend the cost");
                Assert.That(Sentinel.Active.Count, Is.EqualTo(1));
            }
            finally { Sentinel.DestroyAllActive(); Object.DestroyImmediate(maxGo); }
        }

        [Test]
        public void IsValidSentinelPlacementClearsOnceTheBlockingSentinelIsDestroyed()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            PickupWallet.SetPowerCells(100);
            var maxGo = NewMax();
            var abilities = maxGo.GetComponent<PlayerAbilities>();
            try
            {
                var point = new Vector3(-3f, 0f, 1f);
                abilities.TryDeploySentinel(point);
                Assert.That(abilities.IsValidSentinelPlacement(point), Is.False);

                Sentinel deployed = Sentinel.Active[0];
                deployed.TakeDamage(new DamageInfo(deployed.HealthCurrent, Vector3.zero, Vector3.forward, Team.Enemy));

                Assert.That(abilities.IsValidSentinelPlacement(point), Is.True,
                    "a destroyed sentinel must free the point it occupied, same as it frees its deployment slot (MV-397)");
            }
            finally { Sentinel.DestroyAllActive(); Object.DestroyImmediate(maxGo); }
        }

        [Test]
        public void AimedDeployIsRejectedWhenThePointOverlapsALiveRobot()
        {
            // RobotEnemy.Active is only populated by OnEnable, which Unity does not invoke for plain
            // MonoBehaviours outside Play mode — so OnEnable is invoked directly here, the same
            // reflection-a-private-Unity-callback idiom SentinelAreaCrossingTests.InvokeUpdate uses.
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            PickupWallet.SetPowerCells(100);
            var maxGo = NewMax();
            var abilities = maxGo.GetComponent<PlayerAbilities>();
            var robotGo = new GameObject("Robot");
            robotGo.transform.position = new Vector3(6f, 0f, 6f);
            robotGo.AddComponent<CharacterController>();
            var robot = robotGo.AddComponent<RobotEnemy>();
            try
            {
                typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(robot, null);

                Assert.That(abilities.TryDeploySentinel(robotGo.transform.position), Is.False,
                    "a sentinel must not be dropped on top of a live robot");
                Assert.That(Sentinel.Active.Count, Is.EqualTo(0));
            }
            finally
            {
                Sentinel.DestroyAllActive();
                RobotEnemy.ResetRegistry();
                Object.DestroyImmediate(maxGo);
                Object.DestroyImmediate(robotGo);
            }
        }

        // ---------------------------------------------------------------- SentinelJoystickControl

        private GameObject _max;
        private GameObject _pad;

        private SentinelJoystickControl NewControl()
        {
            var abilities = _max.GetComponent<PlayerAbilities>();
            var control = _pad.AddComponent<SentinelJoystickControl>();
            var knob = new GameObject("Knob", typeof(RectTransform)).GetComponent<RectTransform>();
            control.Init(knob, _max.transform, abilities, rings: null);
            return control;
        }

        private static PointerEventData At(Vector2 pos) => new PointerEventData(EventSystem.current) { position = pos };

        [Test]
        public void PressingShowsThePlacementCircleAndReleasingHidesIt()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            PickupWallet.SetPowerCells(100);
            _max = NewMax();
            _pad = new GameObject("Pad", typeof(RectTransform), typeof(Image));
            var control = NewControl();
            try
            {
                control.OnPointerDown(At(new Vector2(0f, 0f)));
                Assert.That(control.PlacementCircleVisible, Is.True,
                    "MV-399 AC1: activating deployment must show a placement reticle");

                control.OnPointerUp(At(new Vector2(0f, 0f)));
                Assert.That(control.PlacementCircleVisible, Is.False);
            }
            finally { Sentinel.DestroyAllActive(); Object.DestroyImmediate(_max); Object.DestroyImmediate(_pad); }
        }

        [Test]
        public void DraggingPastTheDeadZoneAndReleasingDeploysAwayFromMax()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            PickupWallet.SetPowerCells(100);
            _max = NewMax();
            _pad = new GameObject("Pad", typeof(RectTransform), typeof(Image));
            var control = NewControl();
            try
            {
                control.OnPointerDown(At(new Vector2(0f, 0f)));
                control.OnDrag(At(new Vector2(0f, 40f))); // well past the 13.5px arm threshold
                control.OnPointerUp(At(new Vector2(0f, 40f)));

                Assert.That(Sentinel.Active.Count, Is.EqualTo(1));
                Assert.That(Vector3.Distance(Sentinel.Active[0].transform.position, _max.transform.position),
                    Is.GreaterThan(0.1f),
                    "MV-399 AC2: an armed release must deploy away from Max, not at his feet");
            }
            finally { Sentinel.DestroyAllActive(); Object.DestroyImmediate(_max); Object.DestroyImmediate(_pad); }
        }

        [Test]
        public void ReleasingWhileStillInTheDeadZoneDeploysNothing()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            PickupWallet.SetPowerCells(100); // clamps to PickupWallet.Capacity (20 at Cell Storage level 0)
            int cellsBefore = PickupWallet.PowerCells;
            _max = NewMax();
            _pad = new GameObject("Pad", typeof(RectTransform), typeof(Image));
            var control = NewControl();
            try
            {
                control.OnPointerDown(At(new Vector2(0f, 0f)));
                control.OnDrag(At(new Vector2(0f, 2f))); // inside the dead zone
                control.OnPointerUp(At(new Vector2(0f, 2f)));

                Assert.That(Sentinel.Active.Count, Is.EqualTo(0));
                Assert.That(PickupWallet.PowerCells, Is.EqualTo(cellsBefore), "an unarmed release must spend nothing");
            }
            finally { Sentinel.DestroyAllActive(); Object.DestroyImmediate(_max); Object.DestroyImmediate(_pad); }
        }
    }
}
