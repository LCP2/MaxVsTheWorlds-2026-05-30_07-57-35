using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;

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
    /// which HUD slot ("B"/"U") it occupies once forged, and its cell cost (MV-515: converted from
    /// parts — a Supercell is worth exactly 10 cells, so a fusion's old 3-part cost is now 30 cells).
    /// The layout twin, <see cref="MaxWorlds.UI.RigFusionLayout"/>, carries the same fields plus
    /// x/y/label for drawing; kept separate so <see cref="RigFusionState"/> (gameplay gating) never has
    /// to reach into the UI assembly for data it needs regardless of whether the board is even open.</summary>
    public sealed class RigFusionDef
    {
        public readonly string Id;
        public readonly string ParentA;
        public readonly string ParentB;
        public readonly string HudSlot;
        public readonly int CellCost;

        public RigFusionDef(string id, string parentA, string parentB, string hudSlot, int cellCost)
        {
            Id = id;
            ParentA = parentA;
            ParentB = parentB;
            HudSlot = hudSlot;
            CellCost = cellCost;
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
        public int cellCost;
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
        /// <summary>The most candidates a single Morphing Module draw offers — schema 3 dropped this
        /// from the data file (it's prose only, model.rules' "Max 3, sampled without replacement"),
        /// so it's pinned here instead.</summary>
        private const int DraftMaxCandidatesConst = 3;

        private static string s_worldResourcePath = RigBoardLibrary.ForWorld(0);

        /// <summary>MV-1287: which resource PRIMARY's nodes are read from — the active world's own file
        /// (<see cref="s_worldResourcePath"/>) unless <see cref="UseLoadout"/> pinned a different
        /// weapon's HOME board instead.</summary>
        private static string s_primaryResourcePath = s_worldResourcePath;

        /// <summary>MV-1287: same role as <see cref="s_primaryResourcePath"/>, for SECONDARY.</summary>
        private static string s_secondaryResourcePath = s_worldResourcePath;

        private static int s_activeWorldIndex;

        private static Dictionary<string, RigNodeDef> s_nodes;
        private static string[] s_allIds;
        private static string[] s_allCategoryIds;
        private static Dictionary<string, RigFusionDef> s_fusions;
        private static RigFusionDef[] s_allFusions;

        /// <summary>The world index THE RIG is currently reading from (MV-689) — 0 until
        /// <see cref="UseWorld"/> switches it.</summary>
        public static int ActiveWorldIndex => s_activeWorldIndex;

        /// <summary>Points THE RIG at <paramref name="worldIndex"/>'s own native board
        /// (<see cref="RigBoardLibrary.ForWorld"/>) — a no-op if it's already the active board. Forces a
        /// reload from Resources on the next access when the resource actually changes; every existing
        /// level/unlock in <see cref="RigState"/> is untouched here (the caller,
        /// <c>WeaponSystemState.ApplyWeaponCoreMorph</c>, owns reconciling state across the switch).</summary>
        public static void UseWorld(int worldIndex)
        {
            string path = RigBoardLibrary.ForWorld(worldIndex);
            ApplyResourcePaths(worldIndex, path, path, path);
        }

        /// <summary>MV-1287: points THE RIG at a loadout-composed board for <paramref name="worldIndex"/>
        /// — that world's own ENERGY/MOVE/SUPPORT, plus the PRIMARY category taken whole from
        /// <paramref name="primary"/>'s HOME board (<see cref="WorldCatalog.HomeWorldIndexForPrimary"/>)
        /// and the SECONDARY category taken whole from <paramref name="secondary"/>'s HOME board
        /// (<see cref="WorldCatalog.HomeWorldIndexForSecondary"/>). Only one PRIMARY and one SECONDARY
        /// category ever exist on the composed board, so ids never collide even though the same id
        /// means something different on another world's own file (<c>p_spr</c> is SPREAD on the Backyard
        /// board, SPLIT on the Reef's). <see cref="WeaponSystemState.ApplyWorldLoadout"/> calls this with
        /// the world's own native pair, so the native path (every existing caller) resolves to exactly
        /// the same board as before this ticket — composing a world's own file out of itself is a no-op
        /// by construction.</summary>
        public static void UseLoadout(int worldIndex, WeaponCatalog.PrimaryKind primary, SecondaryKind secondary)
        {
            string worldPath = RigBoardLibrary.ForWorld(worldIndex);
            string primaryPath = RigBoardLibrary.ForWorld(WorldCatalog.HomeWorldIndexForPrimary(primary));
            string secondaryPath = RigBoardLibrary.ForWorld(WorldCatalog.HomeWorldIndexForSecondary(secondary));
            ApplyResourcePaths(worldIndex, worldPath, primaryPath, secondaryPath);
        }

        private static void ApplyResourcePaths(int worldIndex, string worldPath, string primaryPath, string secondaryPath)
        {
            s_activeWorldIndex = worldIndex;
            if (worldPath == s_worldResourcePath && primaryPath == s_primaryResourcePath &&
                secondaryPath == s_secondaryResourcePath) return;
            s_worldResourcePath = worldPath;
            s_primaryResourcePath = primaryPath;
            s_secondaryResourcePath = secondaryPath;
            s_nodes = null;
        }

        private static RigBoardWire LoadWire(string resourcePath)
        {
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
            {
                Debug.LogError($"[RigBoard] no data at Resources/{resourcePath}.json");
                return null;
            }

            try
            {
                return JsonUtility.FromJson<RigBoardWire>(asset.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RigBoard] {resourcePath}.json is malformed: {e.Message}");
                return null;
            }
        }

        private static void EnsureLoaded()
        {
            if (s_nodes != null) return;

            s_nodes = new Dictionary<string, RigNodeDef>();
            s_allIds = Array.Empty<string>();
            s_allCategoryIds = Array.Empty<string>();
            s_fusions = new Dictionary<string, RigFusionDef>();
            s_allFusions = Array.Empty<RigFusionDef>();

            RigBoardWire worldWire = LoadWire(s_worldResourcePath);
            if (worldWire == null) return;

            RigBoardWire primaryWire = s_primaryResourcePath == s_worldResourcePath
                ? worldWire : LoadWire(s_primaryResourcePath);
            RigBoardWire secondaryWire = s_secondaryResourcePath == s_worldResourcePath
                ? worldWire
                : s_secondaryResourcePath == s_primaryResourcePath ? primaryWire : LoadWire(s_secondaryResourcePath);

            if (primaryWire == null || secondaryWire == null) return;

            // MV-1287: ENERGY/MOVE/SUPPORT (every category that isn't PRIMARY/SECONDARY) always come
            // from the active world's own file; PRIMARY comes whole from the equipped primary's HOME
            // board, SECONDARY whole from the equipped secondary's HOME board. For the native pairing
            // (every caller before this ticket) all three wires are the same object, so this reduces to
            // "every ability on the world's own file", unchanged — only the order of RigBoard.AllIds
            // moves (no caller depends on it; see the three-wire merge below), never the id set.
            var ids = new List<string>(worldWire.abilities.Length);
            foreach (RigAbilityWire a in worldWire.abilities)
            {
                if (a == null || string.IsNullOrEmpty(a.id)) continue;
                if (a.category == "PRIMARY" || a.category == "SECONDARY") continue;
                s_nodes[a.id] = new RigNodeDef(a.id, a.category, a.maxLevel, a.parent, a.startLevel);
                ids.Add(a.id);
            }
            foreach (RigAbilityWire a in primaryWire.abilities)
            {
                if (a == null || string.IsNullOrEmpty(a.id) || a.category != "PRIMARY") continue;
                s_nodes[a.id] = new RigNodeDef(a.id, a.category, a.maxLevel, a.parent, a.startLevel);
                ids.Add(a.id);
            }
            foreach (RigAbilityWire a in secondaryWire.abilities)
            {
                if (a == null || string.IsNullOrEmpty(a.id) || a.category != "SECONDARY") continue;
                s_nodes[a.id] = new RigNodeDef(a.id, a.category, a.maxLevel, a.parent, a.startLevel);
                ids.Add(a.id);
            }
            s_allIds = ids.ToArray();

            var categoryIds = new List<string>(worldWire.categories.Length);
            foreach (RigCategoryWire c in worldWire.categories)
                if (c != null && !string.IsNullOrEmpty(c.id)) categoryIds.Add(c.id);
            s_allCategoryIds = categoryIds.ToArray();

            // MV-1287 AC4: a FORGE fusion whose two parent categories aren't BOTH actually populated on
            // the composed board is hidden outright, never shown greyed/purchasable — every category on
            // every shipped board is populated today (so this never trips in practice), but a future
            // weapon kit whose home board omits a category it doesn't fit (never happens for the
            // standard ENERGY/MOVE/SUPPORT/PRIMARY/SECONDARY five today) must not leave a fusion offered
            // against a category with nothing in it.
            var populatedCategories = new HashSet<string>();
            foreach (RigNodeDef def in s_nodes.Values) populatedCategories.Add(def.Category);

            var fusions = new List<RigFusionDef>(worldWire.fusions.Length);
            foreach (RigFusionWire f in worldWire.fusions)
            {
                if (f == null || string.IsNullOrEmpty(f.id)) continue;
                if (!populatedCategories.Contains(f.parentA) || !populatedCategories.Contains(f.parentB)) continue;
                var def = new RigFusionDef(f.id, f.parentA, f.parentB, f.hudSlot, f.cellCost);
                s_fusions[f.id] = def;
                fusions.Add(def);
            }
            s_allFusions = fusions.ToArray();
        }

        /// <summary>Every ability id in the tree, in the JSON's own authored order — also the full
        /// pool a Morphing Module draft may draw from now that every ability shares the same gate
        /// (MV-436).</summary>
        public static IReadOnlyList<string> AllIds { get { EnsureLoaded(); return s_allIds; } }

        /// <summary>Every category id in the JSON's own authored order (<c>PRIMARY, SECONDARY, ENERGY,
        /// MOVE, SUPPORT</c>) — the full pool <see cref="RigDraft.DrawCandidateCategories"/> draws its
        /// shed offer from (MV-457).</summary>
        public static IReadOnlyList<string> AllCategoryIds { get { EnsureLoaded(); return s_allCategoryIds; } }

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

        /// <summary>Every node id mapped to its own max level on <paramref name="worldIndex"/>'s board,
        /// snapshotted without disturbing whichever board is currently active (<see cref="UseWorld"/>
        /// switches away and back around the read) — MV-856: the WORLD 2 dev entry point must cap
        /// ENERGY/MOVE/SUPPORT at World 1's own levels, not World 2's higher ones, so it needs a second
        /// board's data while World 2's is the one actually loaded. An id absent from
        /// <paramref name="worldIndex"/>'s board is simply absent from the result (never zero-filled) —
        /// the caller decides what "not on that board" means.</summary>
        public static IReadOnlyDictionary<string, int> SnapshotMaxLevels(int worldIndex)
        {
            int activeIndex = s_activeWorldIndex;
            UseWorld(worldIndex);
            var result = new Dictionary<string, int>();
            foreach (string id in AllIds)
                result[id] = MaxLevel(id);
            UseWorld(activeIndex);
            return result;
        }

        /// <summary>Reloads from Resources on the next access, back to World 1's board — test isolation
        /// only (a live build never needs this; MV-689 is the only thing that switches boards at
        /// runtime, and always explicitly via <see cref="UseWorld"/>/<see cref="UseLoadout"/>).</summary>
        public static void ResetForTests()
        {
            s_worldResourcePath = RigBoardLibrary.ForWorld(0);
            s_primaryResourcePath = s_worldResourcePath;
            s_secondaryResourcePath = s_worldResourcePath;
            s_activeWorldIndex = 0;
            s_nodes = null;
        }
    }
}
