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
    /// keeps: the block's own box is still what stops the player, a robot or the boss. A cover piece
    /// dressed "crate" (<see cref="CoverDressing.None"/>) keeps its own body — no turret stands in for
    /// it — but MV-1019 gives it its own Reef material (<see cref="ReefKit.ApplyCrateSkin"/>) instead
    /// of leaving it to <see cref="WorldMaterials"/>'s ordinary shape-classified sweep, which painted
    /// it darker than the floor it sits on.
    /// </summary>
    public static class ReefDressing
    {
        /// <summary>Builds a coolant turret in place of every <see cref="CoverDressing.Machinery"/>
        /// cover piece (hiding that piece's own renderer) and re-skins every bare
        /// <see cref="CoverDressing.None"/> "crate" piece with its own Reef material (MV-1019).
        /// Returns how many turrets were placed — crates are re-skinned in place, not counted, since
        /// nothing is built or hidden for them.</summary>
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
