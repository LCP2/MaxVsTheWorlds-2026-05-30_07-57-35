using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-944 — Lee's play of TestFlight 0.9.9 (areas 10-14, 2026-09-25): a ground-floor battle area
    /// with an upper walkway ("deck") above it read as ONE fight — Max's rockets (and every other
    /// weapon) hit the deck's robots while he stood on the floor, and robots up there fired back down at
    /// him. Root cause: no targeting or damage site anywhere in the combat code compared the shooter's
    /// combat level (floor vs deck) against the receiver's — <see cref="PlayerRocket.ApplySplashDamage"/>
    /// queried a bare 3D radius around the impact point with no Y/level gate at all.
    ///
    /// The fix adds one shared comparison, <see cref="CombatLevel.SameLevel"/> (off the new
    /// <see cref="MapData.IsOnDeck"/>), and applies it at every targeting/damage site the ticket names.
    /// This test proves the one PlayerRocket splash-damage case the ticket's own AC calls out by name: a
    /// projectile fired by a floor-level Max must not damage a deck-level robot, even once the rocket's
    /// own homing has climbed to the robot's height by the time it detonates (see
    /// <see cref="PlayerRocket"/>'s own <c>_origin</c> field doc comment for why the level check has to
    /// key off the LAUNCH point, not the live detonation point, for exactly this reason).
    ///
    /// Fails on the commit before this ticket: firing this same rocket at the same deck robot damages it
    /// — see the fix comment for the quoted pre-fix health delta.
    /// </summary>
    public sealed class MV944FloorDeckCrossLevelDamageTests
    {
        private const float Dt = 1f / 60f;
        private const float RocketSpeed = 14f;

        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _pathGo;
        private GameObject _robotGo;

        [SetUp]
        [TearDown]
        public void Clear()
        {
            PlayerRocket.DestroyAllActive();
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
            if (_pathGo != null) { Object.DestroyImmediate(_pathGo); _pathGo = null; }
            if (_robotGo != null) { Object.DestroyImmediate(_robotGo); _robotGo = null; }
        }

        [Test]
        public void RocketFiredFromTheFloor_CannotDamageARobotStandingOnTheDeckAbove()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            Assert.IsNotNull(BackyardPathMapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");

            // a12 ("Pipe Gallery") authors a garrisoned deck in-place over its own floor (MV-896) — the
            // exact "ground floor + upper walkway" shape the ticket's own evidence describes.
            MapEntity deck = map.Entity("a12_deck1");
            Assert.IsNotNull(deck, "MV-944: world2_config.json must still author a12's own deck");
            Assert.Greater(deck.height, 0f,
                "setup failure: a12_deck1 must be elevated above the floor for this to be a floor/deck test at all");

            _pathGo = new GameObject("MV944-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            BackyardPathMapField.SetValue(path, map);

            // The deck robot: standing dead centre on the deck, at the deck's own top height.
            _robotGo = new GameObject("DeckRusher");
            _robotGo.AddComponent<CharacterController>();
            RobotEnemy robot = _robotGo.AddComponent<RobotEnemy>();
            robot.Apply(EnemyArchetype.Rusher);
            robot.transform.position = new Vector3(deck.x, deck.height, deck.z);
            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

            float healthBeforeFire = robot.HealthCurrent;
            Assert.Greater(healthBeforeFire, 0f, "setup failure: the robot must start alive with health to lose");

            // Max fires from the floor directly beneath the deck (y=0, well under the deck's own height
            // — floor-level by construction).
            var floorOrigin = new Vector3(deck.x, 0f, deck.z - 1f);

            PlayerRocket rocket = PlayerRocket.Fire(floorOrigin, robot.transform, RocketSpeed,
                damage: 40f, splashRadius: 2f, cluster: false, launchYawRight: true);

            bool detonated = false;
            for (int i = 0; i < 300 && !detonated; i++)   // 300 * 1/60f = 5s simulated — past the 4s fuel budget
            {
                rocket.Tick(Dt);
                if (rocket == null) detonated = true;
            }

            Assert.IsTrue(detonated,
                "setup failure: the rocket never detonated within 5s (past its own 4s fuel budget) against " +
                "a stationary robot 1m away in XZ — this test proves nothing about cross-level damage if it " +
                "never even lands a hit attempt");

            Assert.AreEqual(healthBeforeFire, robot.HealthCurrent,
                "MV-944: a rocket Max fired from the FLOOR damaged a robot standing on the DECK above " +
                "him — floor and deck must fight separately, so this splash must never have applied");
        }
    }
}
