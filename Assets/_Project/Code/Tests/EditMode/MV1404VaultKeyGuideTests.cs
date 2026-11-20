using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1404 (ticket AC1): "after Max collects a7_vault1's key, the HUD key indicator is active and the
    /// arrow's resolved direction points from Max to a7_vault1 (within 10 degrees); after the vault opens,
    /// both are inactive." Must fail to COMPILE on the pre-fix base commit: <see cref="VaultKeyGuide"/> does
    /// not exist there (same strongest-form-of-Rule-1 idiom MV1321VaultAndKeyEliteTests uses).
    ///
    /// Real entry points: a hand-authored <see cref="WorldConfig"/> with a vault id "a7_vault1" loaded
    /// through <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/>; the key is placed through
    /// <see cref="PickupDirector.PlaceVaultKeyAt"/> and collected through the real
    /// <c>PickupDirector.Collect</c> (reflected); the door opens through <see cref="Vault.Tick"/>.
    /// Tier 2: every assertion reads a resolved runtime value (the guide's own active flags and the arrow
    /// transform's resolved forward direction), never an authored constant or a pixel.
    /// // Guards MV-1404
    /// </summary>
    public sealed class MV1404VaultKeyGuideTests
    {
        private const float OriginX = 93452f;
        private const float OriginZ = 41217f;

        private GameObject _root;
        private GameObject _playerGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            Vault.ResetRegistry();
            VaultKeyState.Reset();
            Cleanup();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_root != null) Object.DestroyImmediate(_root);
            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            Cleanup();
            VaultKeyState.Reset();
            Vault.ResetRegistry();
            DevTuning.Reset();
        }

        private static void Cleanup()
        {
            foreach (var g in Object.FindObjectsByType<VaultKeyGuide>(FindObjectsSortMode.None))
                Object.DestroyImmediate(g.gameObject);
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            Pickup.ResetRegistry();
        }

        private static WorldConfig BuildWorld() => new WorldConfig
        {
            world = "MV-1404 Test World",
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
                    id = "a7", index = 1, role = "normal",
                    origin = new WorldAreaOrigin { x = OriginX - 10f, z = OriginZ },
                    size = new WorldAreaSize { w = 20f, d = 20f },
                    vaults = new[]
                    {
                        new WorldVault { id = "a7_vault1", x = OriginX, z = OriginZ, w = 6f, d = 6f, door = "S" },
                    },
                },
            },
            gates = new[]
            {
                new WorldGate
                {
                    id = "g0", width = 3f, opensWith = "start",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a7", wall = "S", pos = 0.5f },
                },
            },
        };

        [Test]
        public void CollectingTheKey_ShowsHudKeyAndAnArrowToItsVault_BothGoWhenTheVaultOpens() // Guards MV-1404
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(BuildWorld(), out MapData map, out string reason), reason);
            _root = new GameObject("MV-1404 Root");
            MapBuild built = MapRuntime.Build(map, _root.transform);
            Assert.AreEqual(1, built.Vaults.Count, "setup failure: fixture must build exactly one vault");
            Vault vault = built.Vaults[0];
            Assert.AreEqual("a7_vault1", vault.Id, "setup failure: the vault must carry the authored id");

            // Max stands well away from the vault, north-east of its door.
            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = new Vector3(OriginX + 9f, 0f, OriginZ + 7f);

            VaultKeyGuide guide = VaultKeyGuide.EnsureInstalled();
            guide.Tick(0.016f);
            Assert.IsFalse(guide.KeyIconActive, "setup failure: no key held yet, so no key indicator");
            Assert.IsFalse(guide.ArrowActive, "setup failure: no key held yet, so no arrow");

            // Place and collect the key through the real pickup pipeline.
            PickupDirector director = PickupDirector.EnsureInstalled();
            Pickup key = director.PlaceVaultKeyAt(new Vector3(OriginX + 9f, 0f, OriginZ + 7f), vault.Id);
            InvokeCollect(director, key);
            Assert.IsTrue(VaultKeyState.HeldFor(vault.Id), "setup failure: collecting the key must bank it for a7_vault1");

            guide.Tick(0.016f);
            Assert.IsTrue(guide.KeyIconActive, "MV-1404 AC1: after Max collects the key, the HUD key indicator must be active");
            Assert.IsTrue(guide.ArrowActive, "MV-1404 AC1: after Max collects the key, the arrow at his feet must be active");

            Vector3 doorWorld;
            Assert.IsTrue(Vault.TryGetPosition(vault.Id, out doorWorld, out bool closed) && closed, "setup failure: a7_vault1 must be registered and closed");
            Vector3 want = doorWorld - _playerGo.transform.position; want.y = 0f;
            float angle = Vector3.Angle(guide.ArrowDirection, want.normalized);
            Assert.LessOrEqual(angle, 10f,
                $"MV-1404 AC1: the arrow must point from Max to a7_vault1 (within 10 degrees); it was {angle:F1} degrees off");

            // Max walks to the door; the vault opens and consumes the key.
            _playerGo.transform.position = doorWorld + new Vector3(0f, 0f, -0.5f);
            vault.Tick(0.1f);
            Assert.AreEqual(Vault.VaultState.Open, vault.State, "setup failure: Max at the door holding the key must open the vault");

            guide.Tick(0.016f);
            Assert.IsFalse(guide.KeyIconActive, "MV-1404 AC1: after the vault opens, the HUD key indicator must be inactive");
            Assert.IsFalse(guide.ArrowActive, "MV-1404 AC1: after the vault opens, the arrow must be inactive");
        }

        private static void InvokeCollect(PickupDirector director, Pickup pickup)
        {
            var live = (IList)typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(director);
            int index = live.IndexOf(pickup);
            Assert.GreaterOrEqual(index, 0, "the key pickup must be live on the director");
            typeof(PickupDirector).GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { index, pickup });
        }
    }
}
