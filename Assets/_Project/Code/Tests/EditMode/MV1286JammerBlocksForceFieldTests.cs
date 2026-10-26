using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1286 — a live Jammer's field blocks Max's triggered abilities. Fails to compile on a base
    /// commit with no <see cref="AbilityJam"/> type at all (same "brand new shared query, fails to
    /// compile" proof the sibling Jammer/Shield-Projector tickets use for their own new surface).
    ///
    /// ONE new EditMode test (MV-465 Rule 1) — the ticket's own AC1 scenario, both halves in one
    /// method since they're the same regression on the same boundary: <see cref="PlayerAbilities.TryActivateForceField"/>
    /// returns false with a Jammer 5 m from Max (inside <see cref="AbilityJam.JamRadius"/>'s 6 m) and
    /// true with it repositioned to 7 m (outside). Tier 2 (resolved values): asserts the real method's
    /// boolean outcome, never an authored constant.
    /// </summary>
    public sealed class MV1286JammerBlocksForceFieldTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        // Well clear of every other EditMode fixture's own coordinates (same reasoning
        // MV1280JammerApproachMovementTests gives for its own origin).
        private static readonly Vector3 RigOrigin = new Vector3(81234f, 0f, -67890f);
        private static readonly Vector3 MaxPos = RigOrigin;

        private GameObject _max;
        private PlayerAbilities _abilities;
        private GameObject _spawnerGo;
        private RobotEnemy _jammer;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            DevTuning.Reset();
            PickupWallet.Reset();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);

            _max = new GameObject("MV1286 Max", typeof(CharacterController), typeof(PlayerController));
            _max.transform.position = MaxPos;
            _abilities = _max.GetComponent<PlayerAbilities>();
            if (_abilities == null) _abilities = _max.AddComponent<PlayerAbilities>();

            WeaponSystemState.Acquire(AbilityKind.ForceField);
            PickupWallet.SetPowerCells(9999); // AC1's own "with enough cells"

            _spawnerGo = new GameObject("MV1286 Spawner");
            _spawnerGo.transform.position = MaxPos;
            var spawner = _spawnerGo.AddComponent<EnemySpawner>();

            var jammerList = spawner.SpawnExact(EnemyKind.Jammer, 1);
            Assert.AreEqual(1, jammerList.Count, "test setup: SpawnExact didn't spawn the Jammer");
            _jammer = jammerList[0];
            RobotOnEnableMethod.Invoke(_jammer, null); // seeds RobotEnemy.Active, same idiom MV1280 uses
        }

        [TearDown]
        public void TearDown()
        {
            if (_spawnerGo != null) Object.DestroyImmediate(_spawnerGo); // destroys every spawned robot too
            if (_max != null) Object.DestroyImmediate(_max);
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            WeaponSystemState.Reset();
            DevTuning.Reset();
            PickupWallet.Reset();
        }

        [Test]
        public void TryActivateForceField_FalseAtFiveMetres_TrueAtSevenMetres()
        {
            _jammer.transform.position = MaxPos + new Vector3(5f, 0f, 0f);

            Assert.That(_abilities.TryActivateForceField(), Is.False,
                "a Jammer 5 m from Max is inside the 6 m field and must block the activation");

            _jammer.transform.position = MaxPos + new Vector3(7f, 0f, 0f);

            Assert.That(_abilities.TryActivateForceField(), Is.True,
                "a Jammer 7 m from Max is outside the 6 m field and must not block the activation");
        }
    }
}
