using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Models
{
    /// <summary>
    /// The set-dressing props imported from the Kenney Nature Kit (YT-75), and the size each model
    /// actually is.
    ///
    /// Placement code needs to know how big a prop is BEFORE it loads one — that's what lets the
    /// dressing be generated as pure data and its invariants checked in an EditMode test without
    /// building a single GameObject (same trick as <c>BackyardCover</c>). So the kit's dimensions
    /// live here, measured from the source meshes, in kit units: a prop's world size is
    /// <see cref="Size"/> × the scale it's placed at.
    ///
    /// A hand-copied table would rot the first time a model is swapped, so it doesn't get to: the
    /// import tool re-centres every prop on its own bounds, and an EditMode test loads each prefab
    /// and asserts its real bounds still match the number here. Get one wrong and the build fails,
    /// rather than a fence quietly sinking into the ground.
    ///
    /// Keys are the kit's own file names, loaded through <see cref="ModelLibrary"/> under
    /// <see cref="Folder"/>.
    /// </summary>
    public static class PropCatalog
    {
        /// <summary>Sub-folder of ModelLibrary's Resources root that the kit prefabs live in.</summary>
        public const string Folder = "props";

        /// <summary>Under this height (in metres, after scaling) a prop is scenery you walk over —
        /// stepping stones, a dirt row — not something that can hide an enemy or block a sightline.
        /// Flat props are exempt from the placement rules that keep the fight space clear.</summary>
        public const float FlatHeight = 0.3f;

        // --- fence ---
        public const string FencePanel = "fence_planks";
        public const string FenceGate = "fence_gate";

        // --- trees ---
        public const string TreeDefault = "tree_default";
        public const string TreeOak = "tree_oak";
        public const string TreeFat = "tree_fat";
        public const string TreeSmall = "tree_small";
        public const string TreeThin = "tree_thin";

        // --- shrubs & flowers ---
        public const string Bush = "plant_bush";
        public const string BushDetailed = "plant_bushDetailed";
        public const string BushLarge = "plant_bushLarge";
        public const string BushSmall = "plant_bushSmall";
        public const string FlowerRedA = "flower_redA";
        public const string FlowerRedB = "flower_redB";
        public const string FlowerYellowA = "flower_yellowA";
        public const string FlowerPurpleA = "flower_purpleA";
        public const string FlowerPurpleC = "flower_purpleC";
        public const string Grass = "grass";
        public const string GrassLarge = "grass_large";
        public const string GrassLeafs = "grass_leafs";

        // --- garden ---
        public const string PotLarge = "pot_large";
        public const string PotSmall = "pot_small";
        public const string DirtRow = "crops_dirtRow";

        // --- ground detail ---
        public const string PathStone = "path_stone";
        public const string PathStoneCircle = "path_stoneCircle";
        public const string RockSmallA = "rock_smallA";
        public const string RockSmallB = "rock_smallB";
        public const string RockFlat = "rock_smallFlatA";

        // --- woodpile ---
        public const string Log = "log";
        public const string LogStack = "log_stack";
        public const string Stump = "stump_round";
        public const string Sign = "sign";

        private static readonly Dictionary<string, Vector3> Sizes = new Dictionary<string, Vector3>
        {
            { FencePanel,      new Vector3(1.000f, 0.345f, 0.096f) },
            { FenceGate,       new Vector3(1.000f, 0.345f, 0.070f) },

            { TreeDefault,     new Vector3(0.755f, 1.708f, 0.654f) },
            { TreeOak,         new Vector3(0.641f, 1.226f, 0.740f) },
            { TreeFat,         new Vector3(0.755f, 1.150f, 0.654f) },
            { TreeSmall,       new Vector3(0.355f, 1.110f, 0.409f) },
            { TreeThin,        new Vector3(0.680f, 1.490f, 0.617f) },

            { Bush,            new Vector3(0.396f, 0.244f, 0.396f) },
            { BushDetailed,    new Vector3(0.603f, 0.360f, 0.603f) },
            { BushLarge,       new Vector3(0.374f, 0.243f, 0.336f) },
            { BushSmall,       new Vector3(0.383f, 0.207f, 0.336f) },
            { FlowerRedA,      new Vector3(0.159f, 0.292f, 0.181f) },
            { FlowerRedB,      new Vector3(0.207f, 0.259f, 0.239f) },
            { FlowerYellowA,   new Vector3(0.159f, 0.193f, 0.181f) },
            { FlowerPurpleA,   new Vector3(0.159f, 0.242f, 0.181f) },
            { FlowerPurpleC,   new Vector3(0.181f, 0.182f, 0.181f) },
            { Grass,           new Vector3(0.381f, 0.254f, 0.392f) },
            { GrassLarge,      new Vector3(0.409f, 0.254f, 0.408f) },
            { GrassLeafs,      new Vector3(0.234f, 0.142f, 0.257f) },

            { PotLarge,        new Vector3(0.564f, 0.200f, 0.488f) },
            { PotSmall,        new Vector3(0.323f, 0.268f, 0.280f) },
            { DirtRow,         new Vector3(1.000f, 0.050f, 0.620f) },

            { PathStone,       new Vector3(1.000f, 0.050f, 0.577f) },
            { PathStoneCircle, new Vector3(0.932f, 0.050f, 0.877f) },
            { RockSmallA,      new Vector3(0.361f, 0.191f, 0.361f) },
            { RockSmallB,      new Vector3(0.361f, 0.177f, 0.361f) },
            { RockFlat,        new Vector3(0.496f, 0.065f, 0.430f) },

            { Log,             new Vector3(0.234f, 0.173f, 0.710f) },
            { LogStack,        new Vector3(0.425f, 0.346f, 0.710f) },
            { Stump,           new Vector3(0.321f, 0.206f, 0.371f) },
            { Sign,            new Vector3(0.300f, 0.409f, 0.070f) },
        };

        /// <summary>Every prop key the kit provides.</summary>
        public static IEnumerable<string> Keys => Sizes.Keys;

        public static int Count => Sizes.Count;

        public static bool Has(string key) => key != null && Sizes.ContainsKey(key);

        /// <summary>The model's size in kit units. Zero for an unknown key — callers treat that as
        /// "not a prop", which is how a typo shows up as a failed validation instead of a
        /// mysteriously misplaced object.</summary>
        public static Vector3 Size(string key) =>
            key != null && Sizes.TryGetValue(key, out var s) ? s : Vector3.zero;

        /// <summary>The <see cref="ModelLibrary"/> key for a prop.</summary>
        public static string ResourceKey(string key) => $"{Folder}/{key}";

        /// <summary>The scale that makes a prop <paramref name="metres"/> tall, keeping it
        /// proportional. Authoring dressing in metres beats authoring it in kit units — a "3.2 m
        /// tree" is a thing you can picture and a thing a test can check.</summary>
        public static Vector3 ScaleToHeight(string key, float metres)
        {
            float h = Size(key).y;
            if (h <= 0f) return Vector3.one;
            return Vector3.one * (metres / h);
        }
    }
}
