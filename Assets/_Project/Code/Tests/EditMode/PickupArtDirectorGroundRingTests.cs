using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-429 — the CollectibleGlow aura (an additive sphere 2.3-2.7x wider than the pickup it was meant
    /// to advertise, floating on the pickup's hover point rather than its mass) is gone, replaced by a
    /// flat <see cref="GroundRing"/> per pickup that stays pinned to the ground instead of riding the
    /// float/bob. Driven by reflection since <c>Build</c>/<c>DressGroundRing</c> are private static
    /// helpers with no other surface to exercise them through outside Play Mode (same idiom as
    /// <see cref="PickupArtDirectorScaleTests"/>).
    ///
    /// NOT covered here (needs real elapsed frames to observe, so it's PlayMode-only and out of scope
    /// per this project's "never author a PlayMode test" rule): the ring's alpha actually oscillating
    /// over time, and its Y staying flat across several seconds of the pickup's own bob rather than at
    /// one single sampled instant. Flagged in the MV-429 Jira comment for CI's PlayMode pass.
    /// </summary>
    public sealed class PickupArtDirectorGroundRingTests
    {
        private const string ArtPrefix = "PartArt:";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static Transform InvokeBuild(Pickup pickup, string key)
        {
            var build = typeof(PickupArtDirector).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(build, "PickupArtDirector.Build went missing");
            return (Transform)build.Invoke(null, new object[] { pickup, ArtPrefix + key });
        }

        private static void InvokeDressGroundRing(Transform pickup, PickupKind kind)
        {
            var method = typeof(PickupArtDirector).GetMethod("DressGroundRing", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "PickupArtDirector.DressGroundRing went missing");
            method.Invoke(null, new object[] { pickup, kind });
        }

        private static Color DeviceRingColor()
        {
            var field = typeof(PickupArtDirector).GetField("DeviceRingColor", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "PickupArtDirector.DeviceRingColor went missing");
            return (Color)field.GetValue(null);
        }

        /// <summary>Skips <see cref="Pickup.Create"/>'s greybox build, same reason
        /// <see cref="PickupArtDirectorScaleTests"/> does — <c>Build</c>/<c>DressGroundRing</c> only ever
        /// read <c>pickup.transform</c>, so the rest of a fully-built pickup is irrelevant here.</summary>
        private static Pickup BarePickup() => new GameObject("Pickup(Test)").AddComponent<Pickup>();

        private static Color RingColorAt(Transform ring)
        {
            var r = ring.GetComponent<MeshRenderer>();
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            return mpb.GetColor(BaseColorId);
        }

        [Test]
        public void PowerCell_BuiltArt_NoLongerCarriesTheCollectibleGlowSphere()
        {
            var pickup = BarePickup();

            InvokeBuild(pickup, WeaponPartArt.Keys.PowerCell);

            Assert.IsNull(pickup.transform.Find("CollectibleGlow"),
                "the power cell still wears the old aura sphere — MV-429 deletes it.");

            Object.DestroyImmediate(pickup.gameObject);
        }

        [Test]
        public void PowerCell_GetsAGroundRing_AtItsSpecifiedRadiusColourAndLift()
        {
            var pickup = BarePickup();

            InvokeDressGroundRing(pickup.transform, PickupKind.PowerCell);

            var ring = pickup.transform.Find("GroundRing");
            Assert.IsNotNull(ring, "the power cell got no ground ring.");
            Assert.AreEqual(0.50f, ring.localScale.x / 2f, 1e-4f, "wrong ring radius.");
            Assert.AreEqual(0.016f, ring.position.y, 1e-4f, "wrong ring lift.");

            Color c = RingColorAt(ring);
            Assert.AreEqual(WeaponPartArt.CellCyan.r, c.r, 1e-4f, "wrong ring colour (r).");
            Assert.AreEqual(WeaponPartArt.CellCyan.g, c.g, 1e-4f, "wrong ring colour (g).");
            Assert.AreEqual(WeaponPartArt.CellCyan.b, c.b, 1e-4f, "wrong ring colour (b).");
            Assert.GreaterOrEqual(c.a, 0.85f * 0.55f - 1e-4f, "ring alpha below its pulse floor.");
            Assert.LessOrEqual(c.a, 0.85f + 1e-4f, "ring alpha above its specified max.");

            Object.DestroyImmediate(pickup.gameObject);
        }

        [Test]
        public void Part_GetsAGroundRing_AtItsSpecifiedRadiusColourAndLift()
        {
            var pickup = BarePickup();

            InvokeDressGroundRing(pickup.transform, PickupKind.Part);

            var ring = pickup.transform.Find("GroundRing");
            Assert.IsNotNull(ring, "the part got no ground ring.");
            Assert.AreEqual(0.46f, ring.localScale.x / 2f, 1e-4f, "wrong ring radius.");
            Assert.AreEqual(0.016f, ring.position.y, 1e-4f, "wrong ring lift.");

            Color c = RingColorAt(ring);
            Assert.AreEqual(WeaponPartArt.Chrome.r, c.r, 1e-4f, "wrong ring colour (r).");
            Assert.AreEqual(WeaponPartArt.Chrome.g, c.g, 1e-4f, "wrong ring colour (g).");
            Assert.AreEqual(WeaponPartArt.Chrome.b, c.b, 1e-4f, "wrong ring colour (b).");
            Assert.GreaterOrEqual(c.a, 0.70f * 0.55f - 1e-4f, "ring alpha below its pulse floor.");
            Assert.LessOrEqual(c.a, 0.70f + 1e-4f, "ring alpha above its specified max.");

            Object.DestroyImmediate(pickup.gameObject);
        }

        [Test]
        public void Device_GetsTwoGroundRings_OuterAndInner_AtTheirSpecifiedRadiiAndLift()
        {
            var pickup = BarePickup();

            InvokeDressGroundRing(pickup.transform, PickupKind.Device);

            var outer = pickup.transform.Find("GroundRingOuter");
            var inner = pickup.transform.Find("GroundRingInner");
            Assert.IsNotNull(outer, "the device got no outer ring.");
            Assert.IsNotNull(inner, "the device got no inner ring.");

            Assert.AreEqual(0.68f, outer.localScale.x / 2f, 1e-4f, "wrong outer ring radius.");
            Assert.AreEqual(0.44f, inner.localScale.x / 2f, 1e-4f, "wrong inner ring radius.");
            Assert.AreEqual(0.016f, outer.position.y, 1e-4f, "wrong outer ring lift.");
            Assert.AreEqual(0.016f, inner.position.y, 1e-4f, "wrong inner ring lift.");

            Color red = DeviceRingColor();
            Color outerColor = RingColorAt(outer);
            Color innerColor = RingColorAt(inner);
            Assert.AreEqual(red.r, outerColor.r, 1e-4f, "outer ring isn't the device's red.");
            Assert.AreEqual(red.g, outerColor.g, 1e-4f, "outer ring isn't the device's red.");
            Assert.AreEqual(red.b, outerColor.b, 1e-4f, "outer ring isn't the device's red.");
            Assert.AreEqual(red.r, innerColor.r, 1e-4f, "inner ring isn't the device's red.");
            Assert.GreaterOrEqual(outerColor.a, 0.90f * 0.55f - 1e-4f, "outer ring alpha below its pulse floor.");
            Assert.LessOrEqual(outerColor.a, 0.90f + 1e-4f, "outer ring alpha above its specified max.");
            Assert.GreaterOrEqual(innerColor.a, 0.50f * 0.55f - 1e-4f, "inner ring alpha below its pulse floor.");
            Assert.LessOrEqual(innerColor.a, 0.50f + 1e-4f, "inner ring alpha above its specified max.");

            Object.DestroyImmediate(pickup.gameObject);
        }

        [Test]
        public void GroundRing_TracksThePickupsXZ_ButStaysPinnedToTheGroundPlane()
        {
            var pickup = BarePickup();
            pickup.transform.position = new Vector3(3f, 0.72f, -2f);   // mid-bob, well above the ground

            InvokeDressGroundRing(pickup.transform, PickupKind.PowerCell);

            var ring = pickup.transform.Find("GroundRing");
            Assert.IsNotNull(ring);
            Assert.AreEqual(3f, ring.position.x, 1e-4f, "ring didn't track the pickup's X.");
            Assert.AreEqual(-2f, ring.position.z, 1e-4f, "ring didn't track the pickup's Z.");
            Assert.AreEqual(0.016f, ring.position.y, 1e-4f,
                "the ring must stay pinned to the ground plane, not the pickup's own bobbed height.");

            Object.DestroyImmediate(pickup.gameObject);
        }
    }
}
