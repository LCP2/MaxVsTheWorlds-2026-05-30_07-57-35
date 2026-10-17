using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Per-world persisted map zoom (MV-1206, Lee 17 Oct: "Could you have the map zoom remembered?
    /// Every time I go into the map, I have to keep on zooming in and in to find myself. Don't track
    /// where Max is, just keep the level of zoom.") — one PlayerPrefs float per world id
    /// (<see cref="MaxWorlds.Arena.WorldIds"/>), read by <see cref="MapScreen"/> on every
    /// <see cref="MapScreen.Open"/> and written on every <see cref="MapScreen.Close"/>. Deliberately
    /// does not cover pan (the ticket's own AC4 — only the zoom survives).
    /// </summary>
    public static class MapZoomMemory
    {
        private const string KeyPrefix = "map.zoom.";
        private const float DefaultZoom = 1f; // today's opening fit, unchanged when nothing is stored

        private static string KeyFor(string worldId) => KeyPrefix + worldId;

        /// <summary>The last zoom multiplier saved for this world, or <see cref="DefaultZoom"/> if
        /// nothing was ever saved.</summary>
        public static float Load(string worldId) => PlayerPrefs.GetFloat(KeyFor(worldId), DefaultZoom);

        public static void Save(string worldId, float zoomMultiplier)
        {
            PlayerPrefs.SetFloat(KeyFor(worldId), zoomMultiplier);
            PlayerPrefs.Save();
        }

        /// <summary>Test seam: deletes the saved value, so the next <see cref="Load"/> falls back to
        /// <see cref="DefaultZoom"/> — simulates "nothing stored yet" without restarting the process.</summary>
        public static void ClearForTest(string worldId) => PlayerPrefs.DeleteKey(KeyFor(worldId));
    }
}
