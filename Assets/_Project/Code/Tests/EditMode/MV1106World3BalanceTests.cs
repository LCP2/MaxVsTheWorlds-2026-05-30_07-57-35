using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1106 (Lee, device, 2026-10-06): "Rockets are too powerful and the primary weapon much too
    /// weak... These Dredge Hulk robots need too much damage to destroy and don't drop anything." One
    /// test (MV-465 Rule 1) carrying every AC as a sub-check, each through real production entry points
    /// — <see cref="WeaponSystemState.ApplyWorldLoadout"/> for per-world loadout, <see cref="Undertow.Tick"/>/
    /// the private <c>WaterBlaster.Update</c>/<see cref="ShoulderRack.Tick"/> (reflected the same way
    /// every other Undertow/ShoulderRack EditMode test in this suite already does — neither carries a
    /// public explicit-dt entry point — never a hand-set private field), <see cref="EnemySpawner.SpawnExact"/>
    /// for a damageable dummy, <see cref="WorldMapLoader.TryLoad"/> + <see cref="AreaAccumulationDirector.EnterArea"/>
    /// for the real garrison path, and <see cref="RobotEnemy.TakeDamage"/> (public) for the kill itself.
    ///
    /// Fails on base commit d30d293: UNDERTOW still deals 4/tick (borrowed from <see cref="WaterBlaster.DefaultDamagePerTick"/>),
    /// the Shoulder Rack deals a flat 30 in every world, world3_config.json's Brute override carries no
    /// <c>maxHealth</c> (so a Dredge Hulk still reads the base archetype's 420), and nothing guarantees a
    /// Dredge Hulk kill drops parts.
    /// </summary>
    public sealed class MV1106World3BalanceTests
    {
        private const float RocketDt = 0.05f;
        private const float ShoulderRackDt = 1f / 60f;

        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo UndertowAwake = typeof(Undertow).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowTick = typeof(Undertow).GetMethod("Tick", NonPublicInstance);
        private static readonly MethodInfo WaterBlasterAwake = typeof(WaterBlaster).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo WaterBlasterUpdate = typeof(WaterBlaster).GetMethod("Update", NonPublicInstance);
        private static readonly MethodInfo RobotEnemyOnEnable = typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            ResetAll();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            GameObject bodies = GameObject.Find("Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            // AreaAccumulationDirector.Bodies() lazily creates its own "Area Robots" container (separate
            // from EnemySpawner's "Robots" above) the first time it places anyone -- same leftover
            // MV514GarrisonHeadStartTests already guards against for the same reason: an empty one left
            // behind here was confusing a later test's own scene-wide lookups.
            GameObject areaRobots = GameObject.Find("Area Robots");
            if (areaRobots != null) Object.DestroyImmediate(areaRobots);
            ResetAll();
        }

        private static void ResetAll()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigBoard.ResetForTests();
            DevMode.Reset();
            DevTuning.Reset();
            DifficultyDirector.Reset();
            // EnemyNavigation.Map lazily caches the first BackyardPath it finds (or the lack of one) and
            // never re-looks until told to -- without this, the garrison test's own synthetic map leaks
            // its cached MapData (or a cached null) into every test that runs after this one and happens
            // to read EnemyNavigation.Map first.
            EnemyNavigation.Reset();
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            foreach (var a in Object.FindObjectsByType<AreaAccumulationDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(a.gameObject);
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            foreach (var s in Object.FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None))
                Object.DestroyImmediate(s.gameObject);
        }

        [Test]
        public void World3Balance_UndertowRocketsDredgeHulkHealthAndDrops()
        {
            // AC1(a)/(b): UNDERTOW is twice the RCDA's tick damage, in World 3 only.
            AssertPrimaryTickDamage(worldIndex: 0, expectedDamage: WaterBlaster.DefaultDamagePerTick, useUndertow: false);
            AssertPrimaryTickDamage(worldIndex: 2, expectedDamage: Undertow.DefaultDamagePerTick, useUndertow: true);

            // AC1(a)/(b): the Shoulder Rack's rocket is weaker in World 3 only — World 2 untouched.
            AssertRocketDamage(worldIndex: 1, expectedDamage: AbilityTuning.DefaultShoulderRackBaseDamage);
            AssertRocketDamage(worldIndex: 2, expectedDamage: AbilityTuning.DefaultShoulderRackBaseDamageWorld3);

            // AC3/AC4: a Dredge Hulk spawned through the real World 3 garrison path.
            AssertDredgeHulkHealthAndGuaranteedDrops();
        }

        // ---------------------------------------------------------------- (a)/(b) primary weapon tick

        private static void AssertPrimaryTickDamage(int worldIndex, float expectedDamage, bool useUndertow)
        {
            WeaponSystemState.ApplyWorldLoadout(worldIndex);

            RobotEnemy dummy = SpawnDummy(Vector3.forward * 5f);
            GameObject weaponGo = null;
            try
            {
                DevMode.Enabled = true;
                DevMode.AutoFire = true;

                float before = dummy.HealthCurrent;

                if (useUndertow)
                {
                    weaponGo = new GameObject("MV1106 Undertow");
                    var undertow = weaponGo.AddComponent<Undertow>();
                    UndertowAwake.Invoke(undertow, null);
                    // One big explicit tick: the lance's seeking tip needs real elapsed time to close on
                    // the target before it can latch and deal its one tick of damage — same shape
                    // MV1036UndertowGatedOutsideWorld3Tests already uses for exactly this reason.
                    UndertowTick.Invoke(undertow, new object[] { 1.2f });
                    Assert.IsTrue(undertow.IsEmitting, $"Undertow must emit for world index {worldIndex}");
                }
                else
                {
                    weaponGo = new GameObject("MV1106 WaterBlaster");
                    var blaster = weaponGo.AddComponent<WaterBlaster>();
                    WaterBlasterAwake.Invoke(blaster, null);
                    WaterBlasterUpdate.Invoke(blaster, null);
                    Assert.IsTrue(blaster.IsEmitting, $"the RCDA must emit for world index {worldIndex}");
                }

                Assert.AreEqual(before - expectedDamage, dummy.HealthCurrent, 0.01f,
                    $"world index {worldIndex}'s primary must remove exactly {expectedDamage} on one tick");
            }
            finally
            {
                if (weaponGo != null) Object.DestroyImmediate(weaponGo);
                DestroyDummy(dummy);
                WeaponSystemState.Reset();
                DevMode.Reset();
            }
        }

        // ---------------------------------------------------------------- (a)/(b) Shoulder Rack rocket

        private static void AssertRocketDamage(int worldIndex, float expectedDamage)
        {
            WeaponSystemState.ApplyWorldLoadout(worldIndex); // sets SecondaryKind=ShoulderRack, RigBoard to this world's board
            RigState.RestoreSnapshot(new Dictionary<string, int> { { "s_rkt", 1 }, { "s_sal", 1 } },
                new[] { "SECONDARY" });
            PickupWallet.SetPowerCellSecondary(1);

            var rackGo = new GameObject("MV1106 ShoulderRack");
            var rack = rackGo.AddComponent<ShoulderRack>();
            RobotEnemy target = SpawnDummy(new Vector3(5f, 0f, 0f));
            try
            {
                Physics.SyncTransforms();
                float before = target.HealthCurrent;

                Advance(rack, 1.8f);
                Assert.AreEqual(1, PlayerRocket.Active.Count,
                    $"world index {worldIndex}: exactly one rocket must fire this salvo");

                float elapsed = 0f;
                const float flightBudget = 4.5f;
                while (PlayerRocket.Active.Count > 0 && elapsed < flightBudget)
                {
                    var inFlight = new List<PlayerRocket>(PlayerRocket.Active);
                    foreach (PlayerRocket r in inFlight) r.Tick(RocketDt);
                    elapsed += RocketDt;
                }

                Assert.AreEqual(0, PlayerRocket.Active.Count,
                    $"world index {worldIndex}: the rocket must detonate within its fuel budget");
                Assert.AreEqual(before - expectedDamage, target.HealthCurrent, 0.05f,
                    $"world index {worldIndex}: the rocket must deal exactly {expectedDamage} damage");
            }
            finally
            {
                PlayerRocket.DestroyAllActive();
                Object.DestroyImmediate(rackGo);
                DestroyDummy(target);
                RigState.Reset();
                PickupWallet.Reset();
                WeaponSystemState.Reset();
            }
        }

        private static void Advance(ShoulderRack rack, float seconds)
        {
            int steps = Mathf.CeilToInt(seconds / ShoulderRackDt) + 1;
            for (int i = 0; i < steps; i++) rack.Tick(ShoulderRackDt);
        }

        // ---------------------------------------------------------------- (c)/(d) Dredge Hulk garrison path

        private static void AssertDredgeHulkHealthAndGuaranteedDrops()
        {
            WorldConfig cfg = TwoAreaWorld3();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            MapZone area2 = map.Zone("area2");
            Assert.IsNotNull(area2);

            var pickupGo = new GameObject("MV1106 PickupDirector");
            var pickupDirector = pickupGo.AddComponent<PickupDirector>();
            // Same EditMode-only OnEnable gap SpawnDummy works around — without this, PickupDirector
            // never subscribes to DropSignals.RobotDied and every kill below reaches no one.
            typeof(PickupDirector).GetMethod("OnEnable", NonPublicInstance).Invoke(pickupDirector, null);

            var directorGo = new GameObject("MV1106 Area Accumulation");
            var director = directorGo.AddComponent<AreaAccumulationDirector>();
            try
            {
                director.ConfigureWorld(cfg, worldIndex: 2); // World 3, 0-based
                director.Configure(map, System.Array.Empty<CoverPiece>()); // FillArea(1) pre-places area 2's garrison
                director.EnterArea(2); // activates/retoughens area 2's garrison (still Dormant — MV-656)

                var dredgeHulks = new List<RobotEnemy>();
                foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
                {
                    Vector3 p = r.transform.position;
                    if (r.Kind == EnemyKind.Brute && area2.Contains(p.x, p.z)) dredgeHulks.Add(r);
                }

                Assert.AreEqual(5, dredgeHulks.Count,
                    "test setup: area 2's authored garrison must place exactly 5 Dredge Hulks");

                // AC3: at Invasion level 0 (DifficultyDirector.Reset() in SetUp -> ToughnessMultiplier == 1),
                // resolved health is the overridden 300 x the live/settings "Robot health" multiplier (1.26).
                float expectedHealth = 300f * EnemySpawner.DefaultRobotHealthMultiplier;
                foreach (RobotEnemy hulk in dredgeHulks)
                {
                    Assert.AreEqual(expectedHealth, hulk.HealthCurrent, 0.5f,
                        "a Dredge Hulk's resolved health must be 300 (world3_config.json's brute override) " +
                        "x 1.26 (the Robot-health multiplier), at Invasion level 0 (toughness x1)");
                }

                // AC4: killing five garrison-placed Dredge Hulks through the real TakeDamage entry point,
                // from a player-team source, must leave at least 10 part pickups live in the world.
                foreach (RobotEnemy hulk in dredgeHulks)
                {
                    hulk.TakeDamage(new DamageInfo(expectedHealth + 50f, hulk.transform.position,
                        Vector3.forward, Team.Player));
                }

                int liveParts = 0;
                foreach (Pickup p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (p.Kind == PickupKind.PowerCell) liveParts++;

                Assert.That(liveParts, Is.GreaterThanOrEqualTo(10),
                    $"killing 5 garrison-placed Dredge Hulks must leave at least 10 part pickups live " +
                    $"(MV-1106's guaranteed 2/kill alone accounts for 10) — got {liveParts}");
            }
            finally
            {
                foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    Object.DestroyImmediate(r.gameObject);
                Object.DestroyImmediate(directorGo);
                // Mirrors the manual OnEnable invoke above: DestroyImmediate alone does not reliably run
                // OnDisable in this EditMode batch run, so without this the GameObject goes away while
                // DropSignals.RobotDied is still subscribed to it — the next test's robot kill then
                // throws MissingReferenceException reaching through the dead delegate.
                typeof(PickupDirector).GetMethod("OnDisable", NonPublicInstance).Invoke(pickupDirector, null);
                Object.DestroyImmediate(pickupGo);
                DifficultyDirector.Reset();
            }
        }

        /// <summary>Minimal synthetic World 3: stub -> a1 -> a2 (5 authored Dredge Hulk garrison slots,
        /// the real <c>world3_config.json</c> brute override mirrored here) -> boss. Same shape as
        /// MV514GarrisonHeadStartTests.TwoAreaWorld, carrying the Dredge Hulk override this ticket adds.</summary>
        private static WorldConfig TwoAreaWorld3()
        {
            return new WorldConfig
            {
                dials = new WorldDials { areaCount = 2, baseThreat = 1f, threatGrowth = 0f, pacingRhythm = new[] { 1f, 1f } },
                enemyOverrides = new[]
                {
                    new WorldEnemyOverride { kind = "brute", displayName = "DREDGE HULK", maxHealth = 300f },
                },
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "stub", index = 0, role = "entry",
                        origin = new WorldAreaOrigin { x = -2f, z = -6f }, size = new WorldAreaSize { w = 4f, d = 6f },
                    },
                    new WorldArea
                    {
                        id = "a1", index = 1, role = "normal",
                        origin = new WorldAreaOrigin { x = -10f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                        composition = new WorldComposition { rusher = 1 },
                    },
                    new WorldArea
                    {
                        // "light" density alone still seeds all 5 — every authored WorldGarrisonEntry is
                        // placed "even past count" (Garrison.SeedSlotsFor's own rule), this only needs to
                        // clear PlacePendingGarrison's own `seedCount <= 0` early-out.
                        id = "a2", index = 2, role = "normal", garrisonDensity = "light",
                        origin = new WorldAreaOrigin { x = -10f, z = 20f }, size = new WorldAreaSize { w = 20f, d = 20f },
                        composition = new WorldComposition { brute = 5 },
                        garrison = new[]
                        {
                            new WorldGarrisonEntry { kind = "brute", x = -6f, z = 24f },
                            new WorldGarrisonEntry { kind = "brute", x = -2f, z = 24f },
                            new WorldGarrisonEntry { kind = "brute", x = 2f, z = 24f },
                            new WorldGarrisonEntry { kind = "brute", x = 6f, z = 24f },
                            new WorldGarrisonEntry { kind = "brute", x = 0f, z = 30f },
                        },
                    },
                    new WorldArea
                    {
                        id = "boss", index = 3, role = "boss+exit",
                        origin = new WorldAreaOrigin { x = -10f, z = 40f }, size = new WorldAreaSize { w = 20f, d = 20f },
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
                    new WorldGate
                    {
                        id = "g1", width = 3f, opensWith = "start",
                        from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "a2", wall = "S", pos = 0.5f },
                    },
                    new WorldGate
                    {
                        id = "bg", width = 3f, opensWith = "all-sheds-destroyed",
                        from = new WorldGateEndpoint { area = "a2", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                    },
                },
            };
        }

        // ---------------------------------------------------------------- shared dummy helper

        /// <summary>A real, resolved-health Heavy — spawned through <see cref="EnemySpawner.SpawnExact"/>,
        /// with 260 base health x1.26 comfortably above every hit this test fires at it (max 30), so one
        /// tick never kills it and HealthCurrent's drop reads as exactly that tick's damage.
        ///
        /// SpawnExact's own <c>SetActive(true)</c> does not reliably invoke Unity's OnEnable
        /// synchronously inside this EditMode batch run (confirmed empirically: <see cref="RobotEnemy.Active"/>
        /// read back 0 immediately after SpawnExact without this) — the same observation
        /// MV694ShoulderRackTests/MV1036UndertowGatedOutsideWorld3Tests already document and work around
        /// by re-invoking it explicitly, which is what populates <see cref="RobotEnemy.Active"/> for
        /// <see cref="ShoulderRack"/>'s own target scan.</summary>
        private static RobotEnemy SpawnDummy(Vector3 position)
        {
            var spawnerGo = new GameObject("MV1106 Spawner");
            spawnerGo.transform.position = position;
            var spawner = spawnerGo.AddComponent<EnemySpawner>();
            List<RobotEnemy> spawned = spawner.SpawnExact(EnemyKind.Heavy, 1);
            Assert.AreEqual(1, spawned.Count, "test setup: SpawnExact didn't spawn a dummy Heavy");
            RobotEnemy dummy = spawned[0];
            dummy.transform.position = position;
            bool alreadyActive = false;
            foreach (RobotEnemy r in RobotEnemy.Active) { if (r == dummy) { alreadyActive = true; break; } }
            if (!alreadyActive) RobotEnemyOnEnable.Invoke(dummy, null);
            Physics.SyncTransforms();
            return dummy;
        }

        private static void DestroyDummy(RobotEnemy dummy)
        {
            if (dummy != null) Object.DestroyImmediate(dummy.transform.root.gameObject);
        }
    }
}
