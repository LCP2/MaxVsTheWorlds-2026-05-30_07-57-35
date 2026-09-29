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
    /// MV-1025 retuned the same constant again, 10 -> 12 m/s (Lee, TestFlight: "back up a little bit
    /// but not back to their original speed"), so this test's own resolved-distance band moves with it
    /// — re-measured at 5.20m (was 4.35m at 10 m/s); see <c>MV1025RocketFireEventSpeedTests</c> for the
    /// ticket's own new guard on the tuned value itself.
    ///
    /// The ticket's own AC quoted a straight-line 5.0m ("speed * 0.5s") expectation, which assumes a
    /// rocket flies dead-level at a constant heading. The real, resolved flight (MV-842's launch arc:
    /// pitched/yawed off the aim line for the first 0.25s, then homing) measures ~5.20m of net
    /// displacement in 0.5s at 12 m/s instead — this test asserts that measured value, not the ticket's
    /// idealised arithmetic, since a resolved-value assertion must match what the engine actually
    /// computes (see the testing policy's Tier 1/2 split).
    ///
    /// The 5.20m re-measurement is this suite's own real output against the MV-1025 fix, captured by
    /// running it with the OLD (pre-MV-1025) 4.35m +/- 0.15m band still in place: "expected ~4.35m of
    /// resolved net displacement in 0.5s at the tuned 10 m/s rocket speed ... measured 5.20m. Expected:
    /// 4.3499999f +/- 0.150000006f But was: 5.19784164f" — not a hand-derived estimate.
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
                Assert.That(distance, Is.EqualTo(5.20f).Within(0.15f),
                    $"expected ~5.20m of resolved net displacement in 0.5s at the MV-1025-tuned 12 m/s " +
                    $"rocket speed (see class doc for why this isn't the ticket's naive 6.0m), measured {distance:0.00}m");
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
