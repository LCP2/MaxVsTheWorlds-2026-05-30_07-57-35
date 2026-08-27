using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-586: <see cref="ForceFieldBubble"/> is a solid, non-trigger collider, so a ramming robot's
    /// body is stopped by it — <see cref="RobotEnemy"/>'s own <c>OnControllerColliderHit</c> already
    /// treats the bubble as a wall to steer around. Because the body never reaches Max's contact
    /// radius, it never reached <see cref="PlayerAbilities.AbsorbForceFieldDamage"/> either — robots
    /// could grind on the bubble forever at no cost to the shield. Fix: the same wall-contact seam
    /// (extracted as <c>RobotEnemy.HandleWallContact</c>, since <see cref="ControllerColliderHit"/>
    /// has no public constructor for a test to build one) now reports a ram to the bubble, which
    /// drains the SAME absorb budget a real hit would, discarding the leaked overflow rather than
    /// forwarding it to <see cref="PlayerHealth"/>.
    /// </summary>
    public sealed class MV586ForceFieldRamTests
    {
        private GameObject _max;
        private PlayerAbilities _abilities;
        private PlayerHealth _health;
        private GameObject _robotGo;
        private RobotEnemy _robot;

        private static readonly FieldInfo AbsorbRemainingField =
            typeof(PlayerAbilities).GetField("_forceFieldAbsorbRemaining", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo BubbleField =
            typeof(PlayerAbilities).GetField("_forceFieldBubble", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo RamCooldownField =
            typeof(RobotEnemy).GetField("_forceFieldRamCooldownTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo HandleWallContactMethod =
            typeof(RobotEnemy).GetMethod("HandleWallContact", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            WeaponSystemState.Reset();
            RigState.Reset();
            RobotEnemy.ResetRegistry();

            _max = new GameObject("Max", typeof(CharacterController), typeof(PlayerController));
            _abilities = _max.GetComponent<PlayerAbilities>();
            if (_abilities == null) _abilities = _max.AddComponent<PlayerAbilities>();
            _health = _max.AddComponent<PlayerHealth>();
            _health.Initialize(); // Awake is not a reliable side effect of AddComponent outside Play mode

            _robotGo = new GameObject("Rusher", typeof(CharacterController));
            _robot = _robotGo.AddComponent<RobotEnemy>();
            _robot.Apply(EnemyArchetype.Rusher);
        }

        [TearDown]
        public void TearDown()
        {
            if (_robotGo != null) Object.DestroyImmediate(_robotGo);
            if (_max != null) Object.DestroyImmediate(_max);
            RobotEnemy.ResetRegistry();
            RigState.Reset();
            WeaponSystemState.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void RammingRobot_DrainsContactDamagePerRateWindow_PopsAtZero_NeverTouchesMaxHealth()
        {
            const float budget = 20f; // small enough to pop within a handful of rams
            DevTuning.ForceFieldAbsorbCap = budget;
            _abilities.ForceActivateForceFieldForTuning();
            var bubble = (ForceFieldBubble)BubbleField.GetValue(_abilities);
            Assert.IsNotNull(bubble, "precondition: activating the field must spawn the bubble");

            float startHealth = _health.Current;
            float contactDamage = EnemyArchetype.Rusher.ContactDamage;

            // Ram 1: reduces the absorb budget by exactly the ramming robot's own ContactDamage.
            HandleWallContactMethod.Invoke(_robot, new object[] { bubble.Collider, Vector3.back });
            float afterFirstRam = (float)AbsorbRemainingField.GetValue(_abilities);
            Assert.That(afterFirstRam, Is.EqualTo(budget - contactDamage).Within(1e-4f),
                "a ram must drain the shield by exactly the ramming robot's own ContactDamage");
            Assert.That(_health.Current, Is.EqualTo(startHealth).Within(1e-4f),
                "a ram the bubble already physically blocked must never touch Max's health");

            // Ram 2, still inside the same robot's rate window: drains nothing.
            HandleWallContactMethod.Invoke(_robot, new object[] { bubble.Collider, Vector3.back });
            Assert.That((float)AbsorbRemainingField.GetValue(_abilities), Is.EqualTo(afterFirstRam).Within(1e-4f),
                "a second ram from the same robot inside its rate window must drain nothing");

            // Simulate the rate window elapsing (same reflection-set-state idiom as MV434BodySeparationTests).
            RamCooldownField.SetValue(_robot, 0f);

            // Ram 3: 8 left, 12 incoming — exceeds the remaining budget, popping the field. The leaked
            // overflow must still never reach Max's health, even on this pop-causing ram.
            Assert.IsTrue(_abilities.ForceFieldActive, "precondition: the field must still be up before the popping ram");
            LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called from edit mode"));
            HandleWallContactMethod.Invoke(_robot, new object[] { bubble.Collider, Vector3.back });

            Assert.IsFalse(_abilities.ForceFieldActive, "a ram that exhausts the budget must pop the field, same as a real hit");
            Assert.That(_abilities.ForceFieldCooldownRemaining, Is.GreaterThan(0f),
                "popping via a ram must start the same cooldown a real pop does");
            Assert.That(_health.Current, Is.EqualTo(startHealth).Within(1e-4f),
                "the pop-causing ram's leaked overflow must be discarded, not applied to Max's health");
        }
    }
}
