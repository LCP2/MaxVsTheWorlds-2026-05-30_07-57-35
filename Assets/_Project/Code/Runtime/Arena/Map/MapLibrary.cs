using System;
using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Where maps live and how they are read (YT-89). Maps are JSON text assets under
    /// <c>Resources/Maps/</c>, loaded by key — the same load-by-stable-key rule the model library
    /// follows, and for the same reason: Addressables is not in the manifest and adding a package is
    /// a guardrail.
    ///
    /// The file on disk is the ONLY source of truth. There is deliberately no code-built fallback
    /// map: a fallback is a second definition of the level that drifts from the first, and the
    /// EditMode tests load this same file, so a map that would not play cannot reach a build.
    /// </summary>
    public static class MapLibrary
    {
        public const string ResourceRoot = "Maps";

        /// <summary>The Backyard vertical slice (YT-38) — the first map expressed in this format.</summary>
        public const string BackyardSlice = "backyard_slice";

        /// <summary>Path of a map's JSON inside the project. Editor-side saving writes here; the
        /// runtime never touches the filesystem (there is none on WebGL).</summary>
        public static string AssetPath(string key) =>
            $"Assets/_Project/Resources/{ResourceRoot}/{key}.json";

        /// <summary>Load a map by key. Returns null and logs if it is missing or unparseable — the
        /// caller decides what a missing map means; here it is never silently papered over.</summary>
        public static MapData Load(string key)
        {
            var asset = Resources.Load<TextAsset>($"{ResourceRoot}/{key}");
            if (asset == null)
            {
                Debug.LogError($"[MapLibrary] no map '{key}' at Resources/{ResourceRoot}/{key}.json");
                return null;
            }

            MapData map = Parse(asset.text);
            if (map == null) Debug.LogError($"[MapLibrary] map '{key}' did not parse");
            return map;
        }

        public static MapData Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                MapData map = JsonUtility.FromJson<MapData>(json);
                return map == null ? null : Normalize(map);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MapLibrary] map JSON is malformed: {e.Message}");
                return null;
            }
        }

        public static string ToJson(MapData map) => JsonUtility.ToJson(map, prettyPrint: true);

        /// <summary>JsonUtility leaves an absent array null rather than empty. Every reader would then
        /// need a null check for "a map with no cover", so fix it once, here.</summary>
        private static MapData Normalize(MapData map)
        {
            map.zones ??= Array.Empty<MapZone>();
            map.links ??= Array.Empty<MapLink>();
            map.entities ??= Array.Empty<MapEntity>();
            return map;
        }

#if UNITY_EDITOR
        /// <summary>Write a map back to its JSON. Editor only — this is the authoring half of the
        /// loop (the map editor window saves through here), and there is no filesystem in a build.</summary>
        public static void Save(string key, MapData map)
        {
            string path = AssetPath(key);
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

            System.IO.File.WriteAllText(path, ToJson(map));
            UnityEditor.AssetDatabase.ImportAsset(path);
        }
#endif
    }
}
