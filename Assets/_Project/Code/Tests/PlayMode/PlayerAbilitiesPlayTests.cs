using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Water Balloon's actual throw/landing/splash and Teleport's blink (WV-231, spec §6a) — the part
    /// WV-230's data model and WV-241's art deliberately left undone (see their own doc comments,
    /// e.g. <c>WaterBalloonSplashVfx</c>: "Splash damage and the robot-stopping effect are WV-231's").
    /// Against a real <see cref="RobotEnemy"/> and real physics, not a mock: percentage damage, the
    /// halt, the cell spend and the cooldown gate all have to hold together for this to be worth
    /// anything.
    /// </summary>
    public sealed class PlayerAbilitiesPlayTests
    {
        private GameObject _max;
        private GameObject _robot;
        private PlayerAbilities _abilities;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            WeaponSystemState.Reset();
            DevTuning.Reset();
            PickupWallet.Reset();

            _max = new GameObject("Max", typeof(CharacterController), typeof(PlayerController));
            _max.tag = "Player";
            yield return null;
            _abilities = _max.GetComponent<PlayerAbilities>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_robot != null) Object.Destroy(_robot);
            if (_max != null) Object.Destroy(_max);
            WeaponSystemState.Reset();
            DevTuning.Reset();
            PickupWallet.Reset();
            yield return null;
        }

        private RobotEnemy SpawnBruiser(Vector3 at)
        {
            _robot = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _robot.name = "Bruiser";
            _robot.transform.position = at;
            _robot.AddComponent<CharacterController>();
            var e = _robot.AddComponent<RobotEnemy>();
            e.Apply(EnemyArchetype.Bruiser);
            return e;
        }

        // ---------------------------------------------------------------- Water Balloon

        [UnityTest]
        public IEnumerator UnacquiredWaterBalloonNeverThrows()
        {
            Assert.IsNotNull(_abilities, "PlayerController must self-attach PlayerAbilities (WV-231)");
            PickupWallet.SetPowerCells(10);
            Assert.That(_abilities.TryThrowWaterBalloon(Vector3.forward), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThrowingStartsTheCooldown()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);

            Assert.That(_abilities.TryThrowWaterBalloon(Vector3.forward), Is.True);

            Assert.That(_abilities.WaterBalloonReady, Is.False, "must be on cooldown immediately after a throw");
            yield return null;
        }

        [UnityTest]
        public IEnumerator LandingSplashesHalfTheBruisersMaxHealthAndHaltsIt()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);

            float level1Distance = AbilityTuning.WaterBalloonDistance(
                1, AbilityTuning.DefaultWaterBalloonBaseDistance, AbilityTuning.DefaultWaterBalloonDistancePerLevel);
            var robot = SpawnBruiser(_max.transform.position + Vector3.forward * level1Distance);
            yield return null;   // let the robot's Awake seed its health

            float maxHealth = robot.MaxHealth;
            Assert.That(_abilities.TryThrowWaterBalloon(Vector3.forward), Is.True);

            // Flight takes real time (distance / flight speed); give it comfortable headroom.
            yield return new WaitForSeconds(1.5f);

            float expectedDamage = maxHealth * (AbilityTuning.DefaultWaterBalloonDamagePct / 100f);
            Assert.That(robot.HealthCurrent, Is.EqualTo(maxHealth - expectedDamage).Within(0.5f),
                "the splash must deal the spec's percentage of the robot's OWN max health");
            Assert.That(robot.IsHalted, Is.True, "a robot the splash hits must be halted");
        }

        // ---------------------------------------------------------------- Teleport

        [UnityTest]
        public IEnumerator UnacquiredTeleportNeverBlinks()
        {
            Vector3 before = _max.transform.position;
            Assert.That(_abilities.TryTeleport(Vector3.forward), Is.False);
            Assert.That(_max.transform.position, Is.EqualTo(before));
            yield return null;
        }

        [UnityTest]
        public IEnumerator AcquiredTeleportMovesMaxAndStartsItsCooldown()
        {
            WeaponSystemState.Acquire(AbilityKind.Teleport);
            PickupWallet.SetPowerCells(10);
            Vector3 before = _max.transform.position;

            Assert.That(_abilities.TryTeleport(Vector3.forward), Is.True);

            Assert.That(Vector3.Distance(_max.transform.position, before), Is.GreaterThan(0.5f),
                "a successful blink must actually move Max");
            Assert.That(_abilities.TeleportReady, Is.False, "must be on cooldown immediately after a blink");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TeleportLevelTwoBlinksTowardTheAimedDirectionNotRandomly()
        {
            WeaponSystemState.Acquire(AbilityKind.Teleport);
            WeaponSystemState.LevelUpAbility(AbilityKind.Teleport);   // L2: aimed
            PickupWallet.SetPowerCells(10);
            Vector3 before = _max.transform.position;

            Assert.That(_abilities.TryTeleport(Vector3.right), Is.True);

            Vector3 delta = _max.transform.position - before;
            Assert.That(delta.normalized.x, Is.GreaterThan(0.9f),
                "L2 Teleport must blink toward the aimed direction, not a random one");
            yield return null;
        }
    }
}
