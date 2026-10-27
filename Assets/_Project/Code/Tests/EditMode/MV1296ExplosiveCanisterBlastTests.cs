using System.Reflection;
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
    /// MV-1296 (ticket AC1): "build an area from config with two canisters 2.5 m apart and a Rusher 2 m
    /// from the first; deal 40 damage to the first; after 1.1 s simulated, the Rusher has taken 60, the
    /// second canister has started its fuse, and a Rusher 4 m away is untouched." Must fail to COMPILE on
    /// the pre-fix base commit: no <c>Canister</c> type, no <c>WorldArea.canisters</c> field and no
    /// <c>WorldCanister</c> type exist there — the strongest form of Rule 1's "fails first" proof. See the
    /// fix comment for the captured failure output.
    ///
    /// Real entry points throughout: a hand-authored <see cref="WorldConfig"/> (same construction idiom
    /// as <c>MV1294PylonLiveFeedersTests.BuildWorld</c>) carrying two canisters, loaded through the real
    /// <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/> pipeline, driving the real
    /// <see cref="Canister.TakeDamage"/>/<see cref="Canister.Tick"/> (and the blast sweep it fires) path —
    /// no direct blast-math call anywhere in this file.
    ///
    /// ONE new test (MV-465 Rule 1). Tier 2 (Rule 2): every assertion reads a resolved runtime value — a
    /// Rusher's own <see cref="RobotEnemy.HealthCurrent"/> and a canister's own <see cref="Canister.IsFusing"/>
    /// — never an authored constant, never a rendered pixel.
    /// // Guards MV-1296
    /// </summary>
    public sealed class MV1296ExplosiveCanisterBlastTests
    {
        // Same "distinctive far-off origin" idiom MV1294PylonLiveFeedersTests uses — EditMode tests share
        // one physics scene for the whole cc-verify run with no per-test reset, and this test's own
        // Physics.OverlapSphere-driven blast sweep (Canister.BlastSweep) makes a stray near-origin
        // collider left by another test a real correctness risk here, not just a cosmetic one.
        private const float OriginX = 97531f;
        private const float OriginZ = 113246f;

        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _root;
        private GameObject _rusherNearGo;
        private GameObject _rusherFarGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            // First VfxMaterials/health-bar init in this test domain logs a plain Debug.Log once, not a
            // failure — same idiom MV1240PlasmaVentDamageTests uses for the same reason.
            LogAssert.ignoreFailingMessages = true;
            DevTuning.Reset();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            Canister.ResetRegistry();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            if (_rusherNearGo != null) Object.DestroyImmediate(_rusherNearGo);
            if (_rusherFarGo != null) Object.DestroyImmediate(_rusherFarGo);
            if (_root != null) Object.DestroyImmediate(_root);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            Canister.ResetRegistry();
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
            DevTuning.Reset();
        }

        /// <summary>Stub (entry, empty) -> "a1" (two canisters 2.5 m apart) — same two-area skeleton
        /// <c>MV1294PylonLiveFeedersTests.BuildWorld</c> uses.</summary>
        private static WorldConfig BuildWorld() => new WorldConfig
        {
            world = "MV-1296 Test World",
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
                    canisters = new[]
                    {
                        new WorldCanister { id = "a1_canister1", x = OriginX, z = OriginZ + 10f },
                        new WorldCanister { id = "a1_canister2", x = OriginX + 2.5f, z = OriginZ + 10f },
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
        public void FortyDamageOnOneCanister_After1Point1SecondsSimulated_Blasts60DamageAt2mAndChainsTheOtherCanistersFuse()
        {
            WorldConfig cfg = BuildWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            _root = new GameObject("MV-1296 Root");
            MapBuild built = MapRuntime.Build(map, _root.transform);

            Assert.AreEqual(2, built.Canisters.Count, "setup failure: fixture must build exactly two canisters");

            // AddComponent's own Awake never runs outside Play mode (Canister.Build's own doc comment,
            // same as Pylon.Build/Replicator.Build) — build every actor explicitly, same idiom
            // MV1294PylonLiveFeedersTests already uses.
            Canister canisterA = built.Canisters[0];
            Canister canisterB = built.Canisters[1];
            canisterA.Build();
            canisterB.Build();

            Assert.IsTrue(canisterA.IsAlive, "setup failure: a freshly built canister must start alive");
            Assert.IsTrue(canisterB.IsAlive, "setup failure: a freshly built canister must start alive");

            Vector3 canisterAPos = canisterA.transform.position;

            // Same reflection idiom MV1240PlasmaVentDamageTests uses: RobotEnemy.OnEnable (not Awake)
            // is what actually seeds a fresh _health from Apply()'s own maxHealth, and never runs as a
            // side effect of AddComponent outside Play mode either.
            _rusherNearGo = new GameObject("RusherNear");
            var rusherNear = _rusherNearGo.AddComponent<RobotEnemy>();
            rusherNear.Apply(EnemyArchetype.Rusher);
            RobotOnEnableMethod.Invoke(rusherNear, null);
            _rusherNearGo.transform.position = canisterAPos + new Vector3(2f, 0f, 0f);

            _rusherFarGo = new GameObject("RusherFar");
            var rusherFar = _rusherFarGo.AddComponent<RobotEnemy>();
            rusherFar.Apply(EnemyArchetype.Rusher);
            RobotOnEnableMethod.Invoke(rusherFar, null);
            _rusherFarGo.transform.position = canisterAPos + new Vector3(4f, 0f, 0f);

            float rusherNearHealthBefore = rusherNear.HealthCurrent;
            float rusherFarHealthBefore = rusherFar.HealthCurrent;

            // === AC1: 40 damage (the ticket's own authored HP) starts canister A's fuse rather than
            // destroying it outright ("does not vanish ... sparks") ===
            canisterA.TakeDamage(new DamageInfo(40f, canisterAPos, Vector3.down, Team.Player));
            Assert.IsTrue(canisterA.IsFusing,
                "MV-1296 AC1: 40 damage on a 40 HP canister must start its fuse, not destroy it outright");

            // === AC1: 1.1 s simulated (11 explicit 0.1 s ticks) carries the fuse past its 1.0 s duration
            // and fires the blast ===
            const float step = 0.1f;
            const float simulatedSeconds = 1.1f;
            for (float t = 0f; t < simulatedSeconds; t += step) canisterA.Tick(step);

            Assert.AreEqual(60f, rusherNearHealthBefore - rusherNear.HealthCurrent, 0.001f,
                "MV-1296 AC1: a Rusher 2 m from the shot canister must take exactly 60 blast damage");
            Assert.AreEqual(0f, rusherFarHealthBefore - rusherFar.HealthCurrent, 0.001f,
                "MV-1296 AC1: a Rusher 4 m from the shot canister (outside the 3 m blast radius) must take no damage");
            Assert.IsTrue(canisterB.IsFusing,
                "MV-1296 AC1: the second canister, 2.5 m away (inside the 3 m blast radius), must have started its own fuse");
        }
    }
}
