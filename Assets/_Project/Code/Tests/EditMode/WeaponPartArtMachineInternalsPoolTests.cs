using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-454 — the machine-internals pool was collapsed to one grey-on-grey-on-grey design (MV-430).
    /// This restores all ten and gives every one of them a warm Brass/Copper accent so a part on grass
    /// reads as loot. Pins the two acceptance criteria that carry a testable shape:
    ///
    /// AC2 — every key in <see cref="WeaponPartArt.MachineInternalsKeys"/> builds a real prop (>=3
    /// renderers, no null material).
    ///
    /// AC3 — every design carries at least one non-neutral material. Proxied as "at least one child
    /// renderer wears the cached Brass or Copper material" rather than sampling a baked albedo texture
    /// for its dominant colour (the tone never lands in a queryable <c>_BaseColor</c> — see
    /// <c>MaterialLibrary.Build</c>, which bakes it into a texture and leaves <c>_BaseColor</c> as the
    /// biome tint) — Brass/Copper are the pool's only non-neutral materials post-fix, and
    /// <c>MaterialLibrary.Tinted</c> caches by colour, so a builder's material and this test's
    /// <c>MaterialLibrary.Tinted(SurfaceKind.Metal, WeaponPartArt.Brass)</c> call are reference-equal.
    /// This fails on f2aab92: the pool held only <c>Keys.Gear</c>, and the pre-fix gear wore Chrome,
    /// Steel and DarkSteel only — no Brass/Copper accent anywhere, so the "at least one" check finds
    /// nothing to match.
    /// </summary>
    public sealed class WeaponPartArtMachineInternalsPoolTests
    {
        [Test]
        public void EveryPoolKey_BuildsAPropWithAtLeastThreeRenderers_AndNoNullMaterials()
        {
            foreach (string key in WeaponPartArt.MachineInternalsKeys)
            {
                var root = WeaponPartArt.Build(key);
                Assert.IsNotNull(root, $"'{key}' built nothing.");

                var renderers = root.GetComponentsInChildren<MeshRenderer>();
                Assert.GreaterOrEqual(renderers.Length, 3,
                    $"'{key}' has fewer than three renderers — barely a prop, won't read as machine internals.");

                foreach (var r in renderers)
                    Assert.IsNotNull(r.sharedMaterial, $"'{key}/{r.name}' has a null material.");

                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EveryPoolDesign_CarriesAtLeastOneNonNeutralMaterial()
        {
            Material brass = MaterialLibrary.Tinted(SurfaceKind.Metal, WeaponPartArt.Brass);
            Material copper = MaterialLibrary.Tinted(SurfaceKind.Metal, WeaponPartArt.Copper);

            foreach (string key in WeaponPartArt.MachineInternalsKeys)
            {
                var root = WeaponPartArt.Build(key);
                var renderers = root.GetComponentsInChildren<MeshRenderer>();

                bool hasAccent = false;
                foreach (var r in renderers)
                {
                    if (r.sharedMaterial == brass || r.sharedMaterial == copper) { hasAccent = true; break; }
                }

                Assert.IsTrue(hasAccent,
                    $"'{key}' has no Brass/Copper accent — it'll read as another grey machine-internals design (MV-454).");

                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BrassAndCopper_AreNotNeutral()
        {
            // The literal AC3 shape: max(r,g,b) - min(r,g,b) > 0.15 on the accent colours themselves.
            AssertSpreadExceeds(WeaponPartArt.Brass, 0.15f, nameof(WeaponPartArt.Brass));
            AssertSpreadExceeds(WeaponPartArt.Copper, 0.15f, nameof(WeaponPartArt.Copper));
        }

        private static void AssertSpreadExceeds(Color c, float threshold, string name)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            Assert.Greater(max - min, threshold, $"{name}'s channel spread should exceed {threshold}.");
        }
    }
}
