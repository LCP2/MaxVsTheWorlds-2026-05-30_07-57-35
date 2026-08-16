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
        /// never repeats an entry within one draw.
        ///
        /// MV-412: the very first draw of a run (nothing acquired yet) always seats
        /// <see cref="AbilityKind.ForceField"/> in one of its candidate slots instead of leaving it to
        /// the uniform roll below — Lee found it tended to land arbitrarily late. Every other slot,
        /// on this draw and every later one, is still filled by the same unweighted sample-without-
        /// replacement so no other ability's relative offer odds change.</summary>
        public static AbilityKind[] DrawCandidates()
        {
            var pool = new List<AbilityKind>(WeaponSystemState.Unacquired);
            int count = Mathf.Min(MaxCandidates, pool.Count);
            var picked = new AbilityKind[count];

            int next = 0;
            bool isFirstDraw = IsAcquiredCountZero();
            if (isFirstDraw && count > 0 && pool.Remove(AbilityKind.ForceField))
                picked[next++] = AbilityKind.ForceField;

            for (int i = next; i < count; i++)
            {
                int roll = Random.Range(0, pool.Count);
                picked[i] = pool[roll];
                pool.RemoveAt(roll);
            }
            return picked;
        }

        private static bool IsAcquiredCountZero()
        {
            foreach (var _ in WeaponSystemState.Acquired) return false;
            return true;
        }
    }
}
