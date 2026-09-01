using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-539's Bolter bolt: straight-line flight with no homing, a hit-radius despawn and a max-range
    /// despawn, and damage resolved live off the player's own max health rather than a hardcoded 10
    /// (AC1). Same "extract the per-frame logic as a pure static function" idiom
    /// <see cref="HomingMissile"/>'s own tests already use for this exact reason — <c>Update()</c>
    /// doesn't run on its own outside Play mode, and this project doesn't author PlayMode tests.
    /// </summary>
    public sealed class BolterBoltTests
    {
        [TearDown]
        public void TearDown() => DevTuning.Reset();

        // ---------------------------------------------------------------- AC4: straight line, no homing

        [Test]
        public void Step_FliesDeadStraight_NeverTurningTowardAnything()
        {
            Vector3 direction = new Vector3(1f, 0f, 2f).normalized;
            Vector3 pos = Vector3.zero;
            const float speed = 14f, dt = 0.1f;

            for (int i = 0; i < 12; i++)
                pos = BolterBolt.Step(pos, direction, speed, dt);

            // Unlike HomingMissile.TickFlying (which rotates transform.rotation toward the target every
            // step), Step() has no target parameter at all — a bolt fired along direction D is still
            // travelling exactly along D after 12 steps, not curving toward wherever the player has
            // since moved to.
            Vector3 expected = direction * (speed * dt * 12);
            Assert.AreEqual(expected.x, pos.x, 1e-4f, "the bolt drifted off its original heading");
            Assert.AreEqual(expected.y, pos.y, 1e-4f, "the bolt drifted off its original heading");
            Assert.AreEqual(expected.z, pos.z, 1e-4f, "the bolt drifted off its original heading");
        }

        // ---------------------------------------------------------------- AC4: despawns on first hit

        [TestCase(0.0f, true)]
        [TestCase(0.34f, true)]
        [TestCase(0.35f, true)]
        [TestCase(0.36f, false)]
        [TestCase(2f, false)]
        public void WithinHitRadius_TransitionsAtTheHitRadius(float distance, bool expectHit)
        {
            Vector3 bolt = Vector3.zero;
            Vector3 target = new Vector3(distance, 0f, 0f);

            Assert.AreEqual(expectHit, BolterBolt.WithinHitRadius(bolt, target, hitRadius: 0.35f),
                $"a bolt {distance} m from the player, against a 0.35 m hit radius, should read hit={expectHit}");
        }

        // ---------------------------------------------------------------- AC4: despawns past max range

        [TestCase(0f, false)]
        [TestCase(10.99f, false)]
        [TestCase(11f, true)]
        [TestCase(11.01f, true)]
        public void TraveledPastMaxRange_TransitionsAtMaxRangePlusThePaddingSlack(
            float traveled, bool expectDespawned)
        {
            // 9 m authored LungeRange + the 2 m padding = 11 m before an unfired bolt gives up.
            float maxDistance = EnemyArchetype.Bolter.LungeRange + BolterBolt.DespawnRangePadding;

            Assert.AreEqual(expectDespawned, BolterBolt.TraveledPastMaxRange(traveled, maxDistance),
                $"{traveled} m travelled against an {maxDistance} m ceiling should give despawned={expectDespawned}");
        }

        // ---------------------------------------------------------------- AC1: damage = 7% of the
        // ---------------------------------------------------------------- player's RESOLVED max health
        // (5% -> 10% per Lee's V12 workbook, 2026-09-01, MV-638; then 10% -> 7% per Lee's V12c
        // workbook, 2026-09-02, MV-642)

        [Test]
        public void DamageFor_ScalesWithThePlayersResolvedMaxHealth_NeverHardcodedToFourteen()
        {
            var go = new GameObject("MV-539 test Max", typeof(CharacterController));
            try
            {
                go.AddComponent<PlayerController>();
                var health = go.AddComponent<PlayerHealth>();

                // The authored default (200 max HP) DOES give 14 damage — but proving that alone can't
                // tell a resolved 7% apart from a hardcoded 14. Overriding the max via the same dev-panel
                // hook PlayerHealth.Max already reads through (DevTuning.PlayerMaxHealth) is what proves
                // this derives from the live value instead.
                DevTuning.PlayerMaxHealth = 340f;
                health.Initialize();   // MV-464: exposed publicly so an EditMode test can invoke it directly

                Assert.AreEqual(340f, health.Max, 1e-4f, "test setup: the dev-tuning override didn't take");

                float damage = BolterBolt.DamageFor(health.Max);

                Assert.AreEqual(23.8f, damage, 1e-4f,
                    "7% of a 340 max-health player should be 23.8, not a hardcoded 14 — the damage must " +
                    "derive from the player's resolved max health, not an authored constant");
                Assert.AreNotEqual(14f, damage, "this would only pass by coincidence if 14 were hardcoded");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ---------------------------------------------------------------- AC3 (MV-642): the new 7%
        // ---------------------------------------------------------------- fraction resolves correctly
        // against Max's authored 200 HP and a second receiver max health, proving the scale factor
        // itself moved, not just that DamageFor scales (the test above already covers that).

        [Test]
        public void DamageFor_ResolvesAtTheNewSevenPercentFraction_MV642()
        {
            Assert.AreEqual(14f, BolterBolt.DamageFor(200f), 1e-4f,
                "7% of 200 should be 14 — on base commit 5159d43 (10% fraction) this resolved to 20");
            Assert.AreEqual(21f, BolterBolt.DamageFor(300f), 1e-4f,
                "7% of 300 should be 21 — on base commit 5159d43 (10% fraction) this resolved to 30");
        }
    }
}
