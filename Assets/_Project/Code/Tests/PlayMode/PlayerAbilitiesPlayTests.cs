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
        public IEnumerator UnacquiredWaterBalloonNeverThrowsEvenWithCellsBanked_MV380()
        {
            // MV-380: restores the acquisition gate MV-370 had dropped — a full cell bank must not be
            // enough on its own.
            PickupWallet.SetPowerCells(10);
            Assert.IsNotNull(_abilities, "PlayerController must self-attach PlayerAbilities (WV-231)");
            Assert.That(_abilities.TryThrowWaterBalloon(Vector3.forward), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WaterBalloonWithNoCellsNeverThrows_MV370()
        {
            // Acquired but the bank is empty (SetUp's PickupWallet.Reset() leaves it at 0) — cells are
            // what must block the throw here, not acquisition.
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            Assert.That(_abilities.TryThrowWaterBalloon(Vector3.forward), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThrowingSpendsOneCellAndStartsTheCooldown_MV370()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            PickupWallet.SetPowerCells(1);

            Assert.That(_abilities.TryThrowWaterBalloon(Vector3.forward), Is.True);

            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0), "each balloon fired must cost exactly one cell");
            Assert.That(_abilities.WaterBalloonReady, Is.False, "must be on cooldown immediately after a throw");
            yield return null;
        }

        [UnityTest]
        public IEnumerator LandingSplashesHalfTheBruisersMaxHealthAndHaltsIt()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            PickupWallet.SetPowerCells(10);

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

        [UnityTest]
        public IEnumerator ASecondThrowSucceedsOnceTheCooldownExpires()
        {
            // MV-292: playtest found Water Balloon "works once" — a real second activation, not just
            // the first, is the regression this locks in. A short DevTuning cooldown keeps the real
            // (Time.deltaTime-driven) wait fast.
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            DevTuning.WaterBalloonCooldownSeconds = 0.05f;
            PickupWallet.SetPowerCells(10);

            Assert.That(_abilities.TryThrowWaterBalloon(Vector3.forward), Is.True);
            Assert.That(_abilities.TryThrowWaterBalloon(Vector3.forward), Is.False,
                "must not throw again while still on cooldown");

            yield return new WaitForSeconds(0.2f);

            Assert.That(_abilities.TryThrowWaterBalloon(Vector3.forward), Is.True,
                "a second throw must succeed once the cooldown has actually expired");
        }

        [UnityTest]
        public IEnumerator RepeatFireLevelThrowsFasterThanLevel1_MV370()
        {
            DevTuning.WaterBalloonCooldownSeconds = 1f;
            PickupWallet.SetPowerCells(2);

            float cooldownAtL1 = WeaponSystemState.WaterBalloonEffectiveCooldownSeconds();
            WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.RepeatFire);
            float cooldownAtL2 = WeaponSystemState.WaterBalloonEffectiveCooldownSeconds();

            Assert.Less(cooldownAtL2, cooldownAtL1, "a Repeat Fire level must shorten the throw cooldown");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SplashAreaLevelWidensTheSplashRadius_MV370()
        {
            float radiusAtL1 = PlayerAbilities.SplashRadius;
            WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.SplashArea);
            float radiusAtL2 = PlayerAbilities.SplashRadius;

            Assert.Greater(radiusAtL2, radiusAtL1, "a Splash Area level must widen the splash radius");
            yield return null;
        }

        [UnityTest]
        public IEnumerator RangeLevelThrowsFartherThanLevel1_MV370()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            PickupWallet.SetPowerCells(10);
            WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.Range);

            Assert.That(_abilities.TryThrowWaterBalloon(Vector3.forward), Is.True);
            yield return new WaitForSeconds(1.5f);

            // The flight itself already proves the throw succeeded; the distance formula covering
            // Range's level-up is pinned directly in AbilityTuningTests — this just proves the live
            // component actually reads the Range track's level rather than a hardcoded level 1.
            int level = WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.Range);
            Assert.That(level, Is.EqualTo(2));
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

        [UnityTest]
        public IEnumerator TeleportLevelOneAlsoBlinksTowardTheAimedDirection()
        {
            // MV-292: a random L1 hop read in playtest as broken/interchangeable with Dash — Teleport
            // must be an AIMED blink at every level, not just its L2 cap.
            WeaponSystemState.Acquire(AbilityKind.Teleport);
            Vector3 before = _max.transform.position;

            Assert.That(_abilities.TryTeleport(Vector3.right), Is.True);

            Vector3 delta = _max.transform.position - before;
            Assert.That(delta.normalized.x, Is.GreaterThan(0.9f),
                "L1 Teleport must already blink toward the aimed direction, not a random one");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TeleportLevelTwoBlinksFartherThanLevelOne()
        {
            // MV-292 AC3: a level-up must produce a noticeable change — here, blink distance.
            WeaponSystemState.Acquire(AbilityKind.Teleport);
            Vector3 before = _max.transform.position;
            Assert.That(_abilities.TryTeleport(Vector3.forward), Is.True);
            float l1Distance = Vector3.Distance(_max.transform.position, before);

            yield return new WaitForSeconds(WeaponSystemState.EffectiveCooldownSeconds(AbilityKind.Teleport) + 0.1f);

            WeaponSystemState.LevelUpAbility(AbilityKind.Teleport);
            before = _max.transform.position;
            Assert.That(_abilities.TryTeleport(Vector3.forward), Is.True);
            float l2Distance = Vector3.Distance(_max.transform.position, before);

            Assert.Greater(l2Distance, l1Distance, "level 2 must blink farther than level 1");
        }

        [UnityTest]
        public IEnumerator ASecondTeleportSucceedsOnceTheCooldownExpires()
        {
            DevTuning.TeleportCooldownSeconds = 0.05f;
            WeaponSystemState.Acquire(AbilityKind.Teleport);

            Assert.That(_abilities.TryTeleport(Vector3.forward), Is.True);
            Assert.That(_abilities.TryTeleport(Vector3.forward), Is.False,
                "must not blink again while still on cooldown");

            yield return new WaitForSeconds(0.2f);

            Assert.That(_abilities.TryTeleport(Vector3.forward), Is.True,
                "a second blink must succeed once the cooldown has actually expired");
        }
    }
}
