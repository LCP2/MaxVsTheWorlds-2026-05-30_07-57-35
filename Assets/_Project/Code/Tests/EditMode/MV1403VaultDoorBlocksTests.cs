using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1403: Lee walked straight into a locked vault. MV-1430 removed the City's vaults, so this now
    /// builds the Reef's a13_vault1 (the same Vault component) from the REAL world3_config.json through WorldMapLoader/MapRuntime, puts a real CharacterController "Max" outside
    /// the locked, keyless vault's west door and drives him east through it in small steps via
    /// CharacterControllerMotion.SafeMove. Asserts a RESOLVED position (Tier 2): he must still be
    /// outside the door plane afterwards, and the vault must still be Closed.
    /// // Guards MV-1403
    /// </summary>
    public sealed class MV1403VaultDoorBlocksTests
    {
        private GameObject _host;
        private GameObject _max;

        [SetUp]
        public void SetUp()
        {
            Vault.ResetRegistry();
            VaultKeyState.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (_max != null) Object.DestroyImmediate(_max);
            if (_host != null) Object.DestroyImmediate(_host);
            Vault.ResetRegistry();
            VaultKeyState.Reset();
        }

        [Test]
        public void KeylessMax_WalkingIntoTheLockedCityVaultDoor_CannotReachTheInside() // Guards MV-1403
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "world3_config must load");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            _host = new GameObject("MV-1403 host");
            MapBuild built = MapRuntime.Build(map, _host.transform);
            Vault vault = built.Vaults.Find(v => v.Id == "a13_vault1");
            Assert.IsNotNull(vault, "setup failure: a13_vault1 must be built");

            // a13_vault1: 6x6, door W => door plane at x = centre.x - 3, 0.8 m thick.
            Vector3 c = vault.transform.position;
            float doorPlaneX = c.x - 3f;

            _max = new GameObject("Max", typeof(CharacterController)) { tag = "Player" };
            var cc = _max.GetComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.4f; cc.center = new Vector3(0f, 0.9f, 0f);
            cc.enabled = false;
            _max.transform.position = new Vector3(doorPlaneX - 2.5f, 0.05f, c.z);
            cc.enabled = true;
            Physics.SyncTransforms();

            for (int i = 0; i < 60; i++)
            {
                CharacterControllerMotion.SafeMove(cc, new Vector3(0.1f, 0f, 0f));
                Physics.SyncTransforms();
                vault.Tick(0.02f);
            }

            Assert.AreEqual(Vault.VaultState.Closed, vault.State, "no key held: the vault must stay closed");
            Assert.Less(_max.transform.position.x, doorPlaneX,
                $"Max must be stopped outside the locked door plane (x {doorPlaneX}) but ended at x {_max.transform.position.x}");
        }
    }
}
