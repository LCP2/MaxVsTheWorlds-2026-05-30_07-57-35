using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1048 — Lee's report: "The rockets are being wasted. They keep launching against solid objects,
    /// especially gates, until they're exhausted." <c>NearestAwakeRobotInRange</c> had no line-of-sight
    /// test, so a robot locked behind a closed gate or wall inside 12 m was still picked as the target;
    /// <c>PlayerRocket</c> then flew at it and detonated on the obstruction every single salvo until ammo
    /// ran out. The one new test the testing policy allows this ticket.
    ///
    /// Proven to fail at <c>ShoulderRack.cs</c>'s pre-fix state (main @ 6a4aa47, unchanged since
    /// MV-1028 @ 12419fa): with a Cover-layer BoxCollider standing between the rack and an awake robot
    /// 6 m away, the old range/level-only filter still picked the robot, spending a cell on a target it
    /// could never reach, and NUnit stopped the test on the very first assertion:
    /// <c>Guards MV-1048: a robot with no clear line of sight must not be selected, so no cell is spent
    /// on an unreachable target -- Expected: 50 But was: 49</c>. Passes clean against the fix below,
    /// which rejects a candidate unless <see cref="HomingSteering.BlockedByGeometry"/> reports the
    /// segment from the rack's own muzzle to the robot's centre clear — the same geometry test the
    /// rocket itself dies on in flight, so selection and flight can't disagree.
    /// </summary>
    public sealed class MV1048ShoulderRackLineOfSightTests
    {
        private const float Dt = 1f / 60f;

        // Same split MV1028ShoulderRackClutterTests documents: EditMode's single-tick test body never
        // pumps the editor loop far enough for AddComponent<RobotEnemy>()'s OnEnable to run
        // synchronously, so NewEnemy must seed RobotEnemy.Active itself.
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
            RobotEnemy.ResetRegistry();
            RigBoard.UseWorld(1);
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
            RobotEnemy.ResetRegistry();
            RigBoard.ResetForTests();
        }

        [Test]
        public void ARobotLockedBehindCover_IsNotSelected_UntilTheCoverIsGone()
        {
            // Guards MV-1048
            WeaponSystemState.SecondaryKind = SecondaryKind.ShoulderRack;
            PickupWallet.SetPowerCellSecondary(50);

            var maxGo = new GameObject("Max");
            var rack = maxGo.AddComponent<ShoulderRack>();

            RobotEnemy rusher = NewEnemy(EnemyArchetype.Rusher, new Vector3(0f, 0f, 6f));

            var gate = new GameObject("MV1048-gate");
            gate.transform.position = new Vector3(0f, 0f, 3f);
            gate.transform.localScale = new Vector3(4f, 3f, 0.6f);
            gate.AddComponent<BoxCollider>();
            CoverLayer.Assign(gate);

            try
            {
                RigState.RestoreSnapshot(new Dictionary<string, int> { { "s_rkt", 1 }, { "s_sal", 1 } },
                    new[] { "SECONDARY" });

                Physics.SyncTransforms();

                Advance(rack, 1.8f);

                Assert.That(PickupWallet.PowerCellsSecondary, Is.EqualTo(50),
                    "Guards MV-1048: a robot with no clear line of sight must not be selected, so no cell is spent on an unreachable target");
                Assert.That(PlayerRocket.Active.Count, Is.EqualTo(0),
                    "Guards MV-1048: no rocket may fire at a target blocked by a gate/wall");

                Object.DestroyImmediate(gate);
                gate = null;
                Physics.SyncTransforms();

                Advance(rack, 1.8f);

                Assert.That(PickupWallet.PowerCellsSecondary, Is.EqualTo(49),
                    "Guards MV-1048: once the obstruction is gone, the now-clear robot must be selected and a cell spent on it");
                Assert.That(PlayerRocket.Active.Count, Is.EqualTo(1),
                    "Guards MV-1048: once the obstruction is gone, a rocket must actually fire at the robot");
                Assert.That(PlayerRocket.Active[0].TargetForTests, Is.EqualTo(rusher.transform),
                    "Guards MV-1048: the rocket fired once clear must target the only awake robot in range");
            }
            finally
            {
                PlayerRocket.DestroyAllActive();
                if (gate != null) Object.DestroyImmediate(gate);
                Object.DestroyImmediate(rusher.gameObject);
                Object.DestroyImmediate(maxGo);
            }
        }

        /// <summary>Same overshoot-margin idiom <c>MV694ShoulderRackTests.Advance</c> uses.</summary>
        private static void Advance(ShoulderRack rack, float seconds)
        {
            int steps = Mathf.CeilToInt(seconds / Dt) + 1;
            for (int i = 0; i < steps; i++) rack.Tick(Dt);
        }

        private static RobotEnemy NewEnemy(EnemyArchetype archetype, Vector3 position)
        {
            var go = new GameObject($"Enemy {archetype.Kind}");
            go.transform.position = position;
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            e.Apply(archetype);
            RobotOnEnableMethod.Invoke(e, null); // seeds RobotEnemy.Active — see the class doc above
            return e;
        }
    }
}
