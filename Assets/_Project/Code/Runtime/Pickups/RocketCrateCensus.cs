using System.Collections.Generic;

namespace MaxWorlds.Pickups
{
    /// <summary>
    /// Which authored Rocket Crates (MV-1448) this run has collected, by map id - so a crate is collected
    /// once per run and a checkpoint restore brings it back as collected. Same scene-lifetime shape as
    /// <c>SecretCensus</c>: wiped when a level builds, persisted by <c>SaveSystem</c> checkpoint capture.
    /// </summary>
    public static class RocketCrateCensus
    {
        private static readonly HashSet<string> Collected = new HashSet<string>();

        public static void Reset() => Collected.Clear();

        public static void MarkCollected(string id)
        {
            if (!string.IsNullOrEmpty(id)) Collected.Add(id);
        }

        public static bool IsCollected(string id) => !string.IsNullOrEmpty(id) && Collected.Contains(id);

        public static string[] CollectedIds()
        {
            var ids = new string[Collected.Count];
            Collected.CopyTo(ids);
            return ids;
        }

        public static void ApplyCheckpointCollectedIds(IReadOnlyList<string> ids)
        {
            if (ids == null) return;
            foreach (string id in ids) MarkCollected(id);
        }
    }
}
