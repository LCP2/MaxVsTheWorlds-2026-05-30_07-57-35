using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1103: World 3's deck floor read as a plain dark floor when Lee arrived from World 2 inside
    /// one running app, but came back correct after the app restarted and resumed into World 3 (same
    /// TestFlight build both times — confirmed by Lee, 2026-10-08).
    ///
    /// Root cause: <see cref="StylizedTextures.Clear"/> calls <c>Object.Destroy</c>, not
    /// <c>DestroyImmediate</c>, while the game is playing — so a swept texture stays non-null until
    /// the end of the frame. <see cref="WorldMaterials"/>'s Reef-cache cache-hit path used to decide
    /// whether to re-point a cached material's texture slots by checking whether the current map read
    /// back null. On a real scene reload into World 3, <c>BackyardPath.Awake</c> sets the Reef palette
    /// (clearing the deck's two textures) and then runs <c>ReefKit.DressHull</c> (which fetches the
    /// cached <see cref="WorldMaterials.M_ShipFloor"/>) in the SAME frame — so the null check never
    /// fired, the material kept pointing at the about-to-be-destroyed texture, and the floor went
    /// dark a moment later when that texture actually died. EditMode's own <c>DestroyImmediate</c>
    /// masked this, since there the null check always fires immediately — why MV-1103's first worker
    /// run could not reproduce it. The fix (see <see cref="WorldMaterials"/>) re-points unconditionally
    /// on every fetch instead of checking nullness, and <see cref="StylizedTextures.DestroyOverrideForTests"/>
    /// is the seam that lets this EditMode test simulate the deferred destroy a running player has.
    ///
    /// Fails on the commit before this ticket's fix: sub-check A's `_BaseMap` comes back destroyed,
    /// exactly as the ticket's own hypothesis predicted — see the fix comment on MV-1103 for the quoted
    /// failing run.
    ///
    /// NOT yet seen on a device — this is proven only via the seam above, in EditMode. See the fix
    /// comment for why.
    /// </summary>
    public sealed class MV1103DeckTexturesSurviveDeferredClearTests
    {
        [TearDown]
        public void TearDown() => StylizedTextures.DestroyOverrideForTests = null;

        [Test]
        public void ReefDeckAndOceanVoidTextures_SurviveAClearDeferredToEndOfFrame()
        {
            // --- a. THE ARRIVAL ORDER: clear-then-fetch in the same frame, destroy at "end of frame" ---
            var collected = new List<Texture2D>();
            StylizedTextures.DestroyOverrideForTests = collected.Add;

            MaterialLibrary.Palette = BiomePalette.Stormdrain;

            Material warm = WorldMaterials.M_ShipFloor;
            Assert.IsTrue(IsLive(warm.GetTexture("_BaseMap")),
                "precondition: the corridor's own warm-up must leave a live base map");
            Assert.IsTrue(IsLive(warm.GetTexture("_EmissionMap")),
                "precondition: the corridor's own warm-up must leave a live emission map");

            var host = new GameObject("MV1103 Arrival Probe");
            GameObject wmHost = null;
            try
            {
                GameObject floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floorGo.transform.SetParent(host.transform, false);
                floorGo.transform.localScale = new Vector3(10f, 0.2f, 10f);
                floorGo.AddComponent<StructuralFloor>();

                wmHost = new GameObject("WorldMaterials");
                var wm = wmHost.AddComponent<WorldMaterials>();
                wm.Apply(BiomePalette.Reef);
                ReefKit.DressHull(host.transform);

                // "End of frame": every texture StylizedTextures.Clear() swept this frame is actually
                // destroyed now, same as a running player's deferred Object.Destroy landing a moment
                // after the reload's own Awake already returned.
                foreach (Texture2D t in collected)
                    if (t != null) Object.DestroyImmediate(t);

                Material floorMat = floorGo.GetComponent<Renderer>().sharedMaterial;
                Texture baseMap = floorMat.GetTexture("_BaseMap");
                Texture emissionMap = floorMat.GetTexture("_EmissionMap");

                Assert.IsTrue(IsLive(baseMap),
                    "the area 1 floor's _BaseMap must be a LIVE texture after a same-frame Clear()+DressHull " +
                    "(MV-1103: this is the dark-floor-on-arrival defect)");
                Assert.AreSame(StylizedTextures.ReefDeckAlbedo(), baseMap,
                    "the floor's _BaseMap must be the SAME object StylizedTextures currently hands out for the " +
                    "deck albedo, not a stale reference to a swept one");

                Assert.IsTrue(IsLive(emissionMap),
                    "the area 1 floor's _EmissionMap must be a LIVE texture after a same-frame Clear()+DressHull");
                Assert.AreSame(StylizedTextures.ReefDeckEmission(), emissionMap,
                    "the floor's _EmissionMap must be the SAME object StylizedTextures currently hands out for the " +
                    "deck emission mask, not a stale reference to a swept one");

                // --- b. EVERY REEF MATERIAL: nothing reachable through WorldMaterials holds a texture
                // that was destroyed by this same sweep.
                AssertNoDestroyedTexture(WorldMaterials.M_ShipFloor, "M_ShipFloor");
                AssertNoDestroyedTexture(WorldMaterials.M_ShipWall, "M_ShipWall");
                AssertNoDestroyedTexture(WorldMaterials.M_Circuit_Cyan, "M_Circuit_Cyan");
                AssertNoDestroyedTexture(WorldMaterials.M_Circuit_Purple, "M_Circuit_Purple");
                AssertNoDestroyedTexture(WorldMaterials.M_BioGlow, "M_BioGlow");
                AssertNoDestroyedTexture(WorldMaterials.M_Hazard, "M_Hazard");
                AssertNoDestroyedTexture(WorldMaterials.M_MetalDark, "M_MetalDark");
                AssertNoDestroyedTexture(WorldMaterials.M_CrateBody, "M_CrateBody");
                AssertNoDestroyedTexture(WorldMaterials.M_CrateCap, "M_CrateCap");
                AssertNoDestroyedTexture(WorldMaterials.M_KelpGreen, "M_KelpGreen");
                AssertNoDestroyedTexture(WorldMaterials.M_KelpMagenta, "M_KelpMagenta");
                AssertNoDestroyedTexture(WorldMaterials.M_LampViolet, "M_LampViolet");
                AssertNoDestroyedTexture(WorldMaterials.M_GlassOcean, "M_GlassOcean");
                AssertNoDestroyedTexture(WorldMaterials.M_OceanVoid, "M_OceanVoid");
            }
            finally
            {
                Object.DestroyImmediate(host);
                if (wmHost != null) Object.DestroyImmediate(wmHost);
            }

            // --- c. IMMEDIATE DESTROY STILL WORKS: with the hook unset (ordinary EditMode
            // DestroyImmediate), the same palette change, Apply and DressHull still give live maps.
            StylizedTextures.DestroyOverrideForTests = null;

            var host2 = new GameObject("MV1103 Immediate-Destroy Probe");
            GameObject wmHost2 = null;
            try
            {
                GameObject floorGo2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floorGo2.transform.SetParent(host2.transform, false);
                floorGo2.transform.localScale = new Vector3(10f, 0.2f, 10f);
                floorGo2.AddComponent<StructuralFloor>();

                MaterialLibrary.Palette = BiomePalette.Stormdrain;
                Material warm2 = WorldMaterials.M_ShipFloor;
                Assert.IsTrue(IsLive(warm2.GetTexture("_BaseMap")),
                    "precondition: the warm-up must still produce a live base map with no hook set");

                wmHost2 = new GameObject("WorldMaterials");
                var wm2 = wmHost2.AddComponent<WorldMaterials>();
                wm2.Apply(BiomePalette.Reef);
                ReefKit.DressHull(host2.transform);

                Material floorMat2 = floorGo2.GetComponent<Renderer>().sharedMaterial;
                Assert.IsTrue(IsLive(floorMat2.GetTexture("_BaseMap")),
                    "with the hook unset (ordinary EditMode DestroyImmediate), the floor's _BaseMap must still be live");
                Assert.IsTrue(IsLive(floorMat2.GetTexture("_EmissionMap")),
                    "with the hook unset (ordinary EditMode DestroyImmediate), the floor's _EmissionMap must still be live");
            }
            finally
            {
                Object.DestroyImmediate(host2);
                if (wmHost2 != null) Object.DestroyImmediate(wmHost2);
            }
        }

        private static bool IsLive(Texture tex) => tex != null;

        private static void AssertNoDestroyedTexture(Material mat, string name)
        {
            Assert.IsNotNull(mat, $"{name} must resolve to a real material in this environment");
            foreach (string prop in new[] { "_BaseMap", "_MainTex", "_EmissionMap" })
            {
                if (!mat.HasProperty(prop)) continue;
                Texture tex = mat.GetTexture(prop);
                // Unity's == is overloaded so a destroyed Object already reads as null via IsLive;
                // this additionally distinguishes "destroyed" from "never assigned" so the failure
                // message is accurate either way.
                bool isDestroyed = !ReferenceEquals(tex, null) && tex == null;
                Assert.IsFalse(isDestroyed,
                    $"{name}'s {prop} must not be a destroyed texture left over from a deferred Clear() sweep");
            }
        }
    }
}
