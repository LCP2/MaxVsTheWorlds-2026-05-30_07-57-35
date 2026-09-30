using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Guards MV-1042 -- <see cref="MapStaticBatchRoot.Active"/> used to be a plain auto-property,
    /// cleared only from <c>OnDestroy</c> (which never runs outside Play mode for this component), so
    /// a destroyed instance was left reachable through it. Every production reader goes through `?.`
    /// (<see cref="CorrosionPuddle.Spawn"/> among them), and `?.`'s own null check is a raw reference
    /// check, never Unity's overloaded `==` -- so it could not see the fake-null and proceeded to call
    /// into the corpse. Fixed by collapsing a destroyed instance to a real null in the getter itself,
    /// the same fake-null idiom MV-981 already established at <c>MapStaticBatchRoot.ApplyAreaGate</c>.
    ///
    /// This is also why the assertion below uses `is null` rather than `== null`: a direct `==`
    /// comparison against the literal `null` always resolves through <c>UnityEngine.Object</c>'s own
    /// overloaded operator regardless of this fix (that overload already treats a destroyed instance
    /// as equal to null), so it cannot tell the fixed getter apart from the broken one. `is null`
    /// compiles to the same raw reference check `?.` uses internally, so it is the only resolved-value
    /// assertion that actually exercises what this ticket changed.
    /// </summary>
    public sealed class MV1042ActiveFakeNullTests
    {
        private GameObject _playerGo;
        private PlayerHealth _playerHealth;

        [SetUp]
        public void SetUp()
        {
            DevMode.Reset();
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
            DevMode.Reset();
            foreach (var p in Object.FindObjectsByType<CorrosionPuddle>(FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        [Test]
        public void ActiveReadsBackNull_AndCorrosionPuddleSpawnStillDamages_AfterTheMapRootIsDestroyed()
        {
            // Guards MV-1042
            LogAssert.ignoreFailingMessages = true; // same collider-strip [Error] noise every full-world-build test carries

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "World 1's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV1042 Host");
            try
            {
                MapRuntime.Build(map, host.transform);
                Transform mapRoot = host.transform.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
                var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
                Assert.IsNotNull(batchRoot, "MapRuntime never attached its own MapStaticBatchRoot");

                // MapStaticBatchRoot.Start() -- never invoked automatically outside Play mode.
                InvokePrivate(batchRoot, "Start");
                Assert.IsTrue(MapStaticBatchRoot.Active == batchRoot,
                    "setup failure: Start() must set Active to the batch root before this test destroys it");

                // Destroys the host, and the batch root under it, WITHOUT going through OnDestroy's own
                // `if (Active == this) Active = null;` clear -- OnDestroy never runs outside Play mode
                // for this component, so Active is left pointing at a corpse exactly the way this
                // ticket's description measured.
                Object.DestroyImmediate(host);

                Assert.IsTrue(MapStaticBatchRoot.Active is null,
                    "MV-1042: Active must read back as a REAL null (the same raw check `?.` performs) once its instance is destroyed, even though nothing ever cleared it");

                _playerGo = new GameObject("Player", typeof(CharacterController)) { tag = "Player" };
                _playerGo.AddComponent<PlayerController>();
                _playerHealth = _playerGo.AddComponent<PlayerHealth>();
                _playerHealth.Initialize(); // exposed publicly so an EditMode test can invoke it directly
                _playerGo.transform.position = Vector3.zero;

                CorrosionPuddle puddle = null;
                Assert.DoesNotThrow(() => puddle = CorrosionPuddle.Spawn(Vector3.zero, radius: 1.5f, duration: 10f),
                    "MV-1042: CorrosionPuddle.Spawn must not throw when the map's Active gate has already been destroyed");
                Assert.IsNotNull(puddle);

                try
                {
                    float before = _playerHealth.Current;
                    puddle.Tick(1.0f); // 4 ticks at the authored 0.25s cadence
                    float lost = before - _playerHealth.Current;

                    Assert.AreEqual(6f, lost, 0.5f,
                        "MV-1042: the puddle must still deal its authored ~6 dmg/s -- with the fix, Active?.RegisterGatedActorAtPosition " +
                        "correctly no-ops instead of gating the puddle invisible off the destroyed root's stale zone state");
                }
                finally
                {
                    Object.DestroyImmediate(puddle.gameObject);
                }
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);
    }
}
