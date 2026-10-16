using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1208 — Lee, 17 Oct: City relays must not behave like World 2's replicators (walk into the
    /// hatch, come out doubled); robots are beamed down from the ship instead. <see cref="Replicator.ApplyCityRelaySkin"/>
    /// now also flips <c>TickLure</c>/<c>TickConsumption</c>'s World 2 body permanently off and routes
    /// the box through the new <see cref="Replicator.TickCityBeamIn"/> beat.
    ///
    /// Fails on the pre-MV-1208 commit: neither <c>TickCityBeamIn</c> nor <c>CityBeamedCount</c> exist
    /// on <c>Replicator</c> at all, so this file does not even compile against that commit — the same
    /// "fails on base" proof <c>MV1201CityRelaySkinTests</c>'s own doc comment already uses for a
    /// brand-new member on this exact class.
    ///
    /// Tier 2 (resolved values): every assertion reads a resolved spawn count, a resolved world
    /// position, or a resolved queue count — never an authored constant, never a rendered pixel.
    /// </summary>
    public sealed class MV1208CityRelayBeamInTests
    {
        // Same "distinctive far-off origin" idiom every other Replicator EditMode test uses — EditMode
        // tests share one physics scene for the whole cc-verify run with no per-test reset.
        private static readonly Vector3 RigOrigin = new Vector3(84112f, 0f, -29657f);
        private static readonly Vector3 FarRigOrigin = new Vector3(84112f, 0f, -29957f); // well clear of the first rig

        private GameObject _playerGo;
        private GameObject _relayGo;
        private GameObject _farRelayGo;

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
            DevTuning.GlobalRobotBudget = 40f; // plenty of headroom so room checks never block this test
            _playerGo = new GameObject("Player") { tag = "Player" };
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_relayGo != null) Object.DestroyImmediate(_relayGo);
            if (_farRelayGo != null) Object.DestroyImmediate(_farRelayGo);
        }

        private static Replicator NewCityRelay(out GameObject go, Vector3 position)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TeleportRelay";
            go.transform.position = position;
            go.transform.localScale = new Vector3(2f, 1.5f, 2f); // the authored 2x1.5x2 Replicator footprint
            var replicator = go.AddComponent<Replicator>();
            replicator.Build();
            replicator.Configure(0); // City relays never spend a doubling budget — irrelevant either way
            replicator.ApplyCityRelaySkin();
            replicator.GetComponent<EnemySpawner>().ConfigureAreaComposition(new WorldComposition { rusher = 5 });
            return replicator;
        }

        [Test]
        public void CityRelay_BeamsInFromTheSky_NeverLures_StopsOnDestructionAndOutOfRange()
        {
            LogAssert.ignoreFailingMessages = true; // same BuildBody collider-strip [Error] every Replicator test carries

            Replicator relay = NewCityRelay(out _relayGo, RigOrigin);
            var spawner = _relayGo.GetComponent<EnemySpawner>();
            _playerGo.transform.position = RigOrigin + new Vector3(10f, 0f, 0f); // 10 m away, within CityBeamInRadius

            const float dt = 1f / 50f;
            const int tenSecondsOfFrames = 500;
            for (int i = 0; i < tenSecondsOfFrames; i++)
                relay.TickCityBeamIn(dt, _playerGo.transform.position);

            int beamedIn = spawner.LiveCountOf(EnemyKind.Rusher);
            Assert.That(beamedIn, Is.InRange(3, 5),
                $"10 s at a 2.5 s beam-in interval must land ~4 robots (4±1); got {beamedIn}");

            Vector3 outFoot = relay.OutRampFootPosition;
            foreach (RobotEnemy r in RobotEnemy.Active)
            {
                if (r.Kind != EnemyKind.Rusher || !r.IsAlive) continue;
                Assert.LessOrEqual(Vector3.Distance(r.transform.position, outFoot), 1.2f,
                    "every beamed-in robot must be placed at the relay's OUT pad, same as a World 2 twin");
            }

            Assert.AreEqual(0, relay.QueueCount, "a City relay must never lure or queue a robot");

            // Destroy the relay — any further ticking must add nothing.
            relay.TakeDamage(new DamageInfo(1_000_000f, relay.transform.position, Vector3.forward, Team.Player));
            Assert.IsFalse(relay.IsAlive, "the relay must actually be dead for the next assertion to mean anything");

            int countAtDeath = spawner.LiveCountOf(EnemyKind.Rusher);
            const int fiveSecondsOfFrames = 250;
            for (int i = 0; i < fiveSecondsOfFrames; i++)
                relay.TickCityBeamIn(dt, _playerGo.transform.position);
            Assert.AreEqual(countAtDeath, spawner.LiveCountOf(EnemyKind.Rusher),
                "a destroyed relay must never add another beamed-in robot");

            // A fresh relay with Max 30 m away (outside CityBeamInRadius) must beam in nothing at all.
            Replicator farRelay = NewCityRelay(out _farRelayGo, FarRigOrigin);
            var farSpawner = _farRelayGo.GetComponent<EnemySpawner>();
            _playerGo.transform.position = FarRigOrigin + new Vector3(30f, 0f, 0f);
            for (int i = 0; i < tenSecondsOfFrames; i++)
                farRelay.TickCityBeamIn(dt, _playerGo.transform.position);
            Assert.AreEqual(0, farSpawner.LiveCountOf(EnemyKind.Rusher),
                "Max standing 30 m away (outside the 25 m beam-in radius) must trigger no beam-ins at all");
            Assert.AreEqual(0, farRelay.CityBeamedCount, "no in-flight descent either, with Max out of range");
        }
    }
}
