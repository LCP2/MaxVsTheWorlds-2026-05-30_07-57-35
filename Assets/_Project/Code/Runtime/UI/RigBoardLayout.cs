using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>One category hex — never owned, never levelled; the board lights it when any child
    /// ability is owned (MV-423).</summary>
    public sealed class RigCategoryLayout
    {
        public readonly string Id, Family, Icon;
        public readonly float X, Y;
        public RigCategoryLayout(string id, string family, string icon, float x, float y)
        {
            Id = id; Family = family; Icon = icon; X = x; Y = y;
        }
    }

    /// <summary>One ability node's UI placement — the layout twin of <see cref="MaxWorlds.Weapons.RigNodeDef"/>,
    /// which owns the model (kind/maxLevel/parent); this only carries what the board needs to draw it.</summary>
    public sealed class RigAbilityLayout
    {
        public readonly string Id, Category, Icon, Label, Kind, Parent;
        public readonly float X, Y;
        public readonly int MaxLevel;
        public RigAbilityLayout(string id, string category, string icon, string label, string kind,
            string parent, float x, float y, int maxLevel)
        {
            Id = id; Category = category; Icon = icon; Label = label; Kind = kind; Parent = parent;
            X = x; Y = y; MaxLevel = maxLevel;
        }
    }

    /// <summary>One FORGE fusion node — diamond-shaped, out of scope for MV-423 (5/5 gives it a real
    /// state machine); this ticket only has to place it and label it correctly.</summary>
    public sealed class RigFusionLayout
    {
        public readonly string Id, Label, ParentA, ParentB, HudSlot;
        public readonly float X, Y;
        public readonly int PartCost;
        public RigFusionLayout(string id, string label, string parentA, string parentB, string hudSlot,
            float x, float y, int partCost)
        {
            Id = id; Label = label; ParentA = parentA; ParentB = parentB; HudSlot = hudSlot;
            X = x; Y = y; PartCost = partCost;
        }
    }

    /// <summary>
    /// THE RIG board's UI-only geometry/colour/icon data (MV-423) — a second reader of the same
    /// <c>Assets/_Project/Resources/UI/rig_board.json</c> <see cref="MaxWorlds.Weapons.RigBoard"/>
    /// already loads for the model layer (MV-422). Kept separate on purpose: RigBoard's own doc
    /// comment is explicit that the model layer never touches geometry/colours/icons, so this class
    /// owns exactly the fields RigBoard ignores. <c>JsonUtility</c> has no dictionary support, so the
    /// fixed-key <c>colours</c> block maps onto named fields (<c>@base</c> for the reserved word) and
    /// the dynamic-key <c>icons</c> block is scanned by hand instead (see <see cref="ParseIcons"/>).
    /// </summary>
    public static class RigBoardLayout
    {
        private const string ResourcePath = "UI/rig_board";

        private static bool s_loaded;
        private static RigCategoryLayout[] s_categories = Array.Empty<RigCategoryLayout>();
        private static RigAbilityLayout[] s_abilities = Array.Empty<RigAbilityLayout>();
        private static RigFusionLayout[] s_fusions = Array.Empty<RigFusionLayout>();
        private static readonly Dictionary<string, string> s_icons = new Dictionary<string, string>();
        private static readonly Dictionary<string, Color> s_colours = new Dictionary<string, Color>();
        private static GeometryWire s_geometry;

        // ------------------------------------------------------------------ wire types (JsonUtility)

        [Serializable] private sealed class RowYWire { public float category, tier1, tier2, tier3, forge; }
        [Serializable] private sealed class RadiusWire { public float category, ability, fusion; }
        [Serializable] private sealed class PartBadgeOffsetWire { public string dx, dy; }
        [Serializable] private sealed class PartBadgeWire { public float radius; public PartBadgeOffsetWire offset; public float plusStrokeWidth; }
        [Serializable] private sealed class LevelPillWire { public float w, h, radius; public string offsetY; public float fontSize; }
        [Serializable] private sealed class RegionRectWire { public float y, h, radius, padX, opacityLit, opacityDark, borderAlphaLit, borderAlphaDark; }
        [Serializable] private sealed class ForgeDividerWire { public float y; }
        [Serializable] private sealed class CapMarkerOffsetWire { public string dx, dy; }
        [Serializable] private sealed class LockedFusionWire { public float borderAlpha, iconAlpha; }

        [Serializable]
        private sealed class ConnectorWire
        {
            public float width, controlBias, startOffsetCategory;
            public string startOffsetAbility, endOffset;
            public float alphaLive, alphaDim, fusionWidth, fusionAlpha, fusionAlphaLocked, fusionControlBias;
        }

        [Serializable]
        private sealed class GeometryWire
        {
            public RowYWire rowY;
            public RadiusWire radius;
            public float strokeOwned, strokeActive, strokeLocked;
            public float capOuterRingOffset, capMarkerRadius;
            public CapMarkerOffsetWire capMarkerOffset;
            public PartBadgeWire partBadge;
            public LevelPillWire levelPill;
            public string labelOffsetY;
            public float labelFontSize, labelLetterSpacing;
            public string categoryLabelOffsetY;
            public float categoryLabelFontSize;
            public RegionRectWire regionRect;
            public ForgeDividerWire forgeDivider;
            public float iconScaleAbility, iconScaleCategory, iconScaleFusion, iconOffsetY;
            public ConnectorWire connector;
            public LockedFusionWire lockedFusion;
            public float glowBlurOwned, glowAlphaOwned, glowBlurDraft, glowAlphaDraft;
            public float forgeCaptionFontSize, fusionSubFontSize;
            public float partsTraySubFontSizeMin, partsTraySubFontSizeMax;
            public float familyDimFactor;
        }

        [Serializable] private sealed class ColourEntryWire { public string hex; }

        [Serializable]
        private sealed class ColoursWire
        {
            public ColourEntryWire pri, sec, eng, mov, sup, part, module, ink;
            public ColourEntryWire @base;
        }

        [Serializable] private sealed class CategoryWire { public string id, family, icon; public float x; }

        [Serializable]
        private sealed class AbilityWire
        {
            public string id, category, icon, label, kind, parent;
            public float x, y;
            public int maxLevel;
        }

        [Serializable]
        private sealed class FusionWire
        {
            public string id, label, parentA, parentB, hudSlot;
            public float x, y;
            public int partCost;
        }

        [Serializable]
        private sealed class RigBoardUiWire
        {
            public GeometryWire geometry;
            public ColoursWire colours;
            public CategoryWire[] categories = Array.Empty<CategoryWire>();
            public AbilityWire[] abilities = Array.Empty<AbilityWire>();
            public FusionWire[] fusions = Array.Empty<FusionWire>();
        }

        // ------------------------------------------------------------------ loading

        private static void EnsureLoaded()
        {
            if (s_loaded) return;
            s_loaded = true;

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogError($"[RigBoardLayout] no data at Resources/{ResourcePath}.json");
                return;
            }

            RigBoardUiWire wire;
            try { wire = JsonUtility.FromJson<RigBoardUiWire>(asset.text); }
            catch (Exception e)
            {
                Debug.LogError($"[RigBoardLayout] rig_board.json is malformed: {e.Message}");
                return;
            }
            if (wire == null) return;

            s_geometry = wire.geometry ?? new GeometryWire();

            var categories = new List<RigCategoryLayout>(wire.categories.Length);
            float categoryY = s_geometry.rowY?.category ?? 0f;
            foreach (var c in wire.categories)
                if (c != null && !string.IsNullOrEmpty(c.id))
                    categories.Add(new RigCategoryLayout(c.id, c.family, c.icon, c.x, categoryY));
            s_categories = categories.ToArray();

            var abilities = new List<RigAbilityLayout>(wire.abilities.Length);
            foreach (var a in wire.abilities)
                if (a != null && !string.IsNullOrEmpty(a.id))
                    abilities.Add(new RigAbilityLayout(a.id, a.category, a.icon, a.label, a.kind, a.parent, a.x, a.y, a.maxLevel));
            s_abilities = abilities.ToArray();

            var fusions = new List<RigFusionLayout>(wire.fusions.Length);
            foreach (var f in wire.fusions)
                if (f != null && !string.IsNullOrEmpty(f.id))
                    fusions.Add(new RigFusionLayout(f.id, f.label, f.parentA, f.parentB, f.hudSlot, f.x, f.y, f.partCost));
            s_fusions = fusions.ToArray();

            s_colours.Clear();
            if (wire.colours != null)
            {
                AddColour("pri", wire.colours.pri);
                AddColour("sec", wire.colours.sec);
                AddColour("eng", wire.colours.eng);
                AddColour("mov", wire.colours.mov);
                AddColour("sup", wire.colours.sup);
                AddColour("part", wire.colours.part);
                AddColour("module", wire.colours.module);
                AddColour("ink", wire.colours.ink);
                AddColour("base", wire.colours.@base);
            }

            s_icons.Clear();
            foreach (var kv in ParseIcons(asset.text)) s_icons[kv.Key] = kv.Value;
        }

        private static void AddColour(string key, ColourEntryWire entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.hex)) return;
            if (ColorUtility.TryParseHtmlString(entry.hex, out var c)) s_colours[key] = c;
        }

        // ------------------------------------------------------------------ public reads

        public static IReadOnlyList<RigCategoryLayout> Categories { get { EnsureLoaded(); return s_categories; } }
        public static IReadOnlyList<RigAbilityLayout> Abilities { get { EnsureLoaded(); return s_abilities; } }
        public static IReadOnlyList<RigFusionLayout> Fusions { get { EnsureLoaded(); return s_fusions; } }

        /// <summary>The family id ("pri"/"sec"/...) for a category id ("PRIMARY"/"SECONDARY"/...) — an
        /// ability node only ever carries its parent category's id, not the family key
        /// <see cref="Colour"/> needs, so callers resolve through this rather than duplicating the
        /// category list's own lookup.</summary>
        public static string CategoryFamily(string categoryId)
        {
            EnsureLoaded();
            foreach (var c in s_categories)
                if (c.Id == categoryId) return c.Family;
            return null;
        }

        /// <summary>A node family's colour ("pri"/"sec"/"eng"/"mov"/"sup"), or <paramref name="part"/>/
        /// "module"/"ink"/"base" for the non-category accents. White if the id is unknown (should never
        /// happen against the real data file).</summary>
        public static Color Colour(string id)
        {
            EnsureLoaded();
            return s_colours.TryGetValue(id, out var c) ? c : Color.white;
        }

        public static string Icon(string id)
        {
            EnsureLoaded();
            return s_icons.TryGetValue(id, out var svg) ? svg : null;
        }

        public static float RadiusCategory { get { EnsureLoaded(); return s_geometry.radius?.category ?? 72f; } }
        public static float RadiusAbility { get { EnsureLoaded(); return s_geometry.radius?.ability ?? 50f; } }
        public static float RadiusFusion { get { EnsureLoaded(); return s_geometry.radius?.fusion ?? 40f; } }

        public static float ForgeDividerY { get { EnsureLoaded(); return s_geometry.forgeDivider?.y ?? 866f; } }

        public static float StrokeOwned { get { EnsureLoaded(); return s_geometry.strokeOwned; } }
        public static float StrokeActive { get { EnsureLoaded(); return s_geometry.strokeActive; } }
        public static float StrokeLocked { get { EnsureLoaded(); return s_geometry.strokeLocked; } }
        public static float CapOuterRingOffset { get { EnsureLoaded(); return s_geometry.capOuterRingOffset; } }
        public static float CapMarkerRadius { get { EnsureLoaded(); return s_geometry.capMarkerRadius; } }

        public static Vector2 CapMarkerOffset(float r)
        {
            EnsureLoaded();
            var o = s_geometry.capMarkerOffset;
            return o == null ? Vector2.zero : new Vector2(ResolveOffset(o.dx, r), ResolveOffset(o.dy, r));
        }

        public static float PartBadgeRadius { get { EnsureLoaded(); return s_geometry.partBadge?.radius ?? 15f; } }
        public static float PartBadgePlusStrokeWidth { get { EnsureLoaded(); return s_geometry.partBadge?.plusStrokeWidth ?? 4f; } }

        public static Vector2 PartBadgeOffset(float r)
        {
            EnsureLoaded();
            var o = s_geometry.partBadge?.offset;
            return o == null ? Vector2.zero : new Vector2(ResolveOffset(o.dx, r), ResolveOffset(o.dy, r));
        }

        public static float LevelPillW { get { EnsureLoaded(); return s_geometry.levelPill?.w ?? 62f; } }
        public static float LevelPillH { get { EnsureLoaded(); return s_geometry.levelPill?.h ?? 30f; } }
        public static float LevelPillRadius { get { EnsureLoaded(); return s_geometry.levelPill?.radius ?? 15f; } }
        public static float LevelPillFontSize { get { EnsureLoaded(); return s_geometry.levelPill?.fontSize ?? 17f; } }
        public static float LevelPillOffsetY(float r) { EnsureLoaded(); return ResolveOffset(s_geometry.levelPill?.offsetY, r); }

        public static float LabelOffsetY(float r) { EnsureLoaded(); return ResolveOffset(s_geometry.labelOffsetY, r); }
        public static float LabelFontSize { get { EnsureLoaded(); return s_geometry.labelFontSize; } }
        public static float CategoryLabelOffsetY(float r) { EnsureLoaded(); return ResolveOffset(s_geometry.categoryLabelOffsetY, r); }
        public static float CategoryLabelFontSize { get { EnsureLoaded(); return s_geometry.categoryLabelFontSize; } }

        public static float RegionRectY { get { EnsureLoaded(); return s_geometry.regionRect?.y ?? 150f; } }
        public static float RegionRectH { get { EnsureLoaded(); return s_geometry.regionRect?.h ?? 706f; } }
        public static float RegionRectRadius { get { EnsureLoaded(); return s_geometry.regionRect?.radius ?? 22f; } }
        public static float RegionRectPadX { get { EnsureLoaded(); return s_geometry.regionRect?.padX ?? 88f; } }
        public static float RegionOpacityLit { get { EnsureLoaded(); return s_geometry.regionRect?.opacityLit ?? 0.009f; } }
        public static float RegionOpacityDark { get { EnsureLoaded(); return s_geometry.regionRect?.opacityDark ?? 0.003f; } }
        public static float RegionBorderAlphaLit { get { EnsureLoaded(); return s_geometry.regionRect?.borderAlphaLit ?? 0.037f; } }
        public static float RegionBorderAlphaDark { get { EnsureLoaded(); return s_geometry.regionRect?.borderAlphaDark ?? 0.010f; } }

        public static float IconScaleAbility { get { EnsureLoaded(); return s_geometry.iconScaleAbility; } }
        public static float IconScaleCategory { get { EnsureLoaded(); return s_geometry.iconScaleCategory; } }
        public static float IconScaleFusion { get { EnsureLoaded(); return s_geometry.iconScaleFusion; } }
        public static float IconOffsetY { get { EnsureLoaded(); return s_geometry.iconOffsetY; } }

        // ------------------------------------------------------------------ connector (MV-443)

        public static float ConnectorWidth { get { EnsureLoaded(); return s_geometry.connector?.width ?? 2.6f; } }
        public static float ConnectorControlBias { get { EnsureLoaded(); return s_geometry.connector?.controlBias ?? 0.55f; } }
        public static float ConnectorStartOffsetCategory { get { EnsureLoaded(); return s_geometry.connector?.startOffsetCategory ?? 88f; } }
        public static float ConnectorAlphaLive { get { EnsureLoaded(); return s_geometry.connector?.alphaLive ?? 0.45f; } }
        public static float ConnectorAlphaDim { get { EnsureLoaded(); return s_geometry.connector?.alphaDim ?? 0.14f; } }
        public static float ConnectorFusionWidth { get { EnsureLoaded(); return s_geometry.connector?.fusionWidth ?? 2.0f; } }
        public static float ConnectorFusionAlpha { get { EnsureLoaded(); return s_geometry.connector?.fusionAlpha ?? 0.014f; } }
        public static float ConnectorFusionAlphaLocked { get { EnsureLoaded(); return s_geometry.connector?.fusionAlphaLocked ?? 0.004f; } }
        public static float ConnectorFusionControlBias { get { EnsureLoaded(); return s_geometry.connector?.fusionControlBias ?? 0.62f; } }

        // ------------------------------------------------------------------ locked fusion diamond (MV-445 defect 5)

        public static float LockedFusionBorderAlpha { get { EnsureLoaded(); return s_geometry.lockedFusion?.borderAlpha ?? 0.031f; } }
        public static float LockedFusionIconAlpha { get { EnsureLoaded(); return s_geometry.lockedFusion?.iconAlpha ?? 0.057f; } }

        // ------------------------------------------------------------------ node glow (MV-446)

        /// <summary>Blur width in px (texture space, 1:1 with on-screen px at 16:9) the owned/lit node
        /// halo fades across beyond the hexagon's own edge — see <see cref="MaxWorlds.UI.HudTextures.PolygonGlow"/>.</summary>
        public static float GlowBlurOwned { get { EnsureLoaded(); return s_geometry.glowBlurOwned > 0f ? s_geometry.glowBlurOwned : 14f; } }
        public static float GlowAlphaOwned { get { EnsureLoaded(); return s_geometry.glowAlphaOwned > 0f ? s_geometry.glowAlphaOwned : 0.45f; } }
        public static float GlowBlurDraft { get { EnsureLoaded(); return s_geometry.glowBlurDraft > 0f ? s_geometry.glowBlurDraft : 10f; } }
        public static float GlowAlphaDraft { get { EnsureLoaded(); return s_geometry.glowAlphaDraft > 0f ? s_geometry.glowAlphaDraft : 0.18f; } }

        // ------------------------------------------------------------------ small-type readability floor (MV-446 defect 3)

        /// <summary>16px floor: the smallest size any of THE RIG's small captions may render at on the
        /// 1920x1080 reference canvas (matches <see cref="LabelFontSize"/>, already used at that size for
        /// every node's own caption) — MV-446 defect 3 found the FORGE caption, fusion sub-captions and
        /// the PARTS tray's "N banked" line all sitting well under it (10-13px), muddy on a downscaled
        /// 6-inch screen.</summary>
        public static float ForgeCaptionFontSize { get { EnsureLoaded(); return s_geometry.forgeCaptionFontSize > 0f ? s_geometry.forgeCaptionFontSize : 18f; } }
        public static float FusionSubFontSize { get { EnsureLoaded(); return s_geometry.fusionSubFontSize > 0f ? s_geometry.fusionSubFontSize : 16f; } }
        public static float PartsTraySubFontSizeMin { get { EnsureLoaded(); return s_geometry.partsTraySubFontSizeMin > 0f ? s_geometry.partsTraySubFontSizeMin : 16f; } }
        public static float PartsTraySubFontSizeMax { get { EnsureLoaded(); return s_geometry.partsTraySubFontSizeMax > 0f ? s_geometry.partsTraySubFontSizeMax : 18f; } }

        // ------------------------------------------------------------------ family dim (MV-462 defect 3)

        /// <summary>An ability family with zero owned abilities recedes as one unit: every graphic under
        /// it — category node fill/stroke/icon/label/pill, every ability node's fill/stroke/icon/label/
        /// pill, every connector inside the family, and the region panel — has its alpha multiplied by
        /// this on top of whatever state-specific alpha it already carries (a locked node's already-faint
        /// treatment gets fainter still, not replaced).</summary>
        public static float FamilyDimFactor { get { EnsureLoaded(); return s_geometry.familyDimFactor > 0f ? s_geometry.familyDimFactor : 0.45f; } }

        /// <summary>"<c>+r*0.92</c>" / "<c>-r*0.92</c>" — a node-radius multiplier, the connector block's
        /// own offset shape (distinct from <see cref="ResolveOffset"/>'s additive "<c>+r-6</c>" terms).</summary>
        public static float ConnectorStartOffsetAbility(float r) { EnsureLoaded(); return ResolveMultiplier(s_geometry.connector?.startOffsetAbility, r); }
        public static float ConnectorEndOffset(float r) { EnsureLoaded(); return ResolveMultiplier(s_geometry.connector?.endOffset, r); }

        private static float ResolveMultiplier(string expr, float r)
        {
            if (string.IsNullOrEmpty(expr)) return 0f;
            var m = MultiplierRegex.Match(expr);
            if (!m.Success) return 0f;
            float value = r * float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return m.Groups[1].Value == "-" ? -value : value;
        }

        private static readonly Regex MultiplierRegex = new Regex(@"([+-])r\*(\d+(?:\.\d+)?)", RegexOptions.Compiled);

        /// <summary>Evaluates a tiny signed-term expression like <c>"+r-6"</c> or <c>"-r+15"</c> — the
        /// data file's only way to author an offset relative to a node's own radius without hardcoding
        /// it per node kind.</summary>
        public static float ResolveOffset(string expr, float r)
        {
            if (string.IsNullOrEmpty(expr)) return 0f;
            float total = 0f;
            foreach (Match m in OffsetTermRegex.Matches(expr))
            {
                bool negative = m.Groups[1].Value == "-";
                string term = m.Groups[2].Value;
                float value = term == "r" ? r : float.Parse(term, CultureInfo.InvariantCulture);
                total += negative ? -value : value;
            }
            return total;
        }

        private static readonly Regex OffsetTermRegex = new Regex(@"([+-])(r|\d+(?:\.\d+)?)", RegexOptions.Compiled);

        /// <summary>Hand-scans the raw JSON text for the <c>icons</c> block — its keys are icon ids
        /// (dynamic), which <c>JsonUtility</c> cannot map onto fields. Brace-depth tracked so the
        /// block's own closing <c>}</c> is found correctly; safe because none of the icon SVG strings
        /// contain a literal (unescaped) brace.</summary>
        private static Dictionary<string, string> ParseIcons(string json)
        {
            var result = new Dictionary<string, string>();
            int keyIdx = json.IndexOf("\"icons\"", StringComparison.Ordinal);
            if (keyIdx < 0) return result;
            int braceStart = json.IndexOf('{', keyIdx);
            if (braceStart < 0) return result;

            int depth = 0, braceEnd = -1;
            for (int i = braceStart; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0) { braceEnd = i; break; }
                }
            }
            if (braceEnd < 0) return result;

            string block = json.Substring(braceStart + 1, braceEnd - braceStart - 1);
            foreach (Match m in IconEntryRegex.Matches(block))
            {
                string id = m.Groups[1].Value;
                if (id == "$comment") continue;
                result[id] = m.Groups[2].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
            return result;
        }

        private static readonly Regex IconEntryRegex =
            new Regex("\"([\\w$]+)\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Compiled);

        /// <summary>Reloads from Resources on the next access — test isolation only.</summary>
        public static void ResetForTests() => s_loaded = false;
    }
}
