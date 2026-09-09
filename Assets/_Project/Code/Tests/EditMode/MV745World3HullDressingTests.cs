using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-745: World 3's floor and walls read as void on build d63d576 — a flat near-black plane
    /// speckled with white points, no plate seams, no cyan circuitry, and area edges that read as
    /// nothing rather than hull. Root cause (confirmed by reading the code, not re-derived):
    /// <see cref="WorldMaterials.M_ShipFloor"/>/<see cref="WorldMaterials.M_ShipWall"/>/
    /// <see cref="WorldMaterials.M_Circuit_Cyan"/> (MV-713) had zero production callers on the floor
    /// or walls — World 3's floor and walls wore the same generic, procedural Ground/Wall shader every
    /// biome shares (<see cref="WorldMaterials.Apply"/>'s shape-classified sweep), only recoloured by
    /// <see cref="BiomePalette.Reef"/>, never the ticket's own named materials. Separately,
    /// <see cref="BackyardLighting"/> always set <c>RenderSettings.skybox</c> regardless of biome, so
    /// World 3 also rendered under Backyard's daylight sky dome.
    ///
    /// Fails on 6845204 (the commit before this fix): <c>WorldMaterials.M_ShipFloor</c> is never
    /// assigned to the floor renderer, no renderer anywhere wears <c>M_Circuit_Cyan</c> (this map's
    /// cover carries no "machinery" piece for MV-744's turret to attach to), and the skybox is never
    /// null regardless of which biome is active.
    ///
    /// Asserts RESOLVED state only, against the real shipped configs (World 1 and World 3), the same
    /// way <see cref="MV742StormdrainPaletteTests"/> already does for World 2's analogous bug: the
    /// actual material identity/property a built renderer ends up wearing, never an authored constant.
    /// </summary>
    public sealed class MV745World3HullDressingTests
    {
        [Test]
        public void World3Build_WearsTheNamedHullMaterials_AndHasNoSky_WhileWorld1IsUnchanged()
        {
            // --- World 3: floor, walls, circuit spine, no skybox. ---
            WorldConfig cfg3 = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg3, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg3, out MapData map3, out string reason3), reason3);

            var root3 = new GameObject("MV745 World3 Probe Root");
            BackyardLighting lighting3 = null;
            try
            {
                // Exactly BackyardPath.Awake's own order: geometry, then the world's palette sweep,
                // then the Reef-only pass — the pass this ticket adds a step to.
                MapRuntime.Build(map3, root3.transform);
                var wm = new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
                wm.Apply(BiomePalette.ForWorld(2));
                ReefKit.DressHull(root3.transform);

                // AC1: the floor's shared material is WorldMaterials.M_ShipFloor, seam detail scale > 0.
                Renderer floor = FindNamed(root3.transform, "Map Floor");
                Assert.IsNotNull(floor, "expected World 3's built map to include its floor slab");
                Assert.AreSame(WorldMaterials.M_ShipFloor, floor.sharedMaterial,
                    "the World 3 floor must wear WorldMaterials.M_ShipFloor by identity, not a generic swept material");
                Assert.Greater(floor.sharedMaterial.GetFloat("_DetailScale"), 0f,
                    "the floor's seam detail scale must be non-zero, or the deck reads as a flat tint again");

                // AC2: at least one renderer uses M_Circuit_Cyan and has emission enabled.
                Renderer circuit = FindByMaterial(root3.transform, WorldMaterials.M_Circuit_Cyan);
                Assert.IsNotNull(circuit, "no renderer in the World 3 scene wears WorldMaterials.M_Circuit_Cyan");
                Assert.IsTrue(circuit.sharedMaterial.IsKeywordEnabled("_EMISSION"),
                    "the circuit material must have emission enabled — a cyan spine that isn't lit is just paint");

                // AC3: every built wall wears M_ShipWall, and the count matches the map's own solved walls.
                List<Renderer> walls = AllWalls(root3.transform);
                int expectedWalls = MapGeometry.Walls(map3).Count;
                Assert.Greater(expectedWalls, 0, "precondition: World 3's own map must solve to some walls");
                Assert.AreEqual(expectedWalls, walls.Count,
                    "the number of built wall renderers must match the map's own solved wall count");
                foreach (Renderer w in walls)
                    Assert.AreSame(WorldMaterials.M_ShipWall, w.sharedMaterial,
                        $"wall '{w.name}' must wear WorldMaterials.M_ShipWall by identity");

                // AC4 (World 3 half): no skybox once BackyardLighting has run against the Reef palette.
                lighting3 = new GameObject("Lighting").AddComponent<BackyardLighting>();
                lighting3.Apply(BackyardLook.Default);
                Assert.IsNull(RenderSettings.skybox,
                    "World 3 is a sealed hull interior — no skybox should show past its walls");
            }
            finally
            {
                if (lighting3 != null) Object.DestroyImmediate(lighting3.gameObject);
                Object.DestroyImmediate(root3);
                RenderSettings.skybox = null;
            }

            // --- World 1: unaffected by any of the above (AC5), and DOES get a skybox (AC4's other half). ---
            WorldConfig cfg1 = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg1, "World 1's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg1, out MapData map1, out string reason1), reason1);

            var root1 = new GameObject("MV745 World1 Probe Root");
            BackyardLighting lighting1 = null;
            try
            {
                MapRuntime.Build(map1, root1.transform);
                var wm = new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
                wm.Apply(BiomePalette.ForWorld(0));
                // No ReefKit.DressHull call here — World 1 never runs the Reef-only pass.

                Renderer floor = FindNamed(root1.transform, "Map Floor");
                Assert.IsNotNull(floor, "expected World 1's built map to include its floor slab");
                Assert.AreNotSame(WorldMaterials.M_ShipFloor, floor.sharedMaterial,
                    "World 1's floor must not have picked up World 3's named hull material");

                int expectedWalls1 = MapGeometry.Walls(map1).Count;
                Assert.AreEqual(expectedWalls1, AllWalls(root1.transform).Count,
                    "World 1's own wall count must be exactly what its map solves — unaffected by MV-745");

                lighting1 = new GameObject("Lighting").AddComponent<BackyardLighting>();
                lighting1.Apply(BackyardLook.Default);
                Assert.IsNotNull(RenderSettings.skybox,
                    "World 1 must still get a sky — only World 3's hull interior loses it");
            }
            finally
            {
                if (lighting1 != null) Object.DestroyImmediate(lighting1.gameObject);
                Object.DestroyImmediate(root1);
                RenderSettings.skybox = null;
            }
        }

        private static Renderer FindNamed(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.GetComponent<Renderer>();
            return null;
        }

        private static Renderer FindByMaterial(Transform root, Material material)
        {
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
                if (r.sharedMaterial == material) return r;
            return null;
        }

        private static List<Renderer> AllWalls(Transform root)
        {
            var walls = new List<Renderer>();
            foreach (StructuralWall w in root.GetComponentsInChildren<StructuralWall>(true))
            {
                var r = w.GetComponent<Renderer>();
                if (r != null) walls.Add(r);
            }
            return walls;
        }
    }
}
