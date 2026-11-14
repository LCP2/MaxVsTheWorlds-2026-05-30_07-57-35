using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1360 — a dropped part on W4's teal deck was lost among rivets/hatch marks. The beacon's warm
    /// core must resolve (world-space bounds, material's resolved colour) to something readable.
    /// At the production camera (14.6 m, 40 deg lens) a 0.8 m core spans ~28 px on a 740 px-tall frame.
    /// </summary>
    public sealed class MV1360PartBeaconTests
    {
        [Test]
        public void PartBeaconCore_ResolvesToAWideSaturatedWarmDisc()
        {
            var pickup = new GameObject("Pickup(Test)");
            pickup.transform.localScale = Vector3.one;
            pickup.transform.position = new Vector3(3f, 0f, 0f);

            MeshRenderer core = PickupArtDirector.BuildPartBeacon(pickup.transform);

            Bounds b = core.bounds;
            Color c = core.sharedMaterial.GetColor("_BaseColor");
            float max = Mathf.Max(c.r, c.g, c.b), min = Mathf.Min(c.r, c.g, c.b);
            float luminance = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

            Object.DestroyImmediate(pickup);

            Assert.GreaterOrEqual(b.size.x, 0.8f, "core world-space diameter (m)");
            Assert.GreaterOrEqual(c.r, 0.99f, "core must be warm: red at full");
            Assert.GreaterOrEqual((max - min) / max, 0.6f, "core must be saturated");
            Assert.GreaterOrEqual(luminance, 0.65f, "core resolved luminance");
        }
    }
}
