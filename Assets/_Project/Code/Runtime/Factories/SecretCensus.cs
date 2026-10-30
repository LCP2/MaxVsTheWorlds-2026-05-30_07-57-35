using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// How many secret caches (MV-1320) this run's loaded world authors, and how many have been FOUND —
    /// a secret's own Supercell collected, not merely its cover piece broken (see
    /// <see cref="DestructibleCover"/>'s own secret handling). Same scene-lifetime, single-active-world
    /// shape every other census in this codebase already keeps (<see cref="FactoryCensus"/>,
    /// <see cref="MaxWorlds.Bosses.BossCensus"/>) — a level loaded a second time counts its own secrets,
    /// not the previous level's (or the previous test's) ghosts.
    ///
    /// <see cref="Found(int)"/>/<see cref="Total(int)"/> both accept a world index purely to match the
    /// shape MV-1322's world results screen reads every other per-world stat through; since only one
    /// world is ever loaded in a scene at a time — the same assumption <see cref="FactoryCensus"/> and
    /// <see cref="MaxWorlds.Bosses.BossCensus"/> already make with no world parameter at all — the
    /// parameter is not used to filter: both always answer for whichever world is currently loaded.
    /// </summary>
    public static class SecretCensus
    {
        private static readonly HashSet<string> FoundIdsSet = new HashSet<string>(8);
        private static int _total;

        /// <summary>Wipe the census. Called when a level starts building (<c>MapRuntime.Build</c>), so a
        /// scene loaded a second time counts its own secrets, not the previous level's (or the previous
        /// test's) ghosts.</summary>
        public static void Reset()
        {
            FoundIdsSet.Clear();
            _total = 0;
        }

        /// <summary>Seed this level's authored secret count (MV-1320) — called once per map build, right
        /// after <see cref="Reset"/>, with the number of <c>secret</c> cover pieces the loaded world
        /// config actually resolved into the built map.</summary>
        public static void SeedTotal(int total) => _total = Mathf.Max(0, total);

        /// <summary>A secret's Supercell has been collected (MV-1320) — called by
        /// <see cref="MaxWorlds.Pickups.PickupDirector"/>'s own <c>Collect</c>. A no-op for a null/empty
        /// id, and idempotent for one already marked found.</summary>
        public static void MarkFound(string secretId)
        {
            if (!string.IsNullOrEmpty(secretId)) FoundIdsSet.Add(secretId);
        }

        /// <summary>Has this secret's cache already been collected (MV-1320) — what
        /// <see cref="DestructibleCover"/> checks before re-placing a cache on a RESUME, so a secret the
        /// player already found never drops a second one.</summary>
        public static bool IsFound(string secretId) =>
            !string.IsNullOrEmpty(secretId) && FoundIdsSet.Contains(secretId);

        /// <summary>How many secrets have been found this scene's lifetime — see the class doc comment
        /// for why <paramref name="worldIndex"/> is accepted but not used to filter.</summary>
        public static int Found(int worldIndex) => FoundIdsSet.Count;

        /// <summary>How many secrets this level authors — see <see cref="SeedTotal"/> and the class doc
        /// comment for why <paramref name="worldIndex"/> is accepted but not used to filter.</summary>
        public static int Total(int worldIndex) => _total;

        /// <summary>The stable ids (<see cref="DestructibleCover.Id"/>) of every secret FOUND this run
        /// (MV-1320) — what a mid-run checkpoint capture persists so a RESUME can restore the same set.
        /// Same shape as <see cref="RepairBay.UsedIds"/>.</summary>
        public static string[] FoundIds()
        {
            var ids = new string[FoundIdsSet.Count];
            FoundIdsSet.CopyTo(ids);
            return ids;
        }

        /// <summary>Re-applies a checkpoint's already-recorded FOUND set (MV-1320) — called by
        /// <see cref="MaxWorlds.Save.SaveSystem.RestoreCheckpoint"/> on RESUME, BEFORE
        /// <see cref="DestructibleCover.ApplyCheckpointDestroyedIds"/> re-applies that same checkpoint's
        /// broken-cover set, so a secret already found by then correctly refuses to re-place its cache.
        /// A no-op for an empty/null set.</summary>
        public static void ApplyCheckpointFoundIds(IReadOnlyList<string> foundIds)
        {
            if (foundIds == null || foundIds.Count == 0) return;
            foreach (string id in foundIds)
                if (!string.IsNullOrEmpty(id)) FoundIdsSet.Add(id);
        }
    }
}
