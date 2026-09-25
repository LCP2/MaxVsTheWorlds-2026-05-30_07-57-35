using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// MV-944 — Lee's play of TestFlight 0.9.9: in areas 10-14 (ground floor + upper walkway), Max's
    /// weapons hit robots on the deck while he stood on the floor, and vice versa. Lee's rule: the two
    /// levels fight separately — targeting and damage apply only within the same level. One shared
    /// comparison (off <see cref="MapData.IsOnDeck"/>) so every site that picks or damages a target —
    /// primary auto-aim, ARC, rockets, launcher missiles, sentinel focus, turret/bolter/beam fire, melee
    /// lunges — resolves "same level" identically, rather than each re-deriving its own height test.
    /// </summary>
    public static class CombatLevel
    {
        /// <summary>True if <paramref name="a"/> and <paramref name="b"/> are on the same combat level
        /// (both floor, or both deck). Fails OPEN (true) with no live <paramref name="map"/> — an EditMode
        /// fixture or a scene with no world loaded must not silently break every targeting path that never
        /// built one, the same fail-open contract <see cref="MaxWorlds.Enemies.SludgePuddle"/>'s own floor
        /// gate already uses.</summary>
        public static bool SameLevel(MapData map, Vector3 a, Vector3 b)
        {
            if (map == null) return true;
            return map.IsOnDeck(a.x, a.y, a.z) == map.IsOnDeck(b.x, b.y, b.z);
        }
    }
}
