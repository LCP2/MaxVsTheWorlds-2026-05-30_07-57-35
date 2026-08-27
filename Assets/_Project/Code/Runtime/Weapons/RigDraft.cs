using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The Morphing Module candidate draw (MV-424, replacing MV-357's <c>AbilityDraft</c>, which sampled
    /// the old per-enum <see cref="WeaponSystemState.Unacquired"/> pool with no notion of "reached").
    /// Draws up to <see cref="MaxWorlds.Weapons.RigBoard.DraftMaxCandidates"/> distinct ids, sampled
    /// without replacement, from <see cref="RigState.EligibleCapIds"/> — caps only, reached, not owned
    /// (model.rules, verbatim). Kept apart from <see cref="MaxWorlds.Pickups.PickupDirector"/> so the
    /// draw itself is EditMode-testable without a scene.
    /// </summary>
    public static class RigDraft
    {
        /// <summary>The most candidates a single shed Morphing Module offers when drawing CATEGORIES.
        /// MV-595 dropped this from 2 to 1 (Lee, 2026-08-26: "force the player to unlock secondary,
        /// then energy, then move, then support, so left to right across the rig") — superseding
        /// MV-457's "a random choice of 2 still-locked families".</summary>
        public const int CategoryDraftMaxCandidates = 1;

        /// <summary>Up to <see cref="RigBoard.DraftMaxCandidates"/> distinct eligible cap ids. Empty
        /// once every cap is either owned or unreached; returns fewer than the max as the pool shrinks,
        /// and never repeats an id within one draw.</summary>
        public static string[] DrawCandidates() => Sample(RigState.EligibleCapIds(), RigBoard.DraftMaxCandidates);

        /// <summary>The single next-in-line LOCKED category id, in <see cref="RigBoard.AllCategoryIds"/>'s
        /// own authored (left-to-right) order — MV-595: no longer a sample of <see cref="CategoryDraftMaxCandidates"/>
        /// (there is nothing left to choose between at 1). Deliberately NOT routed through <see cref="Sample"/>:
        /// that helper's Fisher-Yates shuffle is exactly the randomness this method exists to not have, and
        /// <see cref="DrawCandidates"/> above still needs that same shuffle for its own (unrelated, node-level)
        /// draft, so <see cref="Sample"/> itself is left untouched rather than risk silently de-randomising
        /// both callers. Empty once every category is unlocked.</summary>
        public static string[] DrawCandidateCategories()
        {
            foreach (string category in RigState.LockedCategoryIds())
                return new[] { category };
            return new string[0];
        }

        private static string[] Sample(IEnumerable<string> eligible, int max)
        {
            var pool = new List<string>(eligible);
            int count = Mathf.Min(max, pool.Count);
            var picked = new string[count];

            for (int i = 0; i < count; i++)
            {
                int roll = Random.Range(i, pool.Count);
                (pool[i], pool[roll]) = (pool[roll], pool[i]);
                picked[i] = pool[i];
            }
            return picked;
        }
    }
}
