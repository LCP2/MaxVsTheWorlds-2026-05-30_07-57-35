using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Factories;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-713: World 3's Reef ship kit. The ONE new test this ticket adds (testing policy v2, MV-465) —
    /// one method covering every EditMode-testable AC rather than four separate files. Fails to compile
    /// on e56a7de (the commit before this ticket): <c>WorldMaterials.M_ShipFloor</c>, <c>ReefKit</c>,
    /// <c>MowerHutch.ApplyReefSkin</c>, <c>AreaGate.ApplyReefSkin</c> and <c>BiomePalette.Reef</c> do not
    /// exist there.
    ///
    /// AC1: the eight named materials resolve to the ticket's exact hex values (a resolved
    /// <c>_BaseColor</c> readback, not an authored constant asserting itself — the materials are built
    /// from <c>uint</c> literals in <see cref="WorldMaterials"/>, then read back through a real
    /// <see cref="Material"/>).
    /// AC2 ("the ticket's real guard"): a Reef-skinned hydroponic reactor (<see cref="MowerHutch"/>) and
    /// power hatch (<see cref="AreaGate"/>) keep collider bounds identical, within 0.01 m, to the same
    /// object before the skin — <c>ApplyReefSkin</c> only ever writes a tint field / MaterialPropertyBlock,
    /// never a collider or a tuning number.
    /// AC3: the ocean backdrop is exactly 4 layers, none carrying a collider.
    /// AC4: applying the Reef skin never adds a real-time <see cref="Light"/> — nothing in
    /// <see cref="WorldMaterials"/> or <see cref="ReefKit"/> ever calls <c>AddComponent&lt;Light&gt;</c>.
    /// </summary>
    public sealed class MV713ReefKitTests
    {
        [Test]
        public void ReefKit_MatchesTicketAcceptanceCriteria()
        {
            // --- AC1: the eight materials exist with the exact hex values. ---
            AssertMaterialColor(WorldMaterials.M_ShipFloor, 0x13, 0x22, 0x34, "M_ShipFloor");
            AssertMaterialColor(WorldMaterials.M_ShipWall, 0x1E, 0x32, 0x47, "M_ShipWall");
            AssertMaterialColor(WorldMaterials.M_Circuit_Cyan, 0x3C, 0xDC, 0xF2, "M_Circuit_Cyan");
            AssertMaterialColor(WorldMaterials.M_Circuit_Purple, 0xC4, 0x55, 0xE8, "M_Circuit_Purple");
            AssertMaterialColor(WorldMaterials.M_BioGlow, 0x5C, 0xF2, 0xA4, "M_BioGlow");
            AssertMaterialColor(WorldMaterials.M_Hazard, 0xFF, 0x8B, 0x2E, "M_Hazard");
            AssertMaterialColor(WorldMaterials.M_MetalDark, 0x0C, 0x16, 0x22, "M_MetalDark");
            Assert.IsNotNull(WorldMaterials.M_GlassOcean, "M_GlassOcean must exist");
            AssertRgb(WorldMaterials.ReefGlassOceanNear, 0x0E, 0x6F, 0xA8, "M_GlassOcean near stop");
            AssertRgb(WorldMaterials.ReefGlassOceanFar, 0x05, 0x2B, 0x49, "M_GlassOcean far stop");

            var root = new GameObject("MV713 Probe Root");
            try
            {
                // --- AC2a: the hydroponic reactor (MowerHutch) keeps its collider/tuning across the skin. ---
                GameObject hutchGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hutchGo.transform.SetParent(root.transform, false);
                hutchGo.transform.localScale = new Vector3(3f, 2f, 3f);
                MowerHutch hutch = hutchGo.AddComponent<MowerHutch>();
                hutch.Build();

                Bounds hutchBoundsBefore = hutchGo.GetComponent<Collider>().bounds;
                float hutchHpBefore = hutch.AuthoredMax;

                hutch.ApplyReefSkin();

                Bounds hutchBoundsAfter = hutchGo.GetComponent<Collider>().bounds;
                Assert.That(Vector3.Distance(hutchBoundsBefore.min, hutchBoundsAfter.min), Is.LessThan(0.01f),
                    "ApplyReefSkin moved the hutch's collider min bound");
                Assert.That(Vector3.Distance(hutchBoundsBefore.max, hutchBoundsAfter.max), Is.LessThan(0.01f),
                    "ApplyReefSkin moved the hutch's collider max bound");
                Assert.AreEqual(hutchHpBefore, hutch.AuthoredMax, "ApplyReefSkin must not touch factory HP tuning");

                // --- AC2b: the power hatch (AreaGate) keeps its collider/tuning across the skin. ---
                GameObject gateGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                gateGo.transform.SetParent(root.transform, false);
                gateGo.transform.position = new Vector3(0f, 0f, 10f);
                gateGo.transform.localScale = new Vector3(4f, 3f, 0.6f);
                AreaGate gate = gateGo.AddComponent<AreaGate>();
                InvokeAwake(gate);

                Bounds gateBoundsBefore = gateGo.GetComponent<Collider>().bounds;
                float gateMaxHpBefore = gate.MaxHp;
                Assert.Greater(gateMaxHpBefore, 0f, "precondition: Awake must have built the gate's health");

                gate.ApplyReefSkin();

                Bounds gateBoundsAfter = gateGo.GetComponent<Collider>().bounds;
                Assert.That(Vector3.Distance(gateBoundsBefore.min, gateBoundsAfter.min), Is.LessThan(0.01f),
                    "ApplyReefSkin moved the gate's collider min bound");
                Assert.That(Vector3.Distance(gateBoundsBefore.max, gateBoundsAfter.max), Is.LessThan(0.01f),
                    "ApplyReefSkin moved the gate's collider max bound");
                Assert.AreEqual(gateMaxHpBefore, gate.MaxHp, "ApplyReefSkin must not touch gate break-HP tuning");

                // --- AC3: exactly 4 ocean layers, none carrying a collider. ---
                GameObject backdrop = ReefKit.BuildOceanBackdrop(root.transform);
                OceanLayer[] layers = backdrop.GetComponentsInChildren<OceanLayer>(true);
                Assert.AreEqual(4, layers.Length, "the ocean backdrop must be exactly 4 layers");
                foreach (OceanLayer layer in layers)
                    Assert.IsNull(layer.GetComponent<Collider>(), $"ocean layer {layer.Kind} must not carry a collider");

                // --- AC4: a World 3 (Reef) scene holds no more real-time lights than a World 1 one. ---
                // BackyardLighting is the sole place anything in this project ever creates a Light
                // component (BackyardLighting.cs), and it is not world-parameterized — so the literal
                // comparison the AC asks for is: build a "World 1" scene (lighting alone) and a "World 3"
                // scene (the same lighting PLUS every Reef-only piece this ticket adds layered on top),
                // and confirm the count does not grow.
                var world1Go = new GameObject("MV713 World1 Lighting Probe");
                var world3Go = new GameObject("MV713 World3 Lighting Probe");
                try
                {
                    world1Go.transform.SetParent(root.transform, false);
                    world3Go.transform.SetParent(root.transform, false);

                    int lightsBeforeEither = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length;

                    world1Go.AddComponent<BackyardLighting>().Apply(BackyardLook.Default);
                    int world1Lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length - lightsBeforeEither;

                    int lightsBeforeWorld3Extra = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length;
                    world3Go.AddComponent<BackyardLighting>().Apply(BackyardLook.Default);
                    hutch.ApplyReefSkin();
                    gate.ApplyReefSkin();
                    ReefKit.BuildOceanBackdrop(root.transform);
                    int world3Lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length - lightsBeforeWorld3Extra;

                    Assert.That(world3Lights, Is.LessThanOrEqualTo(world1Lights),
                        "a World 3 (Reef) scene must hold no more real-time lights than a World 1 scene");
                }
                finally
                {
                    Object.DestroyImmediate(world1Go);
                    Object.DestroyImmediate(world3Go);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                FactoryCensus.Reset();
            }
        }

        private static void InvokeAwake(Object component)
        {
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(component, null);
        }

        private static void AssertMaterialColor(Material m, byte r, byte g, byte b, string name)
        {
            Assert.IsNotNull(m, $"{name} must exist");
            AssertRgb(m.GetColor("_BaseColor"), r, g, b, name);
        }

        private static void AssertRgb(Color c, byte r, byte g, byte b, string name)
        {
            const float tol = 0.6f / 255f;
            Assert.That(c.r, Is.EqualTo(r / 255f).Within(tol), $"{name} red channel");
            Assert.That(c.g, Is.EqualTo(g / 255f).Within(tol), $"{name} green channel");
            Assert.That(c.b, Is.EqualTo(b / 255f).Within(tol), $"{name} blue channel");
        }
    }
}
