using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-431 — the ability module (the shed's Hydro-device drop) used to wear DarkSteel + Steel + the
    /// power cell's own HydroGlow cyan, so at the 72° camera it read as "a slightly larger part" instead
    /// of the run-defining drop it is. This pins the fix: its own red colour family, a chrome cap for a
    /// readable top face, and it stays the largest of the three ground scales.
    /// </summary>
    public sealed class WeaponPartArtHydroDeviceTests
    {
        [Test]
        public void Coils_AreModuleRed_NotSteel()
        {
            var root = WeaponPartArt.BuildHydroDevice();
            var coil0 = root.transform.Find("Coil0");
            Assert.IsNotNull(coil0, "the device prop has no Coil0.");

            var mr = coil0.GetComponent<MeshRenderer>();
            var steel = MaterialLibrary.Tinted(SurfaceKind.Metal, new Color(0.55f, 0.58f, 0.63f));
            var moduleRed = MaterialLibrary.Tinted(SurfaceKind.Metal, new Color(0.85f, 0.12f, 0.10f));
            Assert.AreNotSame(steel, mr.sharedMaterial,
                "the coil is still tinted the neutral Steel — it should be the device's own red (MV-431 AC1).");
            Assert.AreSame(moduleRed, mr.sharedMaterial,
                "the coil should be tinted ModuleRed.");

            Object.DestroyImmediate(root);
        }

        [Test]
        public void CoreGlow_IsModuleGlow()
        {
            var root = WeaponPartArt.BuildHydroDevice();
            var core = root.transform.Find("Core");
            Assert.IsNotNull(core, "the device prop has no Core.");
            Assert.AreEqual(0.32f, core.localScale.x, 1e-4f, "the core glow should have grown to size 0.32.");
            Assert.AreEqual(new Vector3(0f, 0.34f, 0f), core.localPosition,
                "the core glow should sit at (0, 0.34, 0).");

            Object.DestroyImmediate(root);
        }

        [Test]
        public void Cap_ExistsWithChromeMaterial_AtTheDocumentedPose()
        {
            var root = WeaponPartArt.BuildHydroDevice();
            var cap = root.transform.Find("Cap");
            Assert.IsNotNull(cap, "the device prop has no Cap — MV-431 AC3.");
            Assert.AreEqual(new Vector3(0f, 0.52f, 0f), cap.localPosition, "the cap should sit at (0, 0.52, 0).");
            Assert.AreEqual(new Vector3(0.10f, 0.02f, 0.10f), cap.localScale, "the cap should be scaled (0.10, 0.02, 0.10).");

            var mr = cap.GetComponent<MeshRenderer>();
            var chrome = MaterialLibrary.Tinted(SurfaceKind.Metal, WeaponPartArt.Chrome);
            Assert.AreSame(chrome, mr.sharedMaterial, "the cap should be Chrome.");

            Object.DestroyImmediate(root);
        }

        [Test]
        public void Fins_LocalPositionsAreUnchangedFromHead()
        {
            // MV-431: "the fins are already seated correctly — do not fix them, only the scale.x and
            // material change." Pinned so a later refactor can't drift the position.
            var root = WeaponPartArt.BuildHydroDevice();
            for (int i = 0; i < 4; i++)
            {
                var fin = root.transform.Find($"Fin{i}");
                Assert.IsNotNull(fin, $"the device prop is missing Fin{i}.");

                float a = i * 90f;
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                Vector3 expectedPos = dir * 0.24f + Vector3.up * 0.16f;
                Assert.AreEqual(expectedPos.x, fin.localPosition.x, 1e-4f, $"Fin{i} x drifted.");
                Assert.AreEqual(expectedPos.y, fin.localPosition.y, 1e-4f, $"Fin{i} y drifted.");
                Assert.AreEqual(expectedPos.z, fin.localPosition.z, 1e-4f, $"Fin{i} z drifted.");

                Assert.AreEqual(new Vector3(0.055f, 0.2f, 0.16f), fin.localScale, $"Fin{i} scale should widen to 0.055 on x.");
            }

            Object.DestroyImmediate(root);
        }

        [Test]
        public void Fins_AreDarkSteel_SoTheRedCoreIsTheOnlyBrightThing()
        {
            var root = WeaponPartArt.BuildHydroDevice();
            var fin0 = root.transform.Find("Fin0");
            Assert.IsNotNull(fin0, "the device prop has no Fin0.");

            var mr = fin0.GetComponent<MeshRenderer>();
            var darkSteel = MaterialLibrary.Tinted(SurfaceKind.Metal, new Color(0.24f, 0.26f, 0.30f));
            Assert.AreSame(darkSteel, mr.sharedMaterial, "the fins should be DarkSteel, not the coils' ModuleRed.");

            Object.DestroyImmediate(root);
        }

        [Test]
        public void ExactlyTwoGlistenDots_AtTheDocumentedSizes()
        {
            var root = WeaponPartArt.BuildHydroDevice();
            var glisten0 = root.transform.Find(WeaponPartArt.GlistenPrefix + "0");
            var glisten1 = root.transform.Find(WeaponPartArt.GlistenPrefix + "1");
            var glisten2 = root.transform.Find(WeaponPartArt.GlistenPrefix + "2");

            Assert.IsNotNull(glisten0, "Glisten0 should still exist.");
            Assert.IsNotNull(glisten1, "Glisten1 should still exist.");
            Assert.IsNull(glisten2, "Glisten2 should have been dropped (MV-431 AC5).");

            Assert.AreEqual(0.07f, glisten0.localScale.x, 1e-4f, "Glisten0 should be sized 0.07.");
            Assert.AreEqual(0.06f, glisten1.localScale.x, 1e-4f, "Glisten1 should be sized 0.06.");

            Object.DestroyImmediate(root);
        }

        [Test]
        public void HydroDeviceGroundScale_Is2_AndTheLargestOfTheThree()
        {
            Assert.AreEqual(2.0f, WeaponPartArt.HydroDeviceGroundScale, 1e-4f,
                "the device's ground scale should be pinned to 2.0 (MV-431 AC2).");
            Assert.Greater(WeaponPartArt.HydroDeviceGroundScale, WeaponPartArt.PartGroundScale,
                "the device should read larger than a part.");
            Assert.Greater(WeaponPartArt.HydroDeviceGroundScale, WeaponPartArt.PowerCellGroundScale,
                "the device should read larger than a power cell.");
        }
    }
}
