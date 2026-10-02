using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// World 3's cover-dressing pass (MV-744) — the Reef counterpart of
    /// <see cref="BackyardDressing"/>'s own <c>DressCover</c>, called from
    /// <see cref="BackyardPath"/> instead of self-installing, since <see cref="BackyardPath.Awake"/>
    /// already knows which world it built and already runs the rest of the Reef-only cosmetic pass
    /// (<c>ApplyReefKit</c>) at the right point in the load order.
    ///
    /// A cover piece dressed "machinery" (<see cref="CoverDressing.Machinery"/>) reads as a coolant
    /// turret (<see cref="ReefKit.BuildCoolantTurret"/>) standing in the same footprint the cover
    /// block reserves — the same "collider stays, art swaps" contract every <c>DressCover</c> case
    /// keeps: the block's own box is still what stops the player, a robot or the boss. That suits the
    /// 2x2 machinery blocks World 1/2's kits authored, but MV-1030's V6 layout also authors machinery
    /// WALLS up to 18 m long; one 1 m turret standing at an 18 m wall's centre read as an invisible
    /// barrier either side of it (MV-1051), so a machinery piece wider than
    /// <see cref="MachineryTurretMaxSpan"/> on either horizontal axis keeps its own block visible
    /// instead, at its full authored footprint. A cover piece dressed "crate"
    /// (<see cref="CoverDressing.None"/>) keeps its own body — no turret stands in for it — but
    /// MV-1019 gives it its own Reef material (<see cref="ReefKit.ApplyCrateSkin"/>) instead of
    /// leaving it to <see cref="WorldMaterials"/>'s ordinary shape-classified sweep, which painted it
    /// darker than the floor it sits on.
    /// </summary>
    public static class ReefDressing
    {
        /// <summary>A machinery piece wider than this on either horizontal axis (MV-1051) keeps its
        /// own block visible instead of swapping for a turret: the V6 layout (MV-1030) authors
        /// machinery walls up to 18 m long, and a single 1 m-radius turret standing at the centre of
        /// an 18 m wall left an invisible collider either side of it — the "invisible barrier" Lee
        /// walked into in World 3 area 4. Pieces at or under this span still read fine as the turret
        /// prop the 2x2 blocks this was designed for.</summary>
        private const float MachineryTurretMaxSpan = 2.5f;

        /// <summary>Builds a coolant turret in place of every <see cref="CoverDressing.Machinery"/>
        /// cover piece at or under <see cref="MachineryTurretMaxSpan"/> on both horizontal axes
        /// (hiding that piece's own renderer); a machinery piece wider than that on either axis keeps
        /// its own block renderer visible instead, re-skinned with the same Reef machinery material,
        /// at its full authored footprint. Also re-skins every bare <see cref="CoverDressing.None"/>
        /// "crate" piece with its own Reef material (MV-1019). Returns how many turrets were placed —
        /// crates and oversized machinery pieces are re-skinned in place, not counted, since nothing is
        /// built or hidden for them.</summary>
        public static int DressCover(Transform parent, IReadOnlyList<CoverPiece> cover)
        {
            int placed = 0;
            if (cover == null) return placed;

            Transform props = null;

            foreach (CoverPiece piece in cover)
            {
                if (piece.Body == null) continue;

                if (piece.Cover.Dressing == CoverDressing.Machinery)
                {
                    Vector3 size = piece.Cover.Size;
                    if (size.x > MachineryTurretMaxSpan || size.z > MachineryTurretMaxSpan)
                    {
                        var longRenderer = piece.Body.GetComponent<Renderer>();
                        if (longRenderer != null) longRenderer.sharedMaterial = WorldMaterials.M_Circuit_Cyan;
                        if (piece.Body.GetComponent<KeepsOwnMaterial>() == null)
                            piece.Body.AddComponent<KeepsOwnMaterial>();
                        continue;
                    }

                    if (props == null)
                    {
                        // Lazily created: a Reef map with no machinery cover at all (there is none
                        // today) should leave no empty "dressed nothing" host behind.
                        props = new GameObject("Reef Props").transform;
                        props.SetParent(parent, false);
                        props.gameObject.AddComponent<KeepsOwnMaterial>();
                    }

                    ReefKit.BuildCoolantTurret(props, piece.Cover.Center, piece.Cover.Size.y);

                    var renderer = piece.Body.GetComponent<Renderer>();
                    if (renderer != null) renderer.enabled = false;

                    placed++;
                }
                else if (piece.Cover.Dressing == CoverDressing.None)
                {
                    ReefKit.ApplyCrateSkin(piece.Body, piece.Cover.Shape == CoverShape.Box);
                }
            }

            return placed;
        }
    }
}
