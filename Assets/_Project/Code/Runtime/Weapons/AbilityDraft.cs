using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The candidate draw for a destroyed shed's ability grant (MV-357, reversing WV-229's single
    /// random pick): up to <see cref="MaxCandidates"/> distinct abilities Max doesn't already own,
    /// sampled without replacement from <see cref="WeaponSystemState.Unacquired"/> so the player is
    /// never offered a duplicate and nothing is ever granted twice. Kept apart from
    /// <see cref="MaxWorlds.Pickups.PickupDirector"/> so the draw itself is EditMode-testable without
    /// a scene.
    /// </summary>
    public static class AbilityDraft
    {
        /// <summary>The most candidates a single draw offers — three is the shape MV-357 (and its
        /// MV-207 precursor) specified, fast enough to read as a choice rather than a chore.</summary>
        public const int MaxCandidates = 3;

        /// <summary>Up to <see cref="MaxCandidates"/> distinct unacquired abilities. Empty once every
        /// ability is owned; returns fewer than <see cref="MaxCandidates"/> as the pool runs low, and
        /// never repeats an entry within one draw.</summary>
        public static AbilityKind[] DrawCandidates()
        {
            var pool = new List<AbilityKind>(WeaponSystemState.Unacquired);
            int count = Mathf.Min(MaxCandidates, pool.Count);
            var picked = new AbilityKind[count];
            for (int i = 0; i < count; i++)
            {
                int roll = Random.Range(0, pool.Count);
                picked[i] = pool[roll];
                pool.RemoveAt(roll);
            }
            return picked;
        }
    }
}
