using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Where world-config files live and how they are read (MV-270) — the <see cref="WorldConfig"/>
    /// counterpart to <see cref="MapLibrary"/>: JSON text assets under <c>Resources/Worlds/</c>, loaded
    /// by stable key, same reason (no Addressables package in the manifest). Kept separate from
    /// <see cref="MapLibrary"/> rather than merged into it — a world config carries the 8 dials and
    /// enemyTypes table a raw <see cref="MapData"/> has no fields for, and a caller that wants the
    /// origination/difficulty engines fed needs the <see cref="WorldConfig"/> itself, not just its
    /// geometry.
    /// </summary>
    public static class WorldLibrary
    {
        public const string ResourceRoot = "Worlds";

        /// <summary>World 1 — Backyard, LOCKED v1 (2026-08-05), the 0.6 milestone's playable world.</summary>
        public const string World1 = "world1_config";

        /// <summary>World 2 — Stormdrain (MV-687). Ships as a placeholder config until MV-700 lands
        /// the real level design.</summary>
        public const string World2 = "world2_config";

        /// <summary>Every world, in play order — index 0 is <see cref="World1"/>. What
        /// <see cref="MaxWorlds.Save.SaveSlotData.WorldIndex"/> counts against.</summary>
        public static readonly string[] Keys = { World1, World2 };

        /// <summary>How many worlds exist.</summary>
        public static int Count => Keys.Length;

        /// <summary>The world key for a 0-based world index, clamped to the last world once a save's
        /// index would otherwise run off the end of <see cref="Keys"/> (there is no world after the
        /// last one to advance into).</summary>
        public static string KeyForIndex(int worldIndex) => Keys[Mathf.Clamp(worldIndex, 0, Keys.Length - 1)];

        /// <summary>Load a world config by key. Returns null and logs if it is missing, unparseable, or
        /// fails validation — the caller decides what a missing/broken world means; here it is never
        /// silently papered over.</summary>
        public static WorldConfig Load(string key)
        {
            var asset = Resources.Load<TextAsset>($"{ResourceRoot}/{key}");
            if (asset == null)
            {
                Debug.LogError($"[WorldLibrary] no world '{key}' at Resources/{ResourceRoot}/{key}.json");
                return null;
            }

            if (!WorldConfigLoader.TryLoad(asset.text, out WorldConfig cfg, out string reason))
            {
                Debug.LogError($"[WorldLibrary] world '{key}' did not load: {reason}");
                return null;
            }

            return cfg;
        }
    }
}
