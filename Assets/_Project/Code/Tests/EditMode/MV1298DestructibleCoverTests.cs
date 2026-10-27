using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1298 (ticket AC1): "build an area with a cover piece of hp 100 between a robot and Max; apply
    /// 100 damage; the piece's collider is disabled and a route query from the robot to Max that was
    /// blocked is now open." Must fail to COMPILE on the pre-fix base commit: no <c>DestructibleCover</c>
    /// type, no <c>WorldCover.hp</c> field and no <c>MapEntity.broken</c> field exist there — the
    /// strongest form of Rule 1's "fails first" proof. See the fix comment for the captured failure
    /// output.
    ///
    /// Real entry points throughout: a hand-authored <see cref="WorldConfig"/> (same construction idiom
    /// as <c>MV1296ExplosiveCanisterBlastTests.BuildWorld</c>) carrying one hp-100 cover piece, loaded
    /// through the real <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/> pipeline, driving the real
    /// <see cref="DestructibleCover.TakeDamage"/> path, then querying the real <see cref="ZoneRouteGrid"/>
    /// the same way a chasing robot does.
    ///
    /// ONE new test (MV-465 Rule 1). Tier 2 (Rule 2): every assertion reads a resolved runtime value —
    /// the built piece's own <see cref="Collider.enabled"/>, <see cref="DestructibleCover.IsAlive"/>, and
    /// <see cref="ZoneRouteGrid.NextStep"/>'s own resolved answer — never an authored constant, never a
    /// rendered pixel.
    /// // Guards MV-1298
    /// </summary>
    public sealed class MV1298DestructibleCoverTests
    {
        // Same "distinctive far-off origin" idiom MV1294PylonLiveFeedersTests/MV1296ExplosiveCanisterBlastTests
        // use — EditMode tests share one physics scene for the whole cc-verify run with no per-test
        // reset, so a stray near-origin collider left by another test is a real correctness risk here.
        private const float OriginX = 84172f;
        private const float OriginZ = 95318f;

        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            EnemyNavigation.Reset();
            DestructibleCover.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);

            DestructibleCover.ResetRegistry();
            EnemyNavigation.Reset();
        }

        /// <summary>Stub (entry, empty) -> "a1" (one hp-100 cover piece at the room's centre) — same
        /// two-area skeleton <c>MV1296ExplosiveCanisterBlastTests.BuildWorld</c> uses.</summary>
        private static WorldConfig BuildWorld() => new WorldConfig
        {
            world = "MV-1298 Test World",
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
                    cover = new[]
                    {
                        new WorldCover
                        {
                            id = "a1_cover1", x = OriginX, z = OriginZ + 10f,
                            width = 2f, height = 1.5f, depth = 1f, shape = "box", hp = 100f,
                        },
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
        public void HundredDamageOnHp100Cover_DisablesItsCollider_AndOpensTheRouteBetweenRobotAndMax()
        {
            WorldConfig cfg = BuildWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            _root = new GameObject("MV-1298 Root");
            MapBuild built = MapRuntime.Build(map, _root.transform);

            Assert.AreEqual(1, built.Cover.Count, "setup failure: fixture must build exactly one cover piece");

            GameObject coverBody = built.Cover[0].Body;
            DestructibleCover cover = coverBody.GetComponent<DestructibleCover>();
            Assert.IsNotNull(cover, "setup failure: an hp-100 cover piece must carry a DestructibleCover component");

            // AddComponent's own Awake never runs outside Play mode (DestructibleCover.Build's own doc
            // comment, same as Canister.Build/Pylon.Build) — build it explicitly, same idiom
            // MV1296ExplosiveCanisterBlastTests already uses.
            cover.Build();
            Assert.IsTrue(cover.IsAlive, "setup failure: a freshly built cover piece must start alive");

            Collider collider = coverBody.GetComponent<Collider>();
            Assert.IsNotNull(collider, "setup failure: the cover body must carry a real Collider");
            Assert.IsTrue(collider.enabled, "setup failure: a freshly built cover piece's collider must start enabled");

            MapZone zone = map.Zone("area1");
            Assert.IsNotNull(zone, "setup failure: the loader must rename area index 1 to 'area1'");

            var robotPos = new Vector2(OriginX - 5f, OriginZ + 10f);
            var maxPos = new Vector2(OriginX + 5f, OriginZ + 10f);

            // === Before: the intact hp-100 cover piece sits directly on the straight line between the
            // robot and Max, so a route query must detour around it, never answer the line itself. ===
            Vector2? blockedStep = ZoneRouteGrid.NextStep(map, zone, robotPos, maxPos);
            Assert.IsTrue(blockedStep.HasValue,
                "setup failure: the room must still have a way around the cover piece for this to prove " +
                "a detour rather than a sealed pocket");
            Assert.AreNotEqual(maxPos, blockedStep.Value,
                "MV-1298 AC1: an intact hp-100 cover piece sitting between the robot and Max must block " +
                "the straight-line route");

            // === AC1: 100 damage on a 100 HP cover piece breaks it ===
            cover.TakeDamage(new DamageInfo(100f, new Vector3(OriginX, 0f, OriginZ + 10f), Vector3.down, Team.Player));

            Assert.IsFalse(cover.IsAlive, "MV-1298 AC1: 100 damage on a 100 HP cover piece must break it");
            Assert.IsFalse(collider.enabled, "MV-1298 AC1: a broken cover piece's collider must be disabled");

            // === After: the same route query must now answer the straight line — the gap is open. ===
            Vector2? openStep = ZoneRouteGrid.NextStep(map, zone, robotPos, maxPos);
            Assert.AreEqual(maxPos, openStep,
                "MV-1298 AC1: breaking the cover piece must open the route — a query that was blocked " +
                "must now answer the straight line between the robot and Max");
        }
    }
}
