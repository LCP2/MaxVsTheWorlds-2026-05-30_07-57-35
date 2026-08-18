using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Weapons
{
    /// <summary>One node of THE RIG's ability tree — id, level cap, category, parent and run-start
    /// level, as authored in <c>rig_board.json</c>. Immutable; <see cref="RigState"/> is where a
    /// run's actual levels live. Schema 3 (MV-436) retired the cap/stat split: every node is now
    /// unlocked the same way (a Morphing Module draft), so there is no kind to distinguish here
    /// any more — only <see cref="StartLevel"/> (today, 1 for <c>p_dmg</c>, 0 for everything else)
    /// tells a node apart from the rest at run start.</summary>
    public sealed class RigNodeDef
    {
        public readonly string Id;
        public readonly string Category;
        public readonly int MaxLevel;
        /// <summary>Null for a root node (no parent — always reached).</summary>
        public readonly string Parent;
        public readonly int StartLevel;

        public RigNodeDef(string id, string category, int maxLevel, string parent, int startLevel)
        {
            Id = id;
            Category = category;
            MaxLevel = maxLevel;
            Parent = parent;
            StartLevel = startLevel;
        }
    }

    /// <summary>One FORGE fusion's model-layer shape (MV-426, 5/5) — id, its two parent category ids,
    /// which HUD slot ("B"/"U") it occupies once forged, and its part cost. The layout twin,
    /// <see cref="MaxWorlds.UI.RigFusionLayout"/>, carries the same fields plus x/y/label for drawing;
    /// kept separate so <see cref="RigFusionState"/> (gameplay gating) never has to reach into the UI
    /// assembly for data it needs regardless of whether the board is even open.</summary>
    public sealed class RigFusionDef
    {
        public readonly string Id;
        public readonly string ParentA;
        public readonly string ParentB;
        public readonly string HudSlot;
        public readonly int PartCost;

        public RigFusionDef(string id, string parentA, string parentB, string hudSlot, int partCost)
        {
            Id = id;
            ParentA = parentA;
            ParentB = parentB;
            HudSlot = hudSlot;
            PartCost = partCost;
        }
    }

    // --- JsonUtility wire types — only the "model" fields this ticket's model layer actually
    // reads; geometry/colours/icons/fusions exist in the JSON for the UI layer (RigBoardLayout)
    // and are simply ignored by JsonUtility (unknown-to-this-type fields are skipped, not an
    // error). Schema 3 (MV-436) moved the run-start level onto each ability itself
    // (<c>startLevel</c>) and dropped both <c>model.startLevels</c> and
    // <c>model.draftMaxCandidates</c> — the max-candidates figure is now prose only
    // ("Max 3" in model.rules), so it's a compile-time constant here instead of a wire field.

    [Serializable]
    internal sealed class RigCategoryWire
    {
        public string id;
    }

    [Serializable]
    internal sealed class RigAbilityWire
    {
        public string id;
        public string category;
        public string kind;
        public int maxLevel;
        public string parent;
        public int startLevel;
    }

    [Serializable]
    internal sealed class RigFusionWire
    {
        public string id;
        public string parentA;
        public string parentB;
        public string hudSlot;
        public int partCost;
    }

    [Serializable]
    internal sealed class RigBoardWire
    {
        public RigCategoryWire[] categories = Array.Empty<RigCategoryWire>();
        public RigAbilityWire[] abilities = Array.Empty<RigAbilityWire>();
        public RigFusionWire[] fusions = Array.Empty<RigFusionWire>();
    }

    /// <summary>
    /// THE RIG's canonical ability tree — loaded from <c>Assets/_Project/Resources/UI/rig_board.json</c>,
    /// a verbatim copy of the design board's own data file. This is the single source of truth for
    /// every node's id, level cap, category, parent and run-start level, replacing the five
    /// parallel enums (<see cref="WeaponTrackKind"/>, <see cref="WaterBalloonTrackKind"/>, the old
    /// <c>SentinelTrackKind</c>, <see cref="AbilityKind"/>, and <see cref="MaxWorlds.Pickups.PickupWallet"/>'s
    /// Cell Capacity track) used to define separately. <see cref="RigState"/> is where a run's
    /// actual per-node levels live; this class only ever describes the tree's fixed shape. Schema 3
    /// (MV-436) retired the cap/stat split model.rules used to carry — every ability is unlocked
    /// the same way now (a Morphing Module draft), so this class no longer tracks a node "kind" at
    /// all.
    /// </summary>
    public static class RigBoard
    {
        private const string ResourcePath = "UI/rig_board";

        /// <summary>The most candidates a single Morphing Module draw offers — schema 3 dropped this
        /// from the data file (it's prose only, model.rules' "Max 3, sampled without replacement"),
        /// so it's pinned here instead.</summary>
        private const int DraftMaxCandidatesConst = 3;

        private static Dictionary<string, RigNodeDef> s_nodes;
        private static string[] s_allIds;
        private static Dictionary<string, RigFusionDef> s_fusions;
        private static RigFusionDef[] s_allFusions;

        private static void EnsureLoaded()
        {
            if (s_nodes != null) return;

            s_nodes = new Dictionary<string, RigNodeDef>();
            s_allIds = Array.Empty<string>();
            s_fusions = new Dictionary<string, RigFusionDef>();
            s_allFusions = Array.Empty<RigFusionDef>();

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogError($"[RigBoard] no data at Resources/{ResourcePath}.json");
                return;
            }

            RigBoardWire wire;
            try
            {
                wire = JsonUtility.FromJson<RigBoardWire>(asset.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RigBoard] rig_board.json is malformed: {e.Message}");
                return;
            }

            if (wire == null) return;

            var ids = new List<string>(wire.abilities.Length);
            foreach (RigAbilityWire a in wire.abilities)
            {
                if (a == null || string.IsNullOrEmpty(a.id)) continue;
                var def = new RigNodeDef(a.id, a.category, a.maxLevel, a.parent, a.startLevel);
                s_nodes[a.id] = def;
                ids.Add(a.id);
            }
            s_allIds = ids.ToArray();

            var fusions = new List<RigFusionDef>(wire.fusions.Length);
            foreach (RigFusionWire f in wire.fusions)
            {
                if (f == null || string.IsNullOrEmpty(f.id)) continue;
                var def = new RigFusionDef(f.id, f.parentA, f.parentB, f.hudSlot, f.partCost);
                s_fusions[f.id] = def;
                fusions.Add(def);
            }
            s_allFusions = fusions.ToArray();
        }

        /// <summary>Every ability id in the tree, in the JSON's own authored order — also the full
        /// pool a Morphing Module draft may draw from now that every ability shares the same gate
        /// (MV-436).</summary>
        public static IReadOnlyList<string> AllIds { get { EnsureLoaded(); return s_allIds; } }

        /// <summary>The most candidates a single Morphing Module draw offers.</summary>
        public static int DraftMaxCandidates => DraftMaxCandidatesConst;

        /// <summary>Run-start level for <paramref name="id"/> — every node is 0 except whatever its
        /// own <c>startLevel</c> authors (today, only <c>p_dmg</c> at 1).</summary>
        public static int StartLevel(string id) => Get(id)?.StartLevel ?? 0;

        public static bool TryGet(string id, out RigNodeDef def)
        {
            EnsureLoaded();
            return s_nodes.TryGetValue(id, out def);
        }

        public static RigNodeDef Get(string id)
        {
            EnsureLoaded();
            return s_nodes.TryGetValue(id, out var def) ? def : null;
        }

        public static bool Exists(string id) { EnsureLoaded(); return s_nodes.ContainsKey(id); }

        public static int MaxLevel(string id) => Get(id)?.MaxLevel ?? 0;

        /// <summary>Null for a root node — a node with no parent is always reached.</summary>
        public static string Parent(string id) => Get(id)?.Parent;

        public static string Category(string id) => Get(id)?.Category;

        /// <summary>Every FORGE fusion in the JSON's own authored order (MV-426).</summary>
        public static IReadOnlyList<RigFusionDef> Fusions { get { EnsureLoaded(); return s_allFusions; } }

        public static bool FusionExists(string id) { EnsureLoaded(); return s_fusions.ContainsKey(id); }

        public static bool TryGetFusion(string id, out RigFusionDef def)
        {
            EnsureLoaded();
            return s_fusions.TryGetValue(id, out def);
        }

        /// <summary>Reloads from Resources on the next access — test isolation only (a live build
        /// never needs this; the tree never changes at runtime).</summary>
        public static void ResetForTests() => s_nodes = null;
    }
}
