using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1082 (Lee, device, 2026-10-06): "What do the orange circles mean? If they're damage areas
    /// then they are not doing damage." <see cref="BossTuning.BladeLife"/> (0.8s) is shorter than
    /// <see cref="BossTuning.BladeArm"/> (0.85s), so <see cref="DamageZone.Update"/> destroyed every
    /// blade zone 0.05s BEFORE it could ever reach its own arm delay and bite — blades dealt zero
    /// damage since those two constants were last retuned.
    ///
    /// Fails on base commit d30d293: at that commit <c>DamageZone</c> has no public <c>Tick(float)</c>
    /// (CS1061 "'DamageZone' does not contain a definition for 'Tick'" — it was a private, Update-only
    /// early return), so this file does not compile before a single assertion runs. Spawning the real
    /// blade constants through <see cref="DamageZone.Spawn"/> and ticking the OLD <c>Update</c> body
    /// by hand to 0.8s total would have destroyed the zone with zero damage dealt, never reaching its
    /// own 0.85s arm delay — the exact defect reported.
    /// </summary>
    public sealed class MV1082BladeZoneArmLifeTests
    {
        // Distinctive far-off origin — EditMode tests share one physics scene for the whole cc-verify
        // run with no per-test reset (MV618/MV548/MV912 precedent).
        private static readonly Vector3 RigOrigin = new Vector3(-213406f, 0f, 157902f);

        [SetUp]
        [TearDown]
        public void Clear() => Sentinel.ResetRegistry();

        private static GameObject BuildMaxLike(Vector3 position)
        {
            var go = new GameObject("MV1082 Max", typeof(CharacterController), typeof(PlayerController))
            { tag = "Player" };
            go.transform.position = position;
            var health = go.AddComponent<PlayerHealth>();
            health.Initialize(); // Awake is not a reliable side effect of AddComponent outside Play mode
            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (GateSolidityTests precedent)
            return go;
        }

        private static Sentinel BuildSentinel(Vector3 position, GameObject go)
        {
            var sentinel = go.AddComponent<Sentinel>();
            sentinel.Init(position, maxHp: 100f, range: 0f, fireInterval: 999f,
                moveSpeed: 0f, standoffDistance: 0f, followTarget: null);
            Physics.SyncTransforms();
            return sentinel;
        }

        /// <summary>AC1: a real blade zone's full life cycle against both a Max-like receiver and a
        /// Sentinel standing at its centre, and a receiver 3m outside its radius.</summary>
        [Test]
        public void BladeZone_FirstTickLandsOnArm_TicksToWorstCase_AndNeverHitsOutsideReceiver()
        {
            Vector3 center = RigOrigin;
            Vector3 outsidePos = center + new Vector3(3f, 0f, 0f); // clear of BladeRadius (1.5m)

            GameObject insideGo = BuildMaxLike(center);
            var insideHealth = insideGo.GetComponent<PlayerHealth>();
            GameObject outsideGo = BuildMaxLike(outsidePos);
            var outsideHealth = outsideGo.GetComponent<PlayerHealth>();

            DamageZone zone = null;
            try
            {
                zone = DamageZone.Spawn(center, BossTuning.BladeRadius, BossTuning.BladeDamage,
                    BossTuning.BladeLife, BossTuning.BladeArm, Color.white);

                float insideStart = insideHealth.Current;
                float outsideStart = outsideHealth.Current;

                zone.Tick(0.84f); // just short of BladeArm (0.85s)
                Assert.AreEqual(insideStart, insideHealth.Current, 1e-3f,
                    "a blade zone must deal no damage before its own arm delay has elapsed");
                Assert.AreEqual(outsideStart, outsideHealth.Current, 1e-3f);
                Assert.IsNotNull(zone, "the zone must still be alive at 0.84s — not destroyed before it can ever arm");

                zone.Tick(0.02f); // age 0.86s — just past arm
                Assert.AreEqual(insideStart - BossTuning.BladeDamage, insideHealth.Current, 1e-3f,
                    "the first damage tick must land the instant the zone arms (0.85s), not armDelay+tickInterval later");
                Assert.AreEqual(outsideStart, outsideHealth.Current, 1e-3f,
                    "a receiver standing 3m outside the blade radius must never take blade damage");

                // Run out the rest of its active life (total lifetime = armDelay + life = 1.65s).
                zone.Tick(1f);
                float insideLost = insideStart - insideHealth.Current;
                Assert.AreEqual(BossTuning.BladeWorstCase, insideLost, 1e-3f,
                    $"a blade zone's full life must deal exactly its worst-case {BossTuning.BladeWorstCase} damage (3 ticks x {BossTuning.BladeDamage})");
                Assert.AreEqual(21f, insideLost, 1e-3f, "the ticket's own authored arithmetic: 3 ticks x 7 damage = 21");
                Assert.AreEqual(outsideStart, outsideHealth.Current, 1e-3f, "still untouched after the zone's full life");
                Assert.IsTrue(zone == null, "the zone must be destroyed once its full lifetime (armDelay + life) has elapsed");
            }
            finally
            {
                if (zone != null) Object.DestroyImmediate(zone.gameObject);
                Object.DestroyImmediate(insideGo);
                Object.DestroyImmediate(outsideGo);
            }

            // Same life cycle against a Sentinel standing at the zone's centre.
            GameObject sentinelGo = new GameObject("MV1082 Sentinel");
            DamageZone sentinelZone = null;
            try
            {
                Sentinel sentinel = BuildSentinel(center, sentinelGo);
                float sentinelStart = sentinel.HealthCurrent;

                sentinelZone = DamageZone.Spawn(center, BossTuning.BladeRadius, BossTuning.BladeDamage,
                    BossTuning.BladeLife, BossTuning.BladeArm, Color.white);

                sentinelZone.Tick(0.84f);
                Assert.AreEqual(sentinelStart, sentinel.HealthCurrent, 1e-3f,
                    "a Sentinel standing at the zone's centre must take no damage before it arms either");

                sentinelZone.Tick(0.02f);
                Assert.AreEqual(sentinelStart - BossTuning.BladeDamage, sentinel.HealthCurrent, 1e-3f,
                    "a Sentinel at the zone's centre must take the first tick the instant it arms");

                sentinelZone.Tick(1f);
                Assert.AreEqual(sentinelStart - 21f, sentinel.HealthCurrent, 1e-3f,
                    "a Sentinel standing in a blade zone for its whole life must take the same 21 total as Max");
                Assert.IsTrue(sentinelZone == null, "the zone must despawn after a Sentinel receiver's full life cycle too");
            }
            finally
            {
                if (sentinelZone != null) Object.DestroyImmediate(sentinelZone.gameObject);
                Object.DestroyImmediate(sentinelGo);
            }
        }

        /// <summary>AC2: the generic guard — no arm/life pair can ever produce a zone that cannot
        /// hurt. Deliberately NOT the blade/grass constants: arm 1.0s, life 0.1s, the exact shape of
        /// pair that used to die before arming.</summary>
        [Test]
        public void AnyArmLifePair_StillDeliversAtLeastOneTick()
        {
            Vector3 center = RigOrigin + new Vector3(0f, 0f, 500f); // clear of the other test's rig
            GameObject receiverGo = BuildMaxLike(center);
            var health = receiverGo.GetComponent<PlayerHealth>();
            DamageZone zone = null;
            try
            {
                const float damage = 9f;
                zone = DamageZone.Spawn(center, 1.5f, damage, life: 0.1f, armDelay: 1.0f, Color.white);

                float start = health.Current;
                zone.Tick(1.0f); // exactly the arm delay
                Assert.AreEqual(start - damage, health.Current, 1e-3f,
                    "an arm 1.0s / life 0.1s zone must still deal its one tick the instant it arms, " +
                    "not die first — this is the exact shape of pair (life < arm) that used to be silent");

                zone.Tick(0.2f); // past the end of its 0.1s active life
                Assert.AreEqual(start - damage, health.Current, 1e-3f,
                    "a zone with only one tick scheduled must never double-tick after arming");
                Assert.IsTrue(zone == null, "the zone must despawn once its short active life ends");
            }
            finally
            {
                if (zone != null) Object.DestroyImmediate(zone.gameObject);
                Object.DestroyImmediate(receiverGo);
            }
        }
    }
}
