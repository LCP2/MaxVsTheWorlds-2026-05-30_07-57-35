using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1016: wires up MV-716's Splicer/hijack scaffolding to the real spawn+tick path.
    /// <see cref="MV716HijackingTests"/> drove <see cref="RobotEnemy.TryBeginSpliceChannel"/>/
    /// <see cref="RobotEnemy.TickSpliceChannel"/> directly — nothing in the game ever called them. This
    /// test instead: loads World 3's config through <see cref="WorldLibrary.Load"/> (the normal loader),
    /// spawns a gunner through the shared spawn path (<see cref="EnemyArchetype.For"/> + <see cref="RobotEnemy.Apply"/>,
    /// the same pair <c>EnemySpawner.Take</c> uses for every garrison/shed/replicator spawn), and ticks
    /// it through the real per-frame methods (<see cref="RobotEnemy.Tick"/>, MV-1015's own public entry
    /// point; <see cref="Sentinel.TickSentinel"/>, made public by this ticket for the same reason).
    ///
    /// Fails on 4436916 (the commit before this ticket): <see cref="EnemyArchetype"/> has no Splicer
    /// field, <see cref="WorldEnemyOverride"/> has no <c>splicer</c> field, and even a robot manually
    /// marked as a Splicer is never ticked toward a channel — nothing in <c>RobotEnemy.TickBody</c> calls
    /// <see cref="RobotEnemy.TryBeginSpliceChannel"/>, and nothing in <c>Sentinel.TickSentinel</c> reads
    /// <see cref="Sentinel.IsHijacked"/> — so <c>gunner.IsSplicer</c> is false and
    /// <c>sentinel.IsHijacked</c> never becomes true no matter how long this test ticks.
    /// </summary>
    public sealed class MV1016SplicerHijacksSentinelTests
    {
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);

        // Well clear of every other EditMode fixture's coordinates — NearestUnhijackedSentinelOnLevel/
        // NearestRobotInRange both read global Active lists, so a stray leftover elsewhere could
        // otherwise interfere (same reasoning MV914SentinelWorldBeamTests/MV1006 give for their own
        // dedicated origins).
        private static readonly Vector3 Origin = new Vector3(-71234f, 0f, 41234f);

        [SetUp]
        [TearDown]
        public void Clear()
        {
            Sentinel.FocusEnabled = false;
            Sentinel.ResetRegistry();
            RobotEnemy.ResetRegistry();
        }

        private static RobotEnemy SpawnGunner(WorldConfig worldConfig, Vector3 position)
        {
            var go = new GameObject("MV1016-Gunner");
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc); // Awake (which normally wires this) doesn't run outside Play mode
            e.Apply(EnemyArchetype.For(EnemyKind.Gunner, worldConfig));
            go.transform.position = position;
            return e;
        }

        [Test]
        public void World3GunnerSplicesAndHijacksASentinel_ThatTargetsMaxUntilItExpires_MV1016()
        {
            GameObject maxGo = null, sentinelGo = null, gunnerGo = null, world1GunnerGo = null;
            try
            {
                WorldConfig world3 = WorldLibrary.Load(WorldLibrary.World3);
                Assert.IsNotNull(world3, "world3_config.json failed to load");
                WorldConfig world1 = WorldLibrary.Load(WorldLibrary.World1);
                Assert.IsNotNull(world1, "world1_config.json failed to load");

                maxGo = new GameObject("MV-1016 test Max", typeof(CharacterController));
                var playerHealth = maxGo.AddComponent<PlayerHealth>();
                playerHealth.Initialize();
                maxGo.transform.position = Origin + new Vector3(1f, 0f, 0f);

                // Range 3 keeps the Sentinel from ever auto-firing at the 5m-away gunner below (which
                // would cancel its channel as friendly damage — MV-1016's own "damage cancels it" rule)
                // while still covering Max at 1m once hijacked.
                var sentinel = new GameObject("Sentinel").AddComponent<Sentinel>();
                sentinelGo = sentinel.gameObject;
                sentinel.Init(Origin, maxHp: 80f, range: 3f, fireInterval: 0.1f,
                    moveSpeed: 0f, standoffDistance: 2.5f, followTarget: maxGo.transform);

                RobotEnemy gunner = SpawnGunner(world3, Origin + new Vector3(5f, 0f, 0f));
                gunnerGo = gunner.gameObject;
                Assert.IsTrue(gunner.IsSplicer, "World 3's gunner override must stamp IsSplicer via the shared spawn path");

                RobotEnemy world1Gunner = SpawnGunner(world1, Origin + new Vector3(50f, 0f, 50f));
                world1GunnerGo = world1Gunner.gameObject;
                Assert.IsFalse(world1Gunner.IsSplicer, "a World 1 gunner spawned the same way must not be a Splicer");

                Physics.SyncTransforms();

                Assert.AreEqual(Team.Player, sentinel.Team, "test precondition: a fresh Sentinel starts on Max's team");

                // 2.6s of real per-frame ticks (AC1) — enough for the 2.5s channel to complete. Split
                // into exact chunks (0.1 to start the channel, then 1.5 + 1.0 = 2.5 exactly), not 26
                // additions of 0.1f — the same "avoids boundary flake" reasoning MV716HijackingTests'
                // own 12s-hijack assert already gives for its 11f + 1f split: 25 accumulated 0.1f steps
                // land a hair under 2.5f in IEEE float, which would leave the channel one tick short.
                gunner.Tick(0.1f);
                sentinel.TickSentinel(0.1f);
                gunner.Tick(1.5f);
                sentinel.TickSentinel(1.5f);
                gunner.Tick(1.0f); // channel elapsed exactly 2.5s here -> completes into BeginHijack
                sentinel.TickSentinel(1.0f);

                Assert.IsTrue(sentinel.IsHijacked, "the Sentinel must be hijacked 2.6s after a Splicer in range/LOS starts channeling");
                Assert.AreEqual(Team.Enemy, sentinel.Team, "a hijacked Sentinel must have flipped to the enemy team");
                Assert.AreEqual(maxGo.transform, sentinel.CurrentTargetTransform,
                    "a hijacked Sentinel's current target must be Max");

                // 11 more seconds -> the hijack expires 12s after it began (AC1). Only 11, not 12: the
                // completing sentinel.TickSentinel(1.0f) call above already ran its own TickHijack(1.0f)
                // as its first line (TickSentinel's own doc), consuming 1s of the 12s window in that
                // same tick. Same exact-chunk reasoning as above.
                sentinel.TickHijack(10f);
                Assert.IsTrue(sentinel.IsHijacked, "must still be hijacked before the 12s mark");
                sentinel.TickHijack(1f); // elapsed 12s exactly (1 + 10 + 1)
                Assert.IsFalse(sentinel.IsHijacked, "the hijack must expire 12s after it began");
                Assert.AreEqual(Team.Player, sentinel.Team, "an expired hijack must return the Sentinel to Max's team");
            }
            finally
            {
                // The hijacked Sentinel's own shot at Max spawns a SentinelBolt (SentinelBolt.Fire) —
                // a loose root GameObject, never parented to the Sentinel, so destroying sentinelGo
                // does not reach it. Left alive, it leaks into the next test's scene (as MV806SentinelBoltTests'
                // own teardown already has to account for). Same find-and-destroy idiom that test uses.
                var bolt = Object.FindAnyObjectByType<MaxWorlds.Arena.SentinelBolt>();
                if (bolt != null) Object.DestroyImmediate(bolt.gameObject);

                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (sentinelGo != null) Object.DestroyImmediate(sentinelGo);
                if (gunnerGo != null) Object.DestroyImmediate(gunnerGo);
                if (world1GunnerGo != null) Object.DestroyImmediate(world1GunnerGo);
            }
        }
    }
}
