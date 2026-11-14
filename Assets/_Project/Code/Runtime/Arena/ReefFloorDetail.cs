using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// World 3's floor-detail pass: the MV-1350 deck, plus (MV-1256) the flooded-deck re-skin that replaces
    /// <see cref="MapRuntime"/>'s Stormdrain-shared acid-green sludge tint with the Reef's own
    /// dark-teal water for this biome only.
    ///
    /// Split the same way <see cref="ReefHydroponics"/> already keeps from <see cref="ReefKit"/>: WHERE
    /// a piece goes lives here (needs <see cref="MapData"/>/<see cref="WorldConfig"/>/cover — Arena/
    /// Gameplay-assembly types), WHAT gets built there is one of <see cref="ReefKit"/>'s own mesh/
    /// material-only methods — <c>MaxWorlds.Rendering.asmdef</c> references only Core and the render
    /// pipeline packages, never this assembly, so the dependency can only run this direction.
    /// </summary>
    public static class ReefFloorDetail
    {
        // ---- MV-1350: the deck ---------------------------------------------------------------------

        /// <summary>MV-1350: builds every area's riveted-plate deck, wear layer and wall-band props
        /// (<see cref="ReefDeckKit"/>), replacing MV-1339's big plain hull panels, grating patches and
        /// MV-1348's per-area caustics quads. One deck mesh, one wear mesh and 3-5 prop objects per
        /// area. The map's own floor slab is left in place: it still carries the corridors between
        /// areas, so it is not "fully covered" anywhere it could be hidden. Returns how many areas got a
        /// deck.</summary>
        public static int PlaceDeck(Transform host, WorldConfig cfg)
        {
            if (host == null || cfg?.areas == null) return 0;

            var root = new GameObject("Reef Deck");
            root.transform.SetParent(host, false);
            int count = 0;
            foreach (WorldArea area in cfg.areas)
            {
                if (area == null || area.size == null || area.size.w <= 0f || area.size.d <= 0f) continue;
                Transform areaRoot = new GameObject($"Deck {area.id}").transform;
                areaRoot.SetParent(root.transform, false);
                ReefDeckKit.BuildDeck(areaRoot, area.id, area.XMin, area.ZMin, area.XMax, area.ZMax);
                ReefDeckKit.BuildWear(areaRoot, area.id, area.XMin, area.ZMin, area.XMax, area.ZMax);
                ReefDeckKit.BuildProps(areaRoot, area.id, area.XMin, area.ZMin, area.XMax, area.ZMax);
                count++;
            }
            return count;
        }

        /// <summary>Stable, cross-platform string hash (FNV-1a) — same algorithm/reasoning as
        /// <c>ReefKit.StableHash</c> (a private member of a different assembly's class, so this is its
        /// own small copy rather than a cross-assembly dependency for one pure function).</summary>
        private static uint StableHash(string s)
        {
            uint hash = 2166136261u;
            if (s != null)
                foreach (char c in s)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
            return hash;
        }

        // ---- Change item 5: flooded-deck water re-skin ----------------------------------------------

        /// <summary>Slow UV drift for the Reef's own caustic-shimmer texture (MV-1256) — far slower than
        /// <c>MapRuntime.SludgeScrollSpeed</c> (0, 0.12): a flat flow current reads right for an open
        /// drain, but this is meant to read as barely-moving water.</summary>
        private static readonly Vector2 FloodShimmerScrollSpeed = new Vector2(0.015f, 0.025f);

        /// <summary>Re-skins every flooded-deck tile World 3 already built (MV-1256, Change item 5) —
        /// found by its own <see cref="SludgeFlow"/> component, the same "already tagged, just read it"
        /// idiom other systems (<c>DeckVisibility</c>, <c>BackyardWear</c>) already use for these same
        /// tiles rather than searching by name. The gameplay 0.6x slow
        /// (<c>WorldConfig.sludgeSpeedMultiplier</c>, <c>MapGeometry.SpeedMultiplierAt</c>) is driven
        /// entirely by the tile's own <see cref="MapEntity"/> footprint, never by this renderer or its
        /// material, so re-skinning the look here cannot touch it (ticket's own "do not re-raise").
        /// Each tile already carries <see cref="KeepsOwnMaterial"/> (<c>MapRuntime.Tint</c> added it
        /// when it first built the tile) so neither the generic biome sweep nor the runtime surface
        /// sweep can repaint it back — this only needs to guard the same invariant, not re-establish
        /// it.</summary>
        public static void DressFlood(Transform host)
        {
            if (host == null) return;

            foreach (SludgeFlow flow in host.GetComponentsInChildren<SludgeFlow>(true))
            {
                Renderer r = flow.GetComponent<Renderer>();
                if (r == null) continue;

                Material water = WorldMaterials.M_FloodWater;
                if (water == null) continue;

                r.sharedMaterial = water;
                if (r.gameObject.GetComponent<KeepsOwnMaterial>() == null) r.gameObject.AddComponent<KeepsOwnMaterial>();
                flow.Configure(r, water, FloodShimmerScrollSpeed);
            }
        }
    }
}
