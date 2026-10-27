using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1294 — City pylons feed a relay or a boss: while any pylon naming that target's id is still
    /// alive, the target cannot be damaged (MV-1304/MV-1309, neither wired by this ticket). This proves
    /// the one piece those two tickets will consume: <see cref="Pylon.LiveFeedersOf"/> counts exactly the
    /// registered, currently-alive pylons feeding a given id, and drops as they are destroyed.
    ///
    /// Real entry points throughout: a hand-authored <see cref="WorldConfig"/> (same construction idiom
    /// as <c>MV828ReplicatorAreaIndexTests.BuildWorld</c>) carrying one relay (<see cref="Replicator"/>)
    /// and two pylons feeding it, loaded through the real <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/>
    /// pipeline — no direct <c>Pylon.Configure</c>/<c>SetFeedTarget</c> call anywhere in this file, so the
    /// schema-to-entity-to-component wiring (<see cref="WorldPylon.feeds"/> -&gt; <see cref="MapEntity.feeds"/>
    /// -&gt; <see cref="Pylon.Feeds"/>) is exercised for real, not assumed. One real hit
    /// (<see cref="Pylon.TakeDamage"/>, 120 damage against the ticket's own authored 120 HP) destroys one
    /// of the two.
    ///
    /// Must fail to COMPILE on the base commit (no <c>Pylon</c> type, no <c>WorldArea.pylons</c> field
    /// exists before this ticket) — the strongest form of Rule 1's "fails first" proof. See the fix
    /// comment for the captured failure output.
    ///
    /// ONE new test (MV-465 Rule 1). Tier 2 (Rule 2): every assertion reads a resolved runtime value —
    /// <see cref="Pylon.LiveFeedersOf"/>'s own live count and <see cref="Pylon.IsAlive"/> — never an
    /// authored constant, never a rendered pixel.
    /// // Guards MV-1294
    /// </summary>
    public sealed class MV1294PylonLiveFeedersTests
    {
        // Same "distinctive far-off origin" idiom MV828ReplicatorAreaIndexTests uses — EditMode tests
        // share one physics scene for the whole cc-verify run with no per-test reset, so near-origin
        // coordinates risk colliding with another test's own leftover fixtures.
        private const float OriginX = 93000f;
        private const float OriginZ = 108000f;

        private const string RelayId = "a1_relay";

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

        /// <summary>Stub (entry, empty) -> "a1" (one relay, two pylons both feeding it) — the minimal
        /// shape <see cref="WorldMapLoader.TryLoad"/>'s own entry/gate validation needs, same two-area
        /// skeleton <c>MV828ReplicatorAreaIndexTests.BuildWorld</c> uses.</summary>
        private static WorldConfig BuildWorld() => new WorldConfig
        {
            world = "MV-1294 Test World",
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
                        new WorldPylon { id = "a1_pylon2", x = OriginX + 4f, z = OriginZ + 6f, feeds = RelayId },
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
        public void LiveFeedersOf_DropsFromTwoToOne_AsOneFeedingPylonIsDestroyed()
        {
            WorldConfig cfg = BuildWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            _root = new GameObject("MV-1294 Root");
            MapBuild built = MapRuntime.Build(map, _root.transform);

            Assert.AreEqual(1, built.Replicators.Count, "setup failure: fixture must build exactly one relay");
            Assert.AreEqual(2, built.Pylons.Count, "setup failure: fixture must build exactly two pylons");

            // AddComponent's own Awake never runs outside Play mode (Replicator.Build/Pylon.Build's own
            // doc comments) — build every actor explicitly, same idiom MV1120ReplicatorHealthBarTests and
            // MV828ReplicatorAreaIndexTests already use.
            built.Replicators[0].Build();
            Pylon pylonA = built.Pylons[0];
            Pylon pylonB = built.Pylons[1];
            pylonA.Build();
            pylonB.Build();

            Assert.AreEqual(RelayId, pylonA.Feeds, "setup failure: pylon A's authored feeds id did not survive WorldPylon -> MapEntity -> Pylon.Configure");
            Assert.AreEqual(RelayId, pylonB.Feeds, "setup failure: pylon B's authored feeds id did not survive WorldPylon -> MapEntity -> Pylon.Configure");
            Assert.IsTrue(pylonA.IsAlive, "setup failure: a freshly built pylon must start alive");
            Assert.IsTrue(pylonB.IsAlive, "setup failure: a freshly built pylon must start alive");

            // === AC1: two live pylons feeding the relay are both counted ===
            Assert.AreEqual(2, Pylon.LiveFeedersOf(RelayId),
                "MV-1294 AC1: two live pylons feeding the same relay id must both be counted");

            // === AC1: 120 damage (the ticket's own authored HP) destroys one, dropping the count to 1 ===
            pylonA.TakeDamage(new DamageInfo(120f, pylonA.transform.position, Vector3.down, Team.Player));
            Assert.IsFalse(pylonA.IsAlive, "setup failure: 120 damage must destroy a 120 HP pylon");
            Assert.IsTrue(pylonB.IsAlive, "setup failure: pylon B must be unaffected by pylon A's own damage");

            Assert.AreEqual(1, Pylon.LiveFeedersOf(RelayId),
                "MV-1294 AC1: destroying one of two feeding pylons must drop the live-feeder count to 1");
        }
    }
}
