using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Weapons
{
    /// <summary>A node's kind (MV-422, canonical data <c>rig_board.json</c>, model.rules): a
    /// <see cref="Cap"/> (capability) can only reach level 1 by being taken in a Morphing Module
    /// draft — parts can never unlock it. A <see cref="Stat"/> needs no draft; it becomes spendable
    /// the moment its parent is at level &gt;= 1, starting at level 0.</summary>
    public enum RigNodeKind { Stat, Cap }

    /// <summary>One node of THE RIG's ability tree — id, kind, level cap, category and parent, as
    /// authored in <c>rig_board.json</c>. Immutable; <see cref="RigState"/> is where a run's actual
    /// levels live.</summary>
    public sealed class RigNodeDef
    {
        public readonly string Id;
        public readonly string Category;
        public readonly RigNodeKind Kind;
        public readonly int MaxLevel;
        /// <summary>Null for a root node (no parent — always reached).</summary>
        public readonly string Parent;

        public RigNodeDef(string id, string category, RigNodeKind kind, int maxLevel, string parent)
        {
            Id = id;
            Category = category;
            Kind = kind;
            MaxLevel = maxLevel;
            Parent = parent;
        }
    }

    // --- JsonUtility wire types (MV-422) — only the "model" fields this ticket's model layer
    // actually reads; geometry/colours/icons/fusions exist in the JSON for a later UI ticket
    // (2/5) and are simply ignored by JsonUtility (unknown-to-this-type fields are skipped, not
    // an error). startLevels is a fixed, closed field the same way WorldEnemyTypes avoids a
    // dictionary (JsonUtility has no dictionary support) — a named field per currently-authored
    // start level, not a generic map.

    [Serializable]
    internal sealed class RigStartLevelsWire
    {
        public int p_dmg;
    }

    [Serializable]
    internal sealed class RigModelWire
    {
        public RigStartLevelsWire startLevels;
        public int draftMaxCandidates = 3;
    }

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
    }

    [Serializable]
    internal sealed class RigBoardWire
    {
        public RigModelWire model;
        public RigCategoryWire[] categories = Array.Empty<RigCategoryWire>();
        public RigAbilityWire[] abilities = Array.Empty<RigAbilityWire>();
    }

    /// <summary>
    /// THE RIG's canonical ability tree (MV-422) — loaded from <c>Assets/_Project/Resources/UI/rig_board.json</c>,
    /// a verbatim copy of the design board's own data file. This is the single source of truth for
    /// every node's id, kind, level cap, category and parent, replacing the five parallel enums
    /// (<see cref="WeaponTrackKind"/>, <see cref="WaterBalloonTrackKind"/>, the old
    /// <c>SentinelTrackKind</c>, <see cref="AbilityKind"/>, and <see cref="MaxWorlds.Pickups.PickupWallet"/>'s
    /// Cell Capacity track) used to define separately. <see cref="RigState"/> is where a run's
    /// actual per-node levels live; this class only ever describes the tree's fixed shape.
    /// </summary>
    public static class RigBoard
    {
        private const string ResourcePath = "UI/rig_board";

        private static Dictionary<string, RigNodeDef> s_nodes;
        private static string[] s_allIds;
        private static string[] s_capIds;
        private static int s_startPDmgLevel = 1;
        private static int s_draftMaxCandidates = 3;

        private static void EnsureLoaded()
        {
            if (s_nodes != null) return;

            s_nodes = new Dictionary<string, RigNodeDef>();
            s_capIds = Array.Empty<string>();
            s_allIds = Array.Empty<string>();

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

            s_startPDmgLevel = wire.model?.startLevels?.p_dmg ?? 0;
            s_draftMaxCandidates = wire.model != null ? wire.model.draftMaxCandidates : 3;

            var ids = new List<string>(wire.abilities.Length);
            var caps = new List<string>();
            foreach (RigAbilityWire a in wire.abilities)
            {
                if (a == null || string.IsNullOrEmpty(a.id)) continue;
                RigNodeKind kind = a.kind == "cap" ? RigNodeKind.Cap : RigNodeKind.Stat;
                var def = new RigNodeDef(a.id, a.category, kind, a.maxLevel, a.parent);
                s_nodes[a.id] = def;
                ids.Add(a.id);
                if (kind == RigNodeKind.Cap) caps.Add(a.id);
            }
            s_allIds = ids.ToArray();
            s_capIds = caps.ToArray();
        }

        /// <summary>Every ability id in the tree, in the JSON's own authored order.</summary>
        public static IReadOnlyList<string> AllIds { get { EnsureLoaded(); return s_allIds; } }

        /// <summary>Every <see cref="RigNodeKind.Cap"/> id — the only pool a Morphing Module draft
        /// ever draws from.</summary>
        public static IReadOnlyList<string> CapIds { get { EnsureLoaded(); return s_capIds; } }

        /// <summary>The most candidates a single Morphing Module draw offers (model.draftMaxCandidates).</summary>
        public static int DraftMaxCandidates { get { EnsureLoaded(); return s_draftMaxCandidates; } }

        /// <summary>Run-start level for <paramref name="id"/> — every node is 0 except whatever
        /// <c>model.startLevels</c> authors (today, only <c>p_dmg</c> at 1).</summary>
        public static int StartLevel(string id)
        {
            EnsureLoaded();
            return id == "p_dmg" ? s_startPDmgLevel : 0;
        }

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

        public static RigNodeKind Kind(string id) => Get(id)?.Kind ?? RigNodeKind.Stat;

        public static bool IsCap(string id) => Kind(id) == RigNodeKind.Cap;

        public static int MaxLevel(string id) => Get(id)?.MaxLevel ?? 0;

        /// <summary>Null for a root node — a node with no parent is always reached.</summary>
        public static string Parent(string id) => Get(id)?.Parent;

        public static string Category(string id) => Get(id)?.Category;

        /// <summary>Reloads from Resources on the next access — test isolation only (a live build
        /// never needs this; the tree never changes at runtime).</summary>
        public static void ResetForTests() => s_nodes = null;
    }
}
