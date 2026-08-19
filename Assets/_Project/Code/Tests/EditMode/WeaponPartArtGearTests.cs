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
        /// <summary>MV-430 pinned the pool at one design (this test used to assert
        /// <c>Length == 1</c>). MV-454 restores the full ten now that every design carries its own
        /// colour accent instead of relying on shape alone — see <see cref="WeaponPartArtMachineInternalsPoolTests"/>
        /// for the coverage that supersedes this. Left as a smoke check that the pool didn't collapse
        /// back to one by accident.</summary>
        [Test]
        public void MachineInternalsKeys_HoldsAllTenDesigns()
        {
            Assert.AreEqual(10, WeaponPartArt.MachineInternalsKeys.Length,
                "the machine-internals pool should hold all ten designs (MV-454 restores MV-430's collapse).");
            Assert.Contains(WeaponPartArt.Keys.Gear, WeaponPartArt.MachineInternalsKeys);
        }

    }
}
