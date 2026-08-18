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
        /// <summary>Up to <see cref="RigBoard.DraftMaxCandidates"/> distinct eligible cap ids. Empty
        /// once every cap is either owned or unreached; returns fewer than the max as the pool shrinks,
        /// and never repeats an id within one draw.</summary>
        public static string[] DrawCandidates()
        {
            var pool = new List<string>(RigState.EligibleCapIds());
            int count = Mathf.Min(RigBoard.DraftMaxCandidates, pool.Count);
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
