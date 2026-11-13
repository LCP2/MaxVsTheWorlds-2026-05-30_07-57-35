using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1347: <c>RusherCap.Apply</c> rebuilt the composition with a constructor that ends at
    /// Dreadnought, zeroing every newer kind (Orbital, Projector, Jammer...) in any blend-with-dial
    /// area once the Rusher cap bit. Sole guard on that defect.
    /// </summary>
    public sealed class MV1347RusherCapKeepsNewKindsTests
    {
        [Test]
        public void World4_AreasEnteredThroughTheDirector_KeepEveryAuthoredOrbitalProjectorAndJammer()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "the shipped world3_config.json failed to load");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string mapReason), mapReason);

            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            Camera[] suppressed = CameraTestUtil.SuppressAmbientMainCameras();
            var directorGo = new GameObject("Area Accumulation");
            try
            {
                var director = directorGo.AddComponent<AreaAccumulationDirector>();
                director.ConfigureWorld(cfg, worldIndex: WorldCatalog.IndexOf(WorldIds.Reef));
                director.Configure(map, System.Array.Empty<CoverPiece>()); // fills area 1
                for (int i = 2; i <= 6; i++) director.EnterArea(i);

                RobotEnemy[] all = Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                int Count(int area, EnemyKind k) =>
                    System.Array.FindAll(all, r => r != null && r.AreaIndex == area && r.Kind == k).Length;

                for (int a = 1; a <= 4; a++)
                    Assert.AreEqual(1, Count(a, EnemyKind.Orbital), $"a{a} should hold exactly one Orbital");
                Assert.AreEqual(1, Count(4, EnemyKind.Projector), "a4 should hold its authored Projector");
                Assert.AreEqual(1, Count(6, EnemyKind.Jammer), "a6 should hold its authored Jammer");
            }
            finally
            {
                GameObject bodies = GameObject.Find("Area Robots");
                if (bodies != null) Object.DestroyImmediate(bodies);
                Object.DestroyImmediate(directorGo);
                CameraTestUtil.RestoreAmbientMainCameras(suppressed);
                RobotEnemy.ResetRegistry();
                DevTuning.Reset();
            }
        }
    }
}
