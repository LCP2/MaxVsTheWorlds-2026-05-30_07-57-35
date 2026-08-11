using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-316 — the power cell's real-art swap-in used to keep its authored geometry's scale as-is,
    /// which read too small in-arena. <see cref="PickupArtDirector"/>'s private <c>Build</c> now applies
    /// <see cref="WeaponPartArt.PowerCellGroundScale"/> on top of the authored geometry, the same idiom
    /// already proven for the Hydro device (WV-236). Driven by reflection since <c>Build</c> is a private
    /// static helper with no other surface to exercise it through outside Play Mode.
    /// </summary>
    public sealed class PickupArtDirectorScaleTests
    {
        private const string ArtPrefix = "PartArt:"; // mirrors PickupArtDirector's private ArtPrefix

        private static Transform InvokeBuild(Pickup pickup, string key)
        {
            var build = typeof(PickupArtDirector).GetMethod("Build",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(build, "PickupArtDirector.Build went missing");
            return (Transform)build.Invoke(null, new object[] { pickup, ArtPrefix + key });
        }

        /// <summary>A bare <see cref="Pickup"/> component, skipping <see cref="Pickup.Create"/>'s
        /// greybox build (it calls the delayed <c>Destroy</c> on a collider, which EditMode logs as an
        /// error and fails the test) — <c>Build</c> only ever reads <c>pickup.transform</c>, so the rest
        /// of a fully-built pickup is irrelevant here.</summary>
        private static Pickup BarePickup() => new GameObject("Pickup(Test)").AddComponent<Pickup>();

        [Test]
        public void PowerCell_BuiltArt_IsScaledUpFromItsAuthoredGeometry()
        {
            var pickup = BarePickup();

            var art = InvokeBuild(pickup, WeaponPartArt.Keys.PowerCell);

            Assert.IsNotNull(art, "the power cell prop failed to build");
            Assert.AreEqual(WeaponPartArt.PowerCellGroundScale, art.localScale.x, 1e-4f,
                "the power cell's ground art should be scaled up so it reads at a larger, more readable size (MV-316).");

            Object.DestroyImmediate(pickup.gameObject);
        }

        [Test]
        public void HydroDevice_BuiltArt_StillUsesItsOwnScale_NotThePowerCells()
        {
            var pickup = BarePickup();

            var art = InvokeBuild(pickup, WeaponPartArt.Keys.HydroDevice);

            Assert.IsNotNull(art, "the Hydro device prop failed to build");
            Assert.AreEqual(WeaponPartArt.HydroDeviceGroundScale, art.localScale.x, 1e-4f,
                "the power cell's new ground scale must not bleed into the Hydro device's own multiplier.");

            Object.DestroyImmediate(pickup.gameObject);
        }

        /// <summary>
        /// MV-326 — a dropped Part's machine-internals design had no ground multiplier at all, so it
        /// rendered at its authored size while the power cell above was already scaled up 1.4x — the
        /// part ended up SMALLER than the cell despite already having a distinct shape/colour, which is
        /// exactly the "look like two identical cells" confusion the ticket reports. <see
        /// cref="WeaponPartArt.PartGroundScale"/> fixes that; this asserts it applies and stays bigger
        /// than the cell's own multiplier so the AC's "and larger" holds for every part design, not just
        /// the one checked here.
        ///
        /// MV-347 — MV-326's fix only landed a 1.25x relative difference (1.75x vs the cell's 1.4x),
        /// still inside the noise at the fixed 72° camera. The AC now names a number: a part must be
        /// exactly 2x a cell's footprint. Asserting the ratio (not just "greater than") is what actually
        /// pins that down — a ratio that drifted back toward 1x would still pass a bare "greater" check.
        /// </summary>
        [Test]
        public void Part_BuiltArt_IsScaledUpAndLargerThanThePowerCell()
        {
            var pickup = BarePickup();

            var art = InvokeBuild(pickup, WeaponPartArt.Keys.Gear);

            Assert.IsNotNull(art, "the part prop failed to build");
            Assert.AreEqual(WeaponPartArt.PartGroundScale, art.localScale.x, 1e-4f,
                "a dropped part's ground art should be scaled up by its own multiplier (MV-326).");
            Assert.AreEqual(2f, WeaponPartArt.PartGroundScale / WeaponPartArt.PowerCellGroundScale, 1e-4f,
                "a part's ground footprint must be exactly 2x a power cell's, driven off the cell's own " +
                "scale so the ratio can't drift (MV-347 AC).");

            Object.DestroyImmediate(pickup.gameObject);
        }
    }
}
