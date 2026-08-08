using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-304: the power cell's swapped-in prop used to wear a neutral Steel casing, which read drab
    /// next to the equally-cyan greybox sphere (Pickup.CellColor) a just-spawned cell shows before the
    /// director dresses it. The casing now has to wear the cell's own CellCyan tone, not a neutral
    /// metal, so the prop is consistently cyan from spawn through collection.
    /// </summary>
    public sealed class WeaponPartArtTests
    {
        [Test]
        public void PowerCellCasing_IsTintedTheCellsOwnCyan()
        {
            var root = WeaponPartArt.BuildPowerCell();
            var casing = root.transform.Find("Casing");
            Assert.IsNotNull(casing, "the power cell prop has no Casing.");

            var mr = casing.GetComponent<MeshRenderer>();
            var expected = MaterialLibrary.Tinted(SurfaceKind.Metal, WeaponPartArt.CellCyan);
            Assert.AreSame(expected, mr.sharedMaterial,
                "the power cell casing isn't tinted the cell's own cyan — it'll read drab next to the core band.");

            Object.DestroyImmediate(root);
        }
    }
}
