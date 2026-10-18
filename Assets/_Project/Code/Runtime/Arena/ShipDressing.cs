using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// The Mothership's own cover-dressing pass (MV-1230) — the Ship counterpart of
    /// <see cref="ReefDressing"/>/<see cref="CityDressing"/>, called from
    /// <see cref="BackyardPath.ApplyWorldMaterials"/> for <see cref="WorldKit.Ship"/>. Same "hide the
    /// box, build the kit piece at its own footprint" contract every dressing pass in this namespace
    /// keeps: the cover entry's own collider is untouched, only its renderer is swapped out.
    ///
    /// Wall/floor dressing (<see cref="ShipKit.BuildBulkheadPanel"/>/<see cref="ShipKit.BuildDeckFloor"/>)
    /// is left for the Hangar Bay layout ticket (MV-1232) to call directly once real wall/floor geometry
    /// exists — this ticket's own one-area placeholder (MV-1225) has no real wall run worth dressing
    /// yet. Same order <see cref="CityKit"/> (MV-1175) shipped in: the kit landed a ticket ahead of
    /// <see cref="CityDressing"/> (MV-1177), which wired it into a real layout once one existed.
    /// </summary>
    public static class ShipDressing
    {
        /// <summary>The full Ship pass for a real layout (MV-1232, the Hangar Bay ticket this class's
        /// own header named as the one to call wall/floor dressing directly): cover dressing plus the
        /// ticket's own "launch-pad hazard marking" — everything <see cref="DressCover"/> alone left
        /// out. Wall dressing is still left for a later ticket; a plain structural wall reads fine at
        /// this camera angle and the tie-breaker order (readability over visual richness) does not
        /// need it to ship this one.</summary>
        public static int Dress(Transform host, WorldConfig cfg, IReadOnlyList<CoverPiece> cover)
        {
            int dressed = DressCover(host, cover);
            BuildLaunchPad(host, cfg);
            return dressed;
        }

        /// <summary>MV-1232: the launch-pad hazard marking drawn in the ticket's own design image — a
        /// patch of deck floor (yellow hazard stripes along its own N/S edges, <see cref="ShipKit.BuildDeckFloor"/>)
        /// in the open ground between the two dropship covers and the drop-pod rack. Purely cosmetic
        /// (<see cref="ShipKit.BuildDeckFloor"/> strips its own colliders), so it can never affect
        /// walkability — only the Hangar Bay's own base floor does that.</summary>
        private static void BuildLaunchPad(Transform host, WorldConfig cfg)
        {
            WorldArea hangarBay = cfg?.Area("a1");
            if (hangarBay == null) return;

            var pad = new Rect(hangarBay.XMin + 20f, hangarBay.ZMin + 15f, 16f, 11f);
            ShipKit.BuildDeckFloor(host, pad);
        }

        /// <summary>Builds the matching <see cref="ShipKit"/> piece over every cover entry whose
        /// <see cref="ArenaCover.Dressing"/> this kit owns, and hides that entry's own box renderer —
        /// the collider itself is left exactly as <see cref="MaxWorlds.Arena.Map.MapRuntime.Build"/>
        /// built it. Returns how many pieces were dressed.</summary>
        public static int DressCover(Transform parent, IReadOnlyList<CoverPiece> cover)
        {
            int dressed = 0;
            if (cover == null) return dressed;

            Transform kit = null;
            foreach (CoverPiece piece in cover)
            {
                if (piece.Body == null) continue;

                GameObject art = BuildPiece(ref kit, parent, piece.Cover);
                if (art == null) continue; // unhandled dressing: the box stays visible

                var renderer = piece.Body.GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = false;
                dressed++;
            }

            return dressed;
        }

        private static GameObject BuildPiece(ref Transform kit, Transform parent, ArenaCover cover)
        {
            if (kit == null)
            {
                kit = new GameObject("Ship Kit").transform;
                kit.SetParent(parent, false);
                kit.gameObject.AddComponent<KeepsOwnMaterial>();
            }

            // ShipKit's own builders take a GROUND-level centre (Y=0) and add their own half-height,
            // the same convention CityKit's builders use (CityDressing.BuildCoverArt's own "at") —
            // cover.Center itself sits at the piece's true mid-height (ArenaCover.Center's own doc),
            // which is the OTHER, ReefKit-only convention, so it is deliberately not used here.
            Vector3 at = new Vector3(cover.CenterXz.x, 0f, cover.CenterXz.y);

            return cover.Dressing switch
            {
                CoverDressing.ShipDropship => ShipKit.BuildDropship(kit, at, cover.Size),
                CoverDressing.ShipDropPod => ShipKit.BuildDropPod(kit, at, cover.Size),
                CoverDressing.ShipCrate => ShipKit.BuildCrate(kit, at, cover.Size),
                CoverDressing.ShipCrateLow => ShipKit.BuildCrate(kit, at, cover.Size),
                CoverDressing.ShipGlassCase => ShipKit.BuildGlassCase(kit, at, cover.Size),
                CoverDressing.ShipConveyor => ShipKit.BuildConveyor(kit, at, cover.Size),
                CoverDressing.ShipConsoleBank => ShipKit.BuildConsoleBank(kit, at, cover.Size),
                CoverDressing.ShipBulkheadStub => ShipKit.BuildBulkheadStub(kit, at, cover.Size),
                _ => null,
            };
        }
    }
}
