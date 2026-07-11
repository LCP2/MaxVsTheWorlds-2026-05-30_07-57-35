using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>YT-53 — the ground telegraph indicator.</summary>
    public sealed class TelegraphVfxTests
    {
        [Test]
        public void Ring_HasAMaterial_AndLiesFlatWithNoCollider()
        {
            var ring = GroundRing.Create("test-ring");
            try
            {
                var r = ring.GetComponent<MeshRenderer>();
                Assert.IsNotNull(r.sharedMaterial, "no material — the indicator would be invisible");

                ring.Show(new Vector3(3f, 0f, 4f), radius: 2f, color: Color.red);

                Assert.IsTrue(ring.Visible);
                Assert.That(ring.transform.position.y, Is.GreaterThan(0f),
                    "the ring must sit clear of the ground plane or it z-fights with it");
                Assert.That(ring.transform.position.x, Is.EqualTo(3f).Within(1e-4f));
                Assert.AreEqual(4f, ring.transform.localScale.x, 1e-4f, "scale should be diameter");
                Assert.IsNull(ring.GetComponent<Collider>(),
                    "a telegraph must never be something the player can collide with");

                ring.Hide();
                Assert.IsFalse(ring.Visible);
            }
            finally
            {
                Object.DestroyImmediate(ring.gameObject);
            }
        }

        [Test]
        public void RingTexture_HasABrightRim_AndADimInterior()
        {
            var tex = VfxMaterials.Ring(128);

            float centre = tex.GetPixel(64, 64).a;
            float rim = tex.GetPixel(64, 64 + 48).a;   // ~0.75 of the radius — in the rim band
            float boundary = tex.GetPixel(64, 127).a;  // right at the disc's edge
            float corner = tex.GetPixel(1, 1).a;       // outside the disc entirely

            Assert.That(rim, Is.GreaterThan(centre),
                "the rim must be brighter than the fill — a plain soft disc reads as a smudge on grass, " +
                "a hard edge reads as a boundary you can stand outside of");
            Assert.That(corner, Is.LessThan(0.02f), "outside the disc must be fully transparent");
            Assert.That(boundary, Is.LessThan(0.05f),
                "alpha must feather to nothing by the boundary — a hard cut there aliases badly");
        }
    }
}
