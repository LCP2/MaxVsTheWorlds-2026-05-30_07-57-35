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
        /// <summary>The most candidates a single shed Morphing Module offers when drawing CATEGORIES
        /// (MV-457) — deliberately smaller than <see cref="RigBoard.DraftMaxCandidates"/>'s node draw
        /// (Lee, 2026-08-19: "a random choice of 2 still-locked families").</summary>
        public const int CategoryDraftMaxCandidates = 2;

        /// <summary>Up to <see cref="RigBoard.DraftMaxCandidates"/> distinct eligible cap ids. Empty
        /// once every cap is either owned or unreached; returns fewer than the max as the pool shrinks,
        /// and never repeats an id within one draw.</summary>
        public static string[] DrawCandidates() => Sample(RigState.EligibleCapIds(), RigBoard.DraftMaxCandidates);

        /// <summary>Up to <see cref="CategoryDraftMaxCandidates"/> distinct LOCKED category ids (MV-457)
        /// — the shed's own family draft, replacing the old per-node draw at the shed itself (the
        /// per-node draw above still exists — the RIG board's own eventual cells-as-currency spend, not
        /// a shed pick, is what draws individual nodes from here on). Empty once every category is
        /// unlocked.</summary>
        public static string[] DrawCandidateCategories() => Sample(RigState.LockedCategoryIds(), CategoryDraftMaxCandidates);

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
