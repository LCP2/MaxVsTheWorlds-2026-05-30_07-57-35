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
    /// Floor dressing (<see cref="ShipKit.BuildDeckFloor"/>) landed with the Hangar Bay layout ticket
    /// (MV-1232) once real floor geometry existed. Wall dressing (<see cref="ShipKit.BuildBulkheadPanel"/>)
    /// landed later still, MV-1260, once every area's own walls existed to dress — see
    /// <see cref="ShipAreaKit.BuildWallDressing"/>. Same order <see cref="CityKit"/> (MV-1175) shipped
    /// in: the kit landed a ticket ahead of <see cref="CityDressing"/> (MV-1177), which wired it into a
    /// real layout once one existed.
    /// </summary>
    public static class ShipDressing
    {
        /// <summary>The full Ship pass for a real layout: cover dressing, the Hangar Bay's own
        /// "launch-pad hazard marking" (MV-1232), MV-1259's own generated atlas floor for every real
        /// combat area, and MV-1260's own wall dressing (panels, lamps, windows, robot racks, clutter,
        /// hull masses) for every room-facing wall face in all 30 areas.</summary>
        public static int Dress(Transform host, MapData map, WorldConfig cfg, IReadOnlyList<CoverPiece> cover)
        {
            int dressed = DressCover(host, cover);
            BuildFloors(host, map);
            BuildLaunchPad(host, cfg);

            // MV-1260: walls collected AFTER the passes above (not before) — a wall one of them already
            // touched carries KeepsOwnMaterial, which WorldMaterials.IsWorldSurface would wrongly
            // exclude (the same MV-1063 trap ReefKit.CollectStructuralWalls's own doc documents); its
            // tag-based collector sidesteps that regardless of which world built the wall.
            List<StructuralWall> walls = ReefKit.CollectStructuralWalls(host);
            float wallHeight = map != null ? map.wallHeight : MapData.DefaultWallHeight;
            ShipAreaKit.BuildWallDressing(host, cfg, walls, wallHeight);

            // MV-1261: one hero set piece per hero area (needs the cover/wall dressing above already
            // built, to find a conveyor/glass-case/console footprint or a resolved wall run to stand on)
            // plus a themed dressing pass for every other area.
            ShipSetPieces.Build(host, cfg, cover, walls, wallHeight);

            return dressed;
        }

        /// <summary>MV-1259: one <see cref="ShipKit.BuildAtlasFloor"/> per real combat area —
        /// <see cref="MapZone.level"/> == 0 (skips a deck overlay, which shares its floor zone's own
        /// footprint already) and <see cref="MapZone.AreaIndex"/> &gt; 0 (skips the entry stub, which
        /// never matches the "area&lt;N&gt;" id convention). <see cref="MothershipDecks.LookFor"/> is the
        /// one per-area lookup <see cref="ShipKit"/> itself cannot make (Rendering assembly, can't see
        /// Arena/Gameplay) — resolved here and handed down as a plain <see cref="Color"/>, same contract
        /// every other ShipKit builder keeps. Every area's floor is built under one shared root so a
        /// single <see cref="StaticBatchingUtility.Combine"/> call covers the whole batch, same pattern
        /// <see cref="MaxWorlds.Arena.BackyardBackdrop"/>/<see cref="MaxWorlds.Arena.BackyardDressing"/>
        /// already use for their own placed pieces.</summary>
        private static void BuildFloors(Transform host, MapData map)
        {
            if (map?.zones == null) return;

            var floors = new GameObject("Ship Floors").transform;
            floors.SetParent(host, false);

            var atlasFootprints = new List<Rect>();
            foreach (MapZone zone in map.zones)
            {
                if (zone == null || zone.level != 0) continue;
                int areaIndex = zone.AreaIndex;
                if (areaIndex <= 0) continue;

                Color seamGlow = MothershipDecks.LookFor(areaIndex).RimColor;
                ShipKit.BuildAtlasFloor(floors, zone.Footprint, areaIndex, seamGlow);
                atlasFootprints.Add(zone.Footprint);
            }

            StaticBatchingUtility.Combine(floors.gameObject);

            HideMapFloorUnderAtlas(host, atlasFootprints);
        }

        /// <summary>MV-1264: the single map-spanning "Map Floor" slab (<see cref="MapGeometry.Floor"/>)
        /// sits right under every atlas floor this pass just built, top-to-top within a millimetre on
        /// the base commit — on a real device the map floor's own flat <c>Palette.GroundBase</c> won
        /// the z-fight and every plate/seam/decal MV-1259 drew was invisible. Same "hide the box, keep
        /// its collider" contract <see cref="DressCover"/> already uses, applied here to whichever real
        /// <see cref="StructuralFloor"/>'s own XZ footprint overlaps a built atlas floor — never a blind
        /// "always disable the map floor", so a future area with no atlas floor over it (the entry stub)
        /// keeps its own ground visible.</summary>
        private static void HideMapFloorUnderAtlas(Transform host, List<Rect> atlasFootprints)
        {
            if (atlasFootprints.Count == 0) return;

            foreach (StructuralFloor floor in host.GetComponentsInChildren<StructuralFloor>(true))
            {
                var renderer = floor.GetComponent<Renderer>();
                if (renderer == null) continue;

                Bounds b = renderer.bounds;
                var floorXz = new Rect(b.min.x, b.min.z, b.size.x, b.size.z);

                bool overlaps = false;
                foreach (Rect atlas in atlasFootprints)
                {
                    if (floorXz.Overlaps(atlas)) { overlaps = true; break; }
                }
                if (!overlaps) continue;

                renderer.enabled = false;
            }
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
