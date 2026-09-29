using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1028 — the exact "never fires" bug MV-832 fixed for the Sentinel, reproduced for the Shoulder
    /// Rack: <c>NearestAwakeRobotInRange</c> ran a 12 m <c>Physics.OverlapSphereNonAlloc</c> against
    /// every layer into a 16-slot buffer, so a room cluttered with non-robot colliders (pillars, walls,
    /// deck slabs, dressing, Sentinels) could fill every slot before a single robot was ever returned —
    /// Lee's TestFlight report was "1 in 40 or so robots" fire despite awake robots standing right next
    /// to Max. The one new test the testing policy allows this ticket.
    ///
    /// Proven to fail at <c>ShoulderRack.cs</c>'s pre-fix state (main @ 7d0c704, unchanged through the
    /// MV-1025 rocket-speed retune): with 24 static BoxColliders inside the rack's 12 m range and one
    /// awake robot at 8 m, the old physics-overlap rule filled its 16-slot buffer with clutter before
    /// reaching the robot's own collider, so one reload window fired nothing:
    /// <c>Expected: 49 But was: 50 -- Guards MV-1028: one reload window must spend exactly one cell on
    /// the only awake robot in range</c> (wallet unchanged, <c>PlayerRocket.Active.Count</c> stayed 0).
    /// Passes clean against the fix below, which sources candidates from <see cref="RobotEnemy.Active"/>
    /// instead of the capped all-layers overlap.
    /// </summary>
    public sealed class MV1028ShoulderRackClutterTests
    {
        private const float Dt = 1f / 60f;

        // EditMode's single-tick test body never pumps the editor loop far enough for
        // AddComponent<RobotEnemy>()'s OnEnable to run synchronously (Awake does — same split
        // MV832SentinelTargetingTests documents), so NewEnemy must seed RobotEnemy.Active itself.
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
        public void FiresAtAnAwakeRobotEvenWhenClutterWouldHaveFilledTheOldPhysicsBuffer()
        {
            // Guards MV-1028
            WeaponSystemState.SecondaryKind = SecondaryKind.ShoulderRack;
            PickupWallet.SetPowerCellSecondary(50);

            var maxGo = new GameObject("Max");
            var rack = maxGo.AddComponent<ShoulderRack>();

            // 24 static BoxColliders 1-6 m out, same dense-clutter-inside-range shape
            // MV832SentinelTargetingTests' AC4 "never fires" probe uses, spawned BEFORE the robot so
            // the old buffer-filling order reproduces the bug.
            var clutter = new List<GameObject>(24);
            for (int i = 0; i < 24; i++)
            {
                var go = new GameObject($"MV1028-clutter-{i}");
                go.transform.position = new Vector3(1f + i * 0.22f, 0f, 0f); // spans ~1m to ~6.1m
                go.AddComponent<BoxCollider>();
                clutter.Add(go);
            }

            RobotEnemy rusher = NewEnemy(EnemyArchetype.Rusher, new Vector3(0f, 0f, 8f));

            try
            {
                RigState.RestoreSnapshot(new Dictionary<string, int> { { "s_rkt", 1 }, { "s_sal", 1 } },
                    new[] { "SECONDARY" });

                Physics.SyncTransforms();

                Advance(rack, 1.8f);

                Assert.That(PickupWallet.PowerCellsSecondary, Is.EqualTo(49),
                    "Guards MV-1028: one reload window must spend exactly one cell on the only awake robot in range");
                Assert.That(PlayerRocket.Active.Count, Is.EqualTo(1),
                    "Guards MV-1028: a rocket must actually fire, not just spend the cell silently");
                Assert.That(PlayerRocket.Active[0].TargetForTests, Is.EqualTo(rusher.transform),
                    "Guards MV-1028: the salvo must target the only awake robot in range, not miss it entirely");
            }
            finally
            {
                PlayerRocket.DestroyAllActive();
                foreach (GameObject go in clutter) Object.DestroyImmediate(go);
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
