using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1363: a destroyed Reef Hatchery Clam used to leave a pale box behind (its lid child was never
    /// hidden), and the live clam wore a Unity Cube. The ONE new test: destroy a built Reef clam, assert
    /// every Renderer under it is disabled except the wreck plate (which sits above the deck), and that no
    /// MeshFilter on the live clam uses the built-in Cube mesh. Fails on the base commit: the lid renderer
    /// stays enabled there, and the lid/body carry the built-in Cube mesh.
    /// </summary>
    public sealed class MV1363ClamReskinTests
    {
        [Test]
        public void DestroyedReefClam_HidesEveryRenderer_ExceptWreckPlate_AndNoCubeMesh()
        {
            LogAssert.ignoreFailingMessages = true;

            WorldDefinition row = WorldCatalog.Get(WorldCatalog.IndexOf(WorldIds.Reef));
            WorldConfig cfg = WorldLibrary.Load(row.ConfigKey);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject("MV1363 Probe Root");
            try
            {
                MapBuild build = MapRuntime.Build(map, root.transform);
                Assert.IsTrue(build.Actors.TryGetValue("a1_shed", out GameObject shed), "a1 shed did not build");
                var clam = shed.GetComponent<MowerHutch>();
                typeof(MowerHutch).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(clam, null);
                clam.ApplyHatcheryClamBehavior();

                foreach (var mf in shed.GetComponentsInChildren<MeshFilter>(true))
                    Assert.AreNotEqual("Cube", mf.sharedMesh != null ? mf.sharedMesh.name : "",
                        $"{mf.name} still uses the built-in Cube mesh");

                clam.TakeDamage(new DamageInfo(1e7f, shed.transform.position, Vector3.forward, Team.Player));
                Assert.IsFalse(clam.IsAlive, "clam did not die");

                int enabledLeft = 0;
                foreach (var r in shed.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name == "ClamWreckPlate") continue;
                    if (r.enabled) { enabledLeft++; Debug.Log($"MV1363 still enabled: {r.name}"); }
                }
                Assert.AreEqual(0, enabledLeft, "renderers still enabled on a destroyed clam");
            }
            finally
            {
                Object.DestroyImmediate(root);
                FactoryCensus.Reset();
            }
        }
    }
}
