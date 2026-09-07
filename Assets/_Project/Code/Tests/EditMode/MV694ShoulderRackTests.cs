using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-694 — the Shoulder Rack, World 2's auto-firing secondary. The one new test the testing policy
    /// allows this ticket: AC1 (fire-rate/targeting/cell-spend timeline) and AC2 (mesh shown only once
    /// bought) share the same setup, so both land in one method rather than two.
    ///
    /// Proven to fail on MV-708's merge commit (6ea4315): none of <see cref="ShoulderRack"/>,
    /// <see cref="PlayerRocket"/>, <see cref="SecondaryKind"/> or <see cref="ShoulderRackTrackKind"/>
    /// existed yet, and <c>rig_board.json</c> had no <c>s_rkt</c>/<c>s_sal</c> nodes — this test fails to
    /// compile against that commit (CS0246 "The type or namespace name 'ShoulderRack' could not be
    /// found"), the same class of pre-fix evidence <c>SentinelSystemTests.AttackModeChangesTheFollowPointAndPrioritisesTheForwardCone_MV636</c>
    /// documents.
    /// </summary>
    public sealed class MV694ShoulderRackTests
    {
        private const float Dt = 1f / 60f;

        [SetUp]
        [TearDown]
        public void Clear()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
        }

        [Test]
        public void AutoFiresAtTheNearestAwakeRobotInRangeAndTracksCellsAndTheMeshTogether_MV694()
        {
            WeaponSystemState.SecondaryKind = SecondaryKind.ShoulderRack;
            PickupWallet.SetPowerCellSecondary(2);

            var maxGo = new GameObject("Max");
            var rack = maxGo.AddComponent<ShoulderRack>();
            RobotEnemy rusher = NewEnemy(EnemyArchetype.Rusher, new Vector3(6f, 0f, 0f));
            RobotEnemy heavy = NewEnemy(EnemyArchetype.Heavy, new Vector3(14f, 0f, 0f));

            try
            {
                Physics.SyncTransforms();

                // AC2 (part 1): s_rkt starts at 0 (RigState.Reset()'s baseline) — the mesh (now built on
                // MaxRig, MV-702) must stay hidden. IsBought is the resolved value MaxRig itself reads
                // to decide that (see MaxRig.TickShoulderRackMount).
                Assert.That(rack.IsBought, Is.False, "the rack mesh must not be shown before s_rkt is owned");

                // s_rkt L1, s_sal L1 (RestoreSnapshot bypasses the draft/reach gate for test setup, the
                // same shortcut MV681WaterBalloonAutoFireToggleTests/RigStateTests use).
                RigState.RestoreSnapshot(new Dictionary<string, int> { { "s_rkt", 1 }, { "s_sal", 1 } },
                    new[] { "SECONDARY" });

                Assert.That(rack.IsBought, Is.True, "AC2: the rack mesh must be shown once s_rkt reaches L1");

                Advance(rack, 1.8f);

                Assert.That(PlayerRocket.Active.Count, Is.EqualTo(1),
                    "advancing 1.8s (one ReloadSeconds window) must fire exactly one rocket");
                Assert.That(PlayerRocket.Active[0].TargetForTests, Is.EqualTo(rusher.transform),
                    "the salvo must target the nearer, in-range Rusher — the Heavy sits 14m out, past the 12m range");
                Assert.That(PickupWallet.PowerCellsSecondary, Is.EqualTo(1), "a salvo must spend exactly one cell");

                Advance(rack, 3.6f);
                Assert.That(PickupWallet.PowerCellsSecondary, Is.EqualTo(0),
                    "two more 1.8s reload windows fit in 3.6s — the second one spends the last cell");
                Assert.That(PlayerRocket.Active.Count, Is.EqualTo(2), "2 rockets fired total after 3.6s more");

                Advance(rack, 1.8f);
                Assert.That(PlayerRocket.Active.Count, Is.EqualTo(2),
                    "a further 1.8s with an empty bank must fire nothing — the rack stays silent, not stuck retrying");
            }
            finally
            {
                PlayerRocket.DestroyAllActive();
                Object.DestroyImmediate(rusher.gameObject);
                Object.DestroyImmediate(heavy.gameObject);
                Object.DestroyImmediate(maxGo);
            }
        }

        /// <summary>Ticks in fixed <see cref="Dt"/> steps, one step PAST the exact target — 108 steps
        /// of 1/60f accumulate float error that can land the reload countdown a hair above zero on the
        /// exact boundary (observed: 0 rockets fired instead of 1). A fired salvo resets the countdown
        /// to a fresh, fixed value regardless of any overshoot, so the extra step never bleeds into a
        /// later call's own timing.</summary>
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
            return e;
        }
    }
}
