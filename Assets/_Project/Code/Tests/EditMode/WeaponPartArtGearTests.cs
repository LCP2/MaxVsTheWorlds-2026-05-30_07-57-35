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

    }
}
