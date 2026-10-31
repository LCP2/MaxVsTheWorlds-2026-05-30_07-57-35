using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1321 (ticket AC1): "build an area with a vault and a garrison robot with carriesKey: true; the
    /// robot is elite; killing it drops a VaultKey; with Max holding the key 1.0 m from the door the door
    /// opens and 2 Supercells exist inside; a second approach places nothing more." Must fail to COMPILE
    /// on the pre-fix base commit: <see cref="WorldVault"/>, <see cref="Vault"/>,
    /// <see cref="VaultKeyState"/>, <see cref="WorldGarrisonEntry.carriesKey"/>,
    /// <see cref="RobotEnemy.CarriesKey"/> and <see cref="PickupKind.VaultKey"/> do not exist there — the
    /// strongest form of Rule 1's "fails first" proof (same idiom <c>MV1320SecretCacheTests</c> uses).
    ///
    /// Real entry points throughout: a hand-authored <see cref="WorldConfig"/> (same two-area skeleton
    /// <c>MV559AuthoredGarrisonPositionsTests</c>/<c>AreaAccumulationDirectorGarrisonAndPlacementTests</c>
    /// use) loaded through the real <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/> pipeline (builds
    /// the vault's own walls/door) and a real <see cref="AreaAccumulationDirector"/> (seeds the garrison
    /// robot). <c>eliteChancePct</c> 0 and <c>eliteMaxPerArea</c> 0 prove the KEY elite genuinely bypasses
    /// both the chance roll AND the per-area cap, not just that it happens to roll elite anyway. The kill
    /// goes through the real <see cref="RobotEnemy.TakeDamage"/> → <c>Die</c> path; the key's collection
    /// and the door's open check go through the real <c>PickupDirector.Collect</c> (reflected, same
    /// idiom <c>MV1320SecretCacheTests.InvokeCollect</c> already uses) and <see cref="Vault.Tick"/>.
    ///
    /// ONE new test (MV-465 Rule 1). Tier 2 (Rule 2): every assertion reads a resolved runtime value — a
    /// robot's own <see cref="RobotEnemy.IsElite"/>/<see cref="RobotEnemy.CarriesKey"/>, a built pickup's
    /// own <see cref="Pickup.Kind"/>/<see cref="Pickup.VaultId"/>, a vault's own
    /// <see cref="Vault.State"/>/<see cref="VaultKeyState.HeldVaultId"/> — never an authored constant,
    /// never a rendered pixel.
    /// // Guards MV-1321
    /// </summary>
    public sealed class MV1321VaultAndKeyEliteTests
    {
        // Same "distinctive far-off origin" idiom MV1320SecretCacheTests/MV559AuthoredGarrisonPositionsTests
        // use — EditMode tests share one physics scene for the whole cc-verify run with no per-test reset.
        private const float OriginX = 91452f;
        private const float OriginZ = 38217f;

        // Comfortably covers the cache's own 0.9 m scatter ring (Vault's own CacheScatterRadius) while
        // staying well clear of anything else this isolated world ever places.
        private const float NearVaultRadius = 1.6f;

        private GameObject _root;
        private GameObject _directorGo;
        private GameObject _playerGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            Vault.ResetRegistry();
            VaultKeyState.Reset();
            DestroyAllPickupsAndDirectors();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_root != null) Object.DestroyImmediate(_root);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            DestroyAllPickupsAndDirectors();
            VaultKeyState.Reset();
            Vault.ResetRegistry();
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
        }

        private static void DestroyAllPickupsAndDirectors()
        {
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            Pickup.ResetRegistry();
        }

        /// <summary>Stub (entry, empty) -> "a1" (one 6x6 south-door vault + one KEY-carrying Rusher
        /// garrison entry) — same minimal two-area skeleton <c>MV1319RepairBayTests.BuildWorld</c> uses
        /// (no boss/exit area: building one here would also build a <c>BigBermudaRig</c>, whose own
        /// first-time shader-resolution logs trip this filtered single-class run's strict log check — a
        /// fixture detail unrelated to anything this ticket tests). <c>eliteChancePct</c>/
        /// <c>eliteMaxPerArea</c> are both 0 — a garrison robot WITHOUT <c>carriesKey</c> could never
        /// roll elite under these dials, so the KEY robot rolling elite anyway is proof of the bypass,
        /// not a coincidence of a generous roll.</summary>
        private static WorldConfig BuildWorld() => new WorldConfig
        {
            world = "MV-1321 Test World",
            dials = new WorldDials
            {
                areaCount = 1, baseThreat = 0f, threatGrowth = 0f,
                pacingRhythm = new[] { 1f }, powerupCadence = 99,
                eliteChancePct = 0f, eliteMaxPerArea = 0,
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
                    id = "a1", index = 1, role = "normal", garrisonDensity = "heavy", // heavy (0.85) * 1 rounds to 1 — SeedGarrison's own seedCount<=0 early-return would otherwise skip the authored entry entirely
                    origin = new WorldAreaOrigin { x = OriginX - 10f, z = OriginZ },
                    size = new WorldAreaSize { w = 20f, d = 20f },
                    composition = new WorldComposition { rusher = 1 }, // exactly enough queued for the one authored garrison entry below
                    vaults = new[]
                    {
                        new WorldVault { id = "a1_vault1", x = OriginX, z = OriginZ, w = 6f, d = 6f, door = "S" },
                    },
                    garrison = new[]
                    {
                        new WorldGarrisonEntry { kind = "rusher", x = OriginX + 6f, z = OriginZ + 6f, carriesKey = true },
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
        public void KeyEliteDropsAKey_OpensItsVault_PlacesTwoSupercells_AndASecondApproachAddsNothing() // Guards MV-1321
        {
            WorldConfig cfg = BuildWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            // === Build the vault's own geometry (walls/door/icon) through the real MapRuntime pipeline ===
            _root = new GameObject("MV-1321 Root");
            MapBuild built = MapRuntime.Build(map, _root.transform);
            Assert.AreEqual(1, built.Vaults.Count, "setup failure: fixture must build exactly one vault");
            Vault vault = built.Vaults[0];
            Assert.AreEqual(Vault.VaultState.Closed, vault.State, "setup failure: a freshly built vault must start closed");

            // === Seed the garrison robot through the real AreaAccumulationDirector pipeline ===
            DevTuning.MaxActiveRobots = 100f; // cap irrelevant — garrison seeding bypasses it entirely anyway
            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>());

            RobotEnemy[] active = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None);
            Assert.AreEqual(1, active.Length, "setup failure: fixture must seed exactly one garrison robot");
            RobotEnemy keyElite = active[0];

            // === AC1 "the robot is elite": bypasses BOTH the 0% chance roll and the 0-per-area cap ===
            Assert.IsTrue(keyElite.IsElite, "MV-1321 AC1: a carriesKey garrison robot must always spawn elite, even at eliteChancePct 0 and eliteMaxPerArea 0");
            Assert.IsTrue(keyElite.CarriesKey, "MV-1321 AC1: a carriesKey garrison robot must resolve CarriesKey true");

            // === AC1 "killing it drops a VaultKey" ===
            Vector3 deathPos = keyElite.transform.position;
            keyElite.TakeDamage(new DamageInfo(1e9f, deathPos, Vector3.down, Team.Player));
            Assert.IsFalse(keyElite.IsAlive, "setup failure: a lethal hit must kill the KEY elite");

            PickupDirector director2 = PickupDirector.EnsureInstalled();
            List<Pickup> nearDeath = LivePickupsNear(director2, new Vector2(deathPos.x, deathPos.z), 2f);
            List<Pickup> keys = new List<Pickup>();
            foreach (Pickup p in nearDeath) if (p.Kind == PickupKind.VaultKey) keys.Add(p);
            Assert.AreEqual(1, keys.Count, "MV-1321 AC1: killing the KEY elite must drop exactly one VaultKey");
            Assert.AreEqual(vault.Id, keys[0].VaultId, "MV-1321 AC1: the dropped VaultKey must carry its own area's vault id");

            // === Collecting it (walk-over, reflected into PickupDirector.Collect same as MV1320SecretCacheTests) ===
            InvokeCollect(director2, keys[0]);
            Assert.IsTrue(VaultKeyState.HeldFor(vault.Id), "MV-1321 AC1: collecting the VaultKey must bank it, bound to its own vault");

            // === AC1 "with Max holding the key 1.0 m from the door the door opens and 2 Supercells exist
            // inside" — the door sits on the south wall (door: "S"), 1.0 m south of it is well inside the
            // 1.5 m open radius ===
            float halfDepth = 3f; // vault d=6
            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = new Vector3(OriginX, 0f, OriginZ - halfDepth + 1.0f);

            vault.Tick(0.1f);
            Assert.AreEqual(Vault.VaultState.Open, vault.State, "MV-1321 AC1: Max holding the key 1.0 m from the door must open it");
            Assert.IsFalse(VaultKeyState.HeldFor(vault.Id), "MV-1321 AC1: opening the vault must consume the held key");

            Vector2 vaultCentreXz = new Vector2(OriginX, OriginZ);
            int supercellsAfterOpen = Count(LivePickupsNear(director2, vaultCentreXz, NearVaultRadius), PickupKind.Supercell);
            Assert.AreEqual(2, supercellsAfterOpen, "MV-1321 AC1: opening the vault must place exactly 2 Supercells at its centre");

            // === AC1 "a second approach places nothing more" ===
            vault.Tick(0.1f);
            int supercellsAfterSecondApproach = Count(LivePickupsNear(director2, vaultCentreXz, NearVaultRadius), PickupKind.Supercell);
            Assert.AreEqual(supercellsAfterOpen, supercellsAfterSecondApproach,
                "MV-1321 AC1: a second approach to an already-open vault must place nothing more");
        }

        private static List<Pickup> LivePickupsNear(PickupDirector director, Vector2 centreXz, float radius)
        {
            var live = (IList)typeof(PickupDirector)
                .GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(director);
            var result = new List<Pickup>();
            foreach (object o in live)
            {
                var p = (Pickup)o;
                if (p == null) continue;
                Vector2 pXz = new Vector2(p.transform.position.x, p.transform.position.z);
                if (Vector2.Distance(pXz, centreXz) <= radius) result.Add(p);
            }
            return result;
        }

        private static int Count(List<Pickup> pickups, PickupKind kind)
        {
            int n = 0;
            foreach (Pickup p in pickups) if (p.Kind == kind) n++;
            return n;
        }

        private static void InvokeCollect(PickupDirector director, Pickup pickup)
        {
            var liveField = typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);
            var live = (IList)liveField.GetValue(director);
            int index = live.IndexOf(pickup);
            Assert.GreaterOrEqual(index, 0, "the collected pickup must still be live on the director");
            typeof(PickupDirector).GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { index, pickup });
        }
    }
}
