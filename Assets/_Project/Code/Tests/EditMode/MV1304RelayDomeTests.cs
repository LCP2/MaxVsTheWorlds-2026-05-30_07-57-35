using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1304 AC1: a relay fed by one live pylon loses 0 health to a 50-damage player hit; after the
    /// pylon is destroyed the same hit removes the full 50.
    ///
    /// Fails on de1c112 (the commit before this ticket): <c>Replicator.TakeDamage</c> carries no
    /// <see cref="Pylon.LiveFeedersOf"/> check at all yet, so the in-dome hit deals its full,
    /// un-blocked 50 — the first assertion below fails (expected 0, actual 50). See the fix comment
    /// for the captured failure output.
    ///
    /// Real entry points throughout: a hand-authored <see cref="WorldConfig"/> carrying one relay and
    /// one pylon feeding it, loaded through the real <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/>
    /// pipeline — same construction idiom as <c>MV1294PylonLiveFeedersTests.BuildWorld</c>, which this
    /// ticket's own Pylon component already proved wires <see cref="WorldPylon.feeds"/> through to
    /// <see cref="Pylon.Feeds"/> correctly, so this file does not re-prove that link.
    ///
    /// ONE new test (MV-465 Rule 1). Tier 2 (Rule 2): every assertion reads a resolved runtime value —
    /// <see cref="Replicator.Normalized"/>'s own health delta — never an authored constant, never a
    /// rendered pixel.
    /// // Guards MV-1304
    /// </summary>
    public sealed class MV1304RelayDomeTests
    {
        // Same "distinctive far-off origin" idiom MV1294PylonLiveFeedersTests uses — EditMode tests
        // share one physics scene for the whole cc-verify run with no per-test reset.
        private const float OriginX = 97000f;
        private const float OriginZ = 112000f;

        private const string RelayId = "a1_relay";
        private const float TestDamage = 50f;

        private GameObject _root;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            EnemyNavigation.Reset();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            EnemyNavigation.Reset();
            DevTuning.Reset();
        }

        /// <summary>Stub (entry, empty) -> "a1" (one relay, one pylon feeding it) — same minimal
        /// two-area skeleton <c>MV1294PylonLiveFeedersTests.BuildWorld</c> uses.</summary>
        private static WorldConfig BuildWorld() => new WorldConfig
        {
            world = "MV-1304 Test World",
            dials = new WorldDials
            {
                areaCount = 1, baseThreat = 0f, threatGrowth = 0f,
                pacingRhythm = new[] { 1f }, powerupCadence = 99,
            },
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", index = 0, role = "entry",
                    origin = new WorldAreaOrigin { x = OriginX - 2f, z = OriginZ - 6f },
                    size = new WorldAreaSize { w = 4f, d = 6f },
                },
                new WorldArea
                {
                    id = "a1", index = 1, role = "normal",
                    origin = new WorldAreaOrigin { x = OriginX - 10f, z = OriginZ },
                    size = new WorldAreaSize { w = 20f, d = 20f },
                    replicators = new[]
                    {
                        new WorldReplicator { id = RelayId, x = OriginX, z = OriginZ + 10f, capacity = 0 },
                    },
                    pylons = new[]
                    {
                        new WorldPylon { id = "a1_pylon1", x = OriginX - 4f, z = OriginZ + 6f, feeds = RelayId },
                    },
                },
            },
            gates = new[]
            {
                new WorldGate
                {
                    id = "g0", width = 3f, opensWith = "start",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                },
            },
        };

        [Test]
        public void TakeDamage_ZeroWhileLivePylonFeeds_FullAfterPylonDestroyed_MV1304()
        {
            WorldConfig cfg = BuildWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            _root = new GameObject("MV-1304 Root");
            MapBuild built = MapRuntime.Build(map, _root.transform);

            Assert.AreEqual(1, built.Replicators.Count, "setup failure: fixture must build exactly one relay");
            Assert.AreEqual(1, built.Pylons.Count, "setup failure: fixture must build exactly one pylon");

            // AddComponent's own Awake never runs outside Play mode (Replicator.Build/Pylon.Build's own
            // doc comments) — build every actor explicitly, same idiom MV1294PylonLiveFeedersTests uses.
            // MapRuntime.BuildReplicator never stamps Replicator.Id itself (that's WorldRunner's own
            // post-build step, see Replicator.SetId's doc comment) — drive it directly here, same
            // "public so an EditMode test can drive identity directly" convention that doc comment names.
            Replicator relay = built.Replicators[0];
            relay.Build();
            relay.SetId(RelayId);
            Pylon pylon = built.Pylons[0];
            pylon.Build();

            Assert.AreEqual(RelayId, relay.Id, "setup failure: Replicator.SetId did not stick");
            Assert.AreEqual(RelayId, pylon.Feeds, "setup failure: the pylon's authored feeds id did not survive WorldPylon -> MapEntity -> Pylon.Configure");
            Assert.IsTrue(pylon.IsAlive, "setup failure: a freshly built pylon must start alive");
            Assert.AreEqual(1, Pylon.LiveFeedersOf(RelayId), "setup failure: exactly one live pylon must feed the relay before either assertion below");

            var hit = new DamageInfo(TestDamage, relay.transform.position, Vector3.down, Team.Player);

            // === AC1 part 1: the dome blocks a hit entirely while its one feeding pylon is alive ===
            float beforeDome = relay.HealthCurrent;
            relay.TakeDamage(hit);
            Assert.AreEqual(0f, beforeDome - relay.HealthCurrent, 1e-3f,
                "MV-1304 AC1: a relay fed by a live pylon must lose 0 health to a player hit");

            // === AC1 part 2: once the feeding pylon is destroyed, the same hit deals its full damage ===
            pylon.TakeDamage(new DamageInfo(120f, pylon.transform.position, Vector3.down, Team.Player));
            Assert.IsFalse(pylon.IsAlive, "setup failure: 120 damage must destroy a 120 HP pylon");
            Assert.AreEqual(0, Pylon.LiveFeedersOf(RelayId), "setup failure: destroying the only feeding pylon must drop the live-feeder count to 0");

            float beforeOpen = relay.HealthCurrent;
            relay.TakeDamage(hit);
            Assert.AreEqual(TestDamage, beforeOpen - relay.HealthCurrent, 1e-3f,
                "MV-1304 AC1: once the only feeding pylon dies the dome must drop at once — the same hit now deals full damage");
        }
    }
}
