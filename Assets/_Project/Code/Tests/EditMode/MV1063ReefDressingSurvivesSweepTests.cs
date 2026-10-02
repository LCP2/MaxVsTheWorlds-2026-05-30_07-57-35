using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1063: World 3's deck, circuit inlays, hydroponic glow, hull lamps and ocean glass rendered
    /// flat in a built player even though <see cref="ReefKit.DressHull"/> assigns the right materials
    /// during <c>BackyardPath.Awake</c>. Root cause: <see cref="RuntimeSurfaceDirector"/> installs
    /// itself at <c>AfterSceneLoad</c> and sweeps every <see cref="MeshRenderer"/> in its own
    /// <c>Start()</c> — guaranteed by Unity to run after every object's <c>Awake</c> has already fired
    /// — and nothing told it to leave a freshly Reef-dressed floor/wall alone, so it repainted them
    /// straight back onto the generic shape-classified material one phase after <c>DressHull</c> set
    /// the real one. The fix tags every Reef-dressed renderer's root with the existing
    /// <see cref="KeepsOwnMaterial"/> escape hatch, which both <see cref="WorldMaterials.IsWorldSurface"/>
    /// and <see cref="RuntimeSurfaceDirector"/>'s own sweep already honour via <c>GetComponentInParent</c>.
    ///
    /// Fails on b9fce15 (the commit before this ticket): the floor/wall renderers come out of the sweep
    /// wearing <see cref="MaterialLibrary.Surface"/>'s generic material, not
    /// <see cref="WorldMaterials.M_ShipFloor"/>/<see cref="WorldMaterials.M_ShipWall"/>.
    /// </summary>
    public sealed class MV1063ReefDressingSurvivesSweepTests
    {
        [SetUp]
        public void SetUp() => MaterialLibrary.Palette = BiomePalette.Reef;

        [Test]
        public void RuntimeSweep_LeavesReefDressedFloorAndWallWearingTheirShipMaterials()
        {
            var host = new GameObject("MV1063 Hull Probe");
            try
            {
                GameObject floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floorGo.transform.SetParent(host.transform, false);
                floorGo.transform.localScale = new Vector3(10f, 0.2f, 10f);
                floorGo.AddComponent<StructuralFloor>();

                GameObject wallGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wallGo.transform.SetParent(host.transform, false);
                wallGo.transform.position = new Vector3(0f, 1.5f, 5f);
                wallGo.transform.localScale = new Vector3(10f, 3f, 0.3f);
                wallGo.AddComponent<StructuralWall>();

                ReefKit.DressHull(host.transform);

                Renderer floorRenderer = floorGo.GetComponent<Renderer>();
                Renderer wallRenderer = wallGo.GetComponent<Renderer>();

                Assert.AreSame(WorldMaterials.M_ShipFloor, floorRenderer.sharedMaterial,
                    "precondition: DressHull must set the floor's ship material before the sweep ever runs");
                Assert.AreSame(WorldMaterials.M_ShipWall, wallRenderer.sharedMaterial,
                    "precondition: DressHull must set the wall's ship material before the sweep ever runs");

                RunSweep();

                Assert.AreSame(WorldMaterials.M_ShipFloor, floorRenderer.sharedMaterial,
                    "RuntimeSurfaceDirector's Start() sweep must never undo ReefKit.DressHull's floor material " +
                    "(MV-1063: this is why World 3's deck rendered flat in a built player)");
                Assert.AreSame(WorldMaterials.M_ShipWall, wallRenderer.sharedMaterial,
                    "RuntimeSurfaceDirector's Start() sweep must never undo ReefKit.DressHull's wall material " +
                    "(MV-1063: this is why World 3's hull rendered flat in a built player)");
            }
            finally { Object.DestroyImmediate(host); }
        }

        private static void RunSweep()
        {
            var go = new GameObject("mv1063-sweep-test-director");
            try
            {
                var director = go.AddComponent<RuntimeSurfaceDirector>();
                typeof(RuntimeSurfaceDirector).GetMethod("Sweep", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(director, null);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
