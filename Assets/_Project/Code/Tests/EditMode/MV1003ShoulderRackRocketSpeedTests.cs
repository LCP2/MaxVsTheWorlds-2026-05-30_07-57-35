using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1003 — Lee's tuning call: the Shoulder Rack's rockets read as too fast, so
    /// <see cref="ShoulderRack"/>'s private RocketSpeed constant drops 14 -> 10 m/s (about 30% slower).
    /// Fires through the rack's own real call site (<see cref="ShoulderRack.Tick"/> ->
    /// <see cref="PlayerRocket.Fire"/>), not a hand-picked speed, so the resolved flight distance this
    /// test measures is driven by whatever that private constant is actually set to.
    ///
    /// The ticket's own AC quoted a straight-line 5.0m ("speed * 0.5s") expectation, which assumes a
    /// rocket flies dead-level at a constant heading. The real, resolved flight (MV-842's launch arc:
    /// pitched/yawed off the aim line for the first 0.25s, then homing) measures ~4.35m of net
    /// displacement in 0.5s at 10 m/s instead — this test asserts that measured value, not the ticket's
    /// idealised arithmetic, since a resolved-value assertion must match what the engine actually
    /// computes (see the testing policy's Tier 1/2 split).
    ///
    /// Proven to fail on 8c86feb (unchanged on ShoulderRack.cs/PlayerRocket.cs since; `git diff 8c86feb
    /// HEAD --stat` on both files is empty): at the pre-fix 14 m/s, the same 0.5s window resolves to
    /// ~6.04m of displacement, well outside the post-fix 4.35m +/- 0.15m band this test asserts.
    /// </summary>
    public sealed class MV1003ShoulderRackRocketSpeedTests
    {
        private const float Dt = 1f / 60f;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
            // Same World 2 selection MV694ShoulderRackTests uses — s_rkt only lives on World 2's board.
            RigBoard.UseWorld(1);
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
            RigBoard.ResetForTests();
        }

        [Test]
        public void FiredRocket_CoversTheTunedSpeedsResolvedDistanceInHalfASecond_MV1003()
        {
            WeaponSystemState.SecondaryKind = SecondaryKind.ShoulderRack;
            PickupWallet.SetPowerCellSecondary(1);

            var maxGo = new GameObject("Max");
            var rack = maxGo.AddComponent<ShoulderRack>();
            RobotEnemy target = NewEnemy(EnemyArchetype.Rusher, new Vector3(0f, 0f, 12f));

            try
            {
                Physics.SyncTransforms();

                // s_rkt L1 only (s_sal stays at baseline -> a 1-rocket salvo with no launch stagger),
                // the same RestoreSnapshot shortcut MV694ShoulderRackTests uses.
                RigState.RestoreSnapshot(new Dictionary<string, int> { { "s_rkt", 1 } }, new[] { "SECONDARY" });

                // One full reload window fires the single-rocket salvo on this exact Tick (delay-0
                // first rocket, see ShoulderRack.Tick's own comment).
                Advance(rack, AbilityTuning.DefaultShoulderRackBaseReloadSeconds);

                Assert.That(PlayerRocket.Active.Count, Is.EqualTo(1),
                    "one reload window against an in-range target must fire exactly one rocket");

                PlayerRocket rocket = PlayerRocket.Active[0];
                Vector3 origin = rocket.transform.position;

                for (int i = 0; i < 30; i++) rocket.Tick(Dt);   // 30 * 1/60f = 0.5s simulated flight

                Assert.That(rocket, Is.Not.Null, "the rocket must still be in flight 0.5s after launch");

                float distance = Vector3.Distance(origin, rocket.transform.position);
                Assert.That(distance, Is.EqualTo(4.35f).Within(0.15f),
                    $"expected ~4.35m of resolved net displacement in 0.5s at the tuned 10 m/s rocket " +
                    $"speed (see class doc for why this isn't the ticket's naive 5.0m), measured {distance:0.00}m");
            }
            finally
            {
                PlayerRocket.DestroyAllActive();
                Object.DestroyImmediate(target.gameObject);
                Object.DestroyImmediate(maxGo);
            }
        }

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
