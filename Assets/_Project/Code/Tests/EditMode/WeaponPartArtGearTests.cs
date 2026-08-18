using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-430 — the gear's teeth used to float 0.070 world units clear of the disc rim (a diameter, not
    /// a radius, treated as the disc's world radius — the same trap <see cref="WeaponPartArt.BuildPowerCell"/>'s
    /// CasingRadius doc already called out) and the machine-internals pool was ten designs that all
    /// collapsed to noise at the fixed 72° camera. This pins the fix: one design in the pool, its teeth
    /// seated against the disc with the ring radius derived from the disc's own scale, and a dark
    /// (not chrome) plinth on every part builder.
    /// </summary>
    public sealed class WeaponPartArtGearTests
    {
        [Test]
        public void MachineInternalsKeys_HoldsOnlyTheGear()
        {
            Assert.AreEqual(1, WeaponPartArt.MachineInternalsKeys.Length,
                "ten machine-internals designs read as noise, not variety, at the 72° camera (MV-430).");
            Assert.AreEqual(WeaponPartArt.Keys.Gear, WeaponPartArt.MachineInternalsKeys[0],
                "the sole remaining design should be the gear.");
        }

        [Test]
        public void Gear_TeethSitAgainstTheDiscRim_NoFloatingGap()
        {
            var root = WeaponPartArt.BuildGear();
            var disc = root.transform.Find("Disc");
            Assert.IsNotNull(disc, "the gear prop has no Disc.");
            float discRadius = disc.localScale.x * 0.5f;

            bool foundAnyTooth = false;
            for (int i = 0; i < 8; i++)
            {
                var tooth = root.transform.Find($"Tooth{i}");
                Assert.IsNotNull(tooth, $"the gear prop is missing Tooth{i}.");
                foundAnyTooth = true;

                Vector3 pos = tooth.localPosition;
                float radialDistance = new Vector2(pos.x, pos.z).magnitude;
                float innerFaceRadius = radialDistance - tooth.localScale.z * 0.5f;

                Assert.LessOrEqual(innerFaceRadius, discRadius + 1e-4f,
                    $"Tooth{i}'s inner face sits outside the disc rim — it'll read as a loose block orbiting the hub, not a tooth on the gear (MV-430).");
            }
            Assert.IsTrue(foundAnyTooth);

            Object.DestroyImmediate(root);
        }

        [Test]
        public void Gear_ToothRingRadius_IsDerivedFromTheDiscScale()
        {
            var root = WeaponPartArt.BuildGear();
            var disc = root.transform.Find("Disc");
            var tooth0 = root.transform.Find("Tooth0");
            Assert.IsNotNull(disc);
            Assert.IsNotNull(tooth0);

            // Tooth0 sits at angle 0 -> local +Z only, so its z-position IS the ring radius directly.
            Assert.AreEqual(disc.localScale.x * 0.5f, tooth0.localPosition.z, 1e-4f,
                "the tooth ring radius should track the disc's own scale, not a separate hardcoded literal (MV-430 AC3).");

            Object.DestroyImmediate(root);
        }

        [Test]
        public void PartPlinth_IsDarkSteel_NotChrome_AtTheNewRadius()
        {
            var root = WeaponPartArt.BuildGear();
            var plinth = root.transform.Find("Plinth");
            Assert.IsNotNull(plinth, "the gear prop has no Plinth.");

            var mr = plinth.GetComponent<MeshRenderer>();
            var darkSteel = MaterialLibrary.Tinted(SurfaceKind.Metal, new Color(0.24f, 0.26f, 0.30f));
            Assert.AreSame(darkSteel, mr.sharedMaterial,
                "the plinth should be dark steel, not chrome — chrome at radius 0.2 out-shone the part on top of it (MV-430).");
            Assert.AreEqual(0.22f, plinth.localScale.x, 1e-4f,
                "the plinth radius should have grown from 0.2 to 0.22 (MV-430).");

            Object.DestroyImmediate(root);
        }

        [Test]
        public void PartGroundScale_IsAnExplicitLiteral_NotDerivedFromThePowerCell()
        {
            Assert.AreEqual(1.8f, WeaponPartArt.PartGroundScale, 1e-4f,
                "the part's ground scale should be pinned to 1.8, no longer 2x the power cell's own scale (MV-430).");
        }
    }
}
