using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.UI
{
    /// <summary>What the minimap draws for one area (MV-264): hidden until the player has been
    /// there, current while they are standing in it, visited once they have moved on. A one-way
    /// progression — the gated arena has no way back past a broken gate — so this is read straight
    /// off <see cref="AreaAccumulationDirector.CurrentArea"/> rather than tracked independently.</summary>
    public enum AreaVisibility { Hidden, Visited, Current }

    /// <summary>
    /// Pure fog-of-war maths for the HUD minimap (MV-264, reintroducing YT-217's minimap now that the
    /// v0.5 recut replaced "a bounded single garden" with a 10-area gated arena). No MonoBehaviour, no
    /// state of its own — <see cref="HudController"/> calls this off the real
    /// <see cref="AreaAccumulationDirector.CurrentArea"/>, the same live index MV-242 already wired.
    /// </summary>
    public static class MinimapModel
    {
        /// <summary>How many "area&lt;N&gt;" zones the map defines — 1-based, so also the highest
        /// valid area index. Everything else the map authors (the boss clearing, an unrecognised id)
        /// is not part of the strip, exactly as <see cref="AreaAccumulationDirector.AreaIndexOf"/>
        /// already decides for the population director. Never hardcoded, so a map with a different
        /// area count draws its own strip rather than being truncated or overrun by a fixed ten.</summary>
        public static int CountAreas(MapData map)
        {
            if (map?.zones == null) return 0;

            int max = 0;
            foreach (MapZone zone in map.zones)
            {
                if (zone == null) continue;
                int index = AreaAccumulationDirector.AreaIndexOf(zone.id);
                if (index > max) max = index;
            }
            return max;
        }

        /// <summary>One state per area, 1..<paramref name="totalAreas"/> in order. Areas below
        /// <paramref name="currentArea"/> are Visited — the gates behind Max do not reopen, so once
        /// passed they stay passed — the one Max is standing in is Current, and everything ahead is
        /// Hidden: the fog-of-war the ticket asks for.</summary>
        public static AreaVisibility[] BuildStates(int totalAreas, int currentArea)
        {
            if (totalAreas < 0) totalAreas = 0;

            var states = new AreaVisibility[totalAreas];
            for (int i = 0; i < totalAreas; i++)
            {
                int areaIndex = i + 1;
                states[i] = areaIndex < currentArea ? AreaVisibility.Visited
                    : areaIndex == currentArea ? AreaVisibility.Current
                    : AreaVisibility.Hidden;
            }
            return states;
        }
    }
}
