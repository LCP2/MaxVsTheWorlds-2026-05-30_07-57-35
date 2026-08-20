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

        /// <summary>MV-472: half the width of this family's own column, sized to its actual content
        /// (the widest sibling spread anywhere in its ability tree) rather than a uniform 1/5 share of
        /// the board — <see cref="RigBoardLayout"/>'s column-layout pass sets this; consumers building a
        /// region panel or checking for clipping at the board's outer edge use it instead of guessing
        /// a shared inter-category spacing (which is no longer uniform).</summary>
        public readonly float ColumnHalfWidth;

        public RigCategoryLayout(string id, string family, string icon, float x, float y, float columnHalfWidth)
        {
            Id = id; Family = family; Icon = icon; X = x; Y = y; ColumnHalfWidth = columnHalfWidth;
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

    /// <summary>One capture size the ui-screens harness shoots THE RIG board at (MV-463 Part 1) —
    /// read from <c>rig_board.json</c>'s own <c>captureAspects</c> list so the harness never hard-codes
    /// a shot size the game isn't actually played at again.</summary>
    public readonly struct RigCaptureAspect
    {
        public readonly string Name;
        public readonly int W, H;
        public RigCaptureAspect(string name, int w, int h) { Name = name; W = w; H = h; }
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
        private static RigCaptureAspect[] s_captureAspects = Array.Empty<RigCaptureAspect>();
        private static readonly Dictionary<string, string> s_icons = new Dictionary<string, string>();
        private static readonly Dictionary<string, Color> s_colours = new Dictionary<string, Color>();
        private static GeometryWire s_geometry;

        // MV-472: the raw, as-authored category/ability data (original x used only to sort siblings
        // left-to-right, y as the tier hint) — kept so both the standard column layout (below) and the
        // lazily-built phone layout can each derive their own positions from the same source topology
        // without re-parsing the JSON.
        private static CategoryWire[] s_rawCategories = Array.Empty<CategoryWire>();
        private static AbilityWire[] s_rawAbilities = Array.Empty<AbilityWire>();
        private static FusionWire[] s_rawFusions = Array.Empty<FusionWire>();

        private static bool s_phoneLoaded;
        private static RigCategoryLayout[] s_phoneCategories = Array.Empty<RigCategoryLayout>();
        private static RigAbilityLayout[] s_phoneAbilities = Array.Empty<RigAbilityLayout>();
        private static RigFusionLayout[] s_phoneFusions = Array.Empty<RigFusionLayout>();

        // ------------------------------------------------------------------ wire types (JsonUtility)

        [Serializable] private sealed class RowYWire { public float category, tier1, tier2, tier3, forge; }
        [Serializable] private sealed class RadiusWire { public float category, ability, fusion; }
        [Serializable] private sealed class PartSlotOffsetWire { public string dx, dy; }
        [Serializable] private sealed class PartSlotWire { public float radius; public PartSlotOffsetWire offset; }
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
            public PartSlotWire partSlot;
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

        [Serializable] private sealed class CaptureAspectWire { public string name; public int w, h; }

        [Serializable]
        private sealed class RigBoardUiWire
        {
            public GeometryWire geometry;
            public ColoursWire colours;
            public CategoryWire[] categories = Array.Empty<CategoryWire>();
            public AbilityWire[] abilities = Array.Empty<AbilityWire>();
            public FusionWire[] fusions = Array.Empty<FusionWire>();
            public CaptureAspectWire[] captureAspects = Array.Empty<CaptureAspectWire>();
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

            s_rawCategories = Array.FindAll(wire.categories, c => c != null && !string.IsNullOrEmpty(c.id));
            s_rawAbilities = Array.FindAll(wire.abilities, a => a != null && !string.IsNullOrEmpty(a.id));
            s_rawFusions = Array.FindAll(wire.fusions, f => f != null && !string.IsNullOrEmpty(f.id));

            float categoryY = s_geometry.rowY?.category ?? 0f;
            float[] standardRowY = { categoryY, s_geometry.rowY?.tier1 ?? 0f, s_geometry.rowY?.tier2 ?? 0f, s_geometry.rowY?.tier3 ?? 0f };
            float abR = s_geometry.radius?.ability ?? 50f;
            float catR = s_geometry.radius?.category ?? 72f;
            var standard = BuildColumnLayout(s_rawCategories, s_rawAbilities, s_rawFusions,
                abR, catR, 2f * abR + StandardNodeGap, StandardTargetWidth, standardRowY,
                s_geometry.rowY?.forge ?? 0f);
            s_categories = standard.Categories;
            s_abilities = standard.Abilities;
            s_fusions = standard.Fusions;

            var captureAspects = new List<RigCaptureAspect>(wire.captureAspects.Length);
            foreach (var a in wire.captureAspects)
                if (a != null && !string.IsNullOrEmpty(a.name) && a.w > 0 && a.h > 0)
                    captureAspects.Add(new RigCaptureAspect(a.name, a.w, a.h));
            s_captureAspects = captureAspects.ToArray();

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

        private static void EnsurePhoneLoaded()
        {
            EnsureLoaded();
            if (s_phoneLoaded) return;
            s_phoneLoaded = true;

            float[] phoneRowY = { CategoryYPhone, Tier1YPhone, Tier2YPhone, Tier3YPhone };
            var phone = BuildColumnLayout(s_rawCategories, s_rawAbilities, s_rawFusions,
                RadiusAbilityPhone, RadiusCategoryPhone, PhoneNodeSpacing, PhoneTargetWidth,
                phoneRowY, FusionYPhone);
            s_phoneCategories = phone.Categories;
            s_phoneAbilities = phone.Abilities;
            s_phoneFusions = phone.Fusions;
        }

        // ------------------------------------------------------------------ column layout (MV-472)
        //
        // MV-472 item 3: "lay the families out to their actual content, not a uniform grid" — the five
        // ability families hold 5/5/4/2/7 nodes and, pre-fix, got an equal 1/5 share of the board's
        // width regardless, so MOVE (2 nodes) sat mostly empty while SUPPORT (7 nodes, up to 3 siblings
        // per row) ran out of room and clipped off the right edge below ~1.4:1 aspect. This walks each
        // category's own ability tree (by PARENT, not by a shared row y — a tier's siblings can have
        // different parents, e.g. SUPPORT's u_mov/u_cst/u_slt each sit under a different tier2 parent)
        // and assigns every node a signed offset in ability-diameter-ish "spacing units" from its own
        // immediate parent, recursively — so a family's required half-width is simply the widest
        // absolute offset found anywhere in its tree, not a hand-tuned guess. Every column is then
        // scaled by the SAME factor to exactly fill <paramref name="targetWidth"/>, so a wide family
        // gets a wide column and a narrow one a narrow column, never a uniform share.
        private const float StandardNodeGap = 20f;

        /// <summary>Both modes leave this much clear board on each outer edge (PRIMARY's own left,
        /// SUPPORT's own right) instead of packing columns flush to the 1920-wide frame's own edges — a
        /// pure-background strip <c>UiScreensDirector</c>'s probe 6 (json x=20) samples to confirm
        /// nothing renders outside the board's own content, same idiom the old uniform-grid layout's own
        /// ~70px edge margin (250 - 360/2) already gave it.</summary>
        private const float OuterMargin = 40f;
        private const float StandardTargetWidth = 1920f - 2f * OuterMargin;

        /// <summary>MV-472: phone mode's own node spacing — a RAW (pre-scale) value. Two competing
        /// constraints bound it, in tension with each other:
        ///
        /// HEX clearance needs <c>scale &gt;= 1</c>. A sibling's own hex EDGE sits at
        /// <c>offset*finalSpacing +/- abilityRadius</c> — note <c>abilityRadius</c> is NOT multiplied by
        /// <c>scale</c> (node SIZE never scales, only POSITION does) — while its column's own half-width
        /// is <c>(offset*nodeSpacing+abilityRadius)*scale</c>. Working the two against each other, the
        /// nodeSpacing terms cancel and it reduces to <c>abilityRadius*scale &gt;= abilityRadius</c>, i.e.
        /// scale itself must clear 1.0, full stop — independent of nodeSpacing. Below that, a fixed-size
        /// hex literally cannot fit inside a column whose own width shrank by the same factor its
        /// position did, and it spills into the neighbouring family — exactly what a first pass at
        /// nodeSpacing=400 (scale ~0.64) did: ENERGY's own CELL STORAGE hex visibly overlapped MOVE's
        /// SPEED hex.
        ///
        /// LABEL clearance wants nodeSpacing (hence finalSpacing = nodeSpacing*scale) as big as possible,
        /// since best-fit shrinks each label into <see cref="PhoneLabelBoxWidth"/> but two adjacent boxes
        /// still must not overlap EACH OTHER.
        ///
        /// Because scale = targetWidth / (7*nodeSpacing + 640) for this 5-category dataset, these pull in
        /// opposite directions: bigger nodeSpacing raises finalSpacing but drops scale toward (and below)
        /// 1. 190 is the highest value that keeps scale comfortably above 1 (~1.11, a real margin, not a
        /// knife-edge) at <see cref="PhoneTargetWidth"/>'s current budget, landing finalSpacing ~211 —
        /// PhoneLabelBoxWidth is sized under that with room to spare.</summary>
        private const float PhoneNodeSpacing = 190f;

        /// <summary>Unlike the standard frame (fixed at 1920 by <c>WeaponsScreen</c>'s own reference
        /// canvas), phone mode never triggers <see cref="WeaponsScreen.ComputeBoardScale"/>'s width
        /// squeeze (phone aspects are always wider than 16:9) — so this is a FIXED width budget, not
        /// derived from the actual live aspect the way the standard frame's own 1920 is. It must therefore
        /// be safe at the NARROWEST aspect phone mode can ever select at,
        /// <see cref="WeaponsScreen.PhoneAspectThreshold"/> (2.10), not the wider real "phone"
        /// captureAspect (2.1667) — sizing it to the latter is exactly what put phone mode's own content
        /// wider than aspect 2.10's actual visible window, clipping PRIMARY's left edge (caught by this
        /// file's own EditMode coverage). visibleRefWidth at 2.10 is 1080*2.10 = 2268; minus the same
        /// outer margin both modes use.</summary>
        private const float PhoneTargetWidth = 2268f - 2f * OuterMargin;

        private sealed class ColumnLayoutResult
        {
            public RigCategoryLayout[] Categories;
            public RigAbilityLayout[] Abilities;
            public RigFusionLayout[] Fusions;
        }

        private static ColumnLayoutResult BuildColumnLayout(CategoryWire[] rawCategories, AbilityWire[] rawAbilities,
            FusionWire[] rawFusions, float abilityRadius, float categoryRadius, float nodeSpacing, float targetWidth,
            float[] rowY, float fusionY)
        {
            var abilitiesByCategory = new Dictionary<string, List<AbilityWire>>();
            foreach (var cat in rawCategories) abilitiesByCategory[cat.id] = new List<AbilityWire>();
            foreach (var ab in rawAbilities)
                if (abilitiesByCategory.TryGetValue(ab.category, out var list)) list.Add(ab);

            var offsetUnits = new Dictionary<string, float>();
            var depthOf = new Dictionary<string, int>();
            var rawHalfWidth = new Dictionary<string, float>();

            foreach (var cat in rawCategories)
            {
                var byParent = new Dictionary<string, List<AbilityWire>>();
                foreach (var ab in abilitiesByCategory[cat.id])
                {
                    string key = string.IsNullOrEmpty(ab.parent) ? "" : ab.parent;
                    if (!byParent.TryGetValue(key, out var list)) byParent[key] = list = new List<AbilityWire>();
                    list.Add(ab);
                }
                foreach (var list in byParent.Values) list.Sort((a, b) => a.x.CompareTo(b.x));

                float maxAbs = 0f;
                void Assign(string parentKey, float parentOffset, int depth)
                {
                    if (!byParent.TryGetValue(parentKey, out var siblings)) return;
                    int n = siblings.Count;
                    for (int i = 0; i < n; i++)
                    {
                        float local = n > 1 ? (i - (n - 1) * 0.5f) : 0f;
                        float off = parentOffset + local;
                        offsetUnits[siblings[i].id] = off;
                        depthOf[siblings[i].id] = depth;
                        if (Mathf.Abs(off) > maxAbs) maxAbs = Mathf.Abs(off);
                        Assign(siblings[i].id, off, depth + 1);
                    }
                }
                Assign("", 0f, 1);

                rawHalfWidth[cat.id] = Mathf.Max(maxAbs * nodeSpacing + abilityRadius, categoryRadius);
            }

            float totalRaw = 0f;
            foreach (var cat in rawCategories) totalRaw += rawHalfWidth[cat.id] * 2f;
            float scale = totalRaw > 0f ? targetWidth / totalRaw : 1f;
            float finalSpacing = nodeSpacing * scale;

            // MV-472: centred on the fixed 1920-wide reference frame's own midpoint (960) — the pivot
            // WeaponsScreen's _boardScaleRoot/_boardRoot machinery actually scales/positions around —
            // not just left-packed with a margin. That's a no-op for standard mode (targetWidth < 1920,
            // this is exactly OuterMargin on each side) but matters for phone mode, whose targetWidth
            // legitimately EXCEEDS 1920 (using the extra width a wide phone aspect's own visible window
            // provides): packing from a fixed left margin there put the content's own centre right of
            // 960, so the whole phone board rendered off-centre and clipped its own right edge — caught
            // by this file's own EditMode coverage (VisibleRefXWindow), not by eye.
            float cursor = (1920f - targetWidth) * 0.5f;
            var categoryX = new Dictionary<string, float>();
            var categoryHalfWidth = new Dictionary<string, float>();
            foreach (var cat in rawCategories)
            {
                float w = rawHalfWidth[cat.id] * 2f * scale;
                categoryX[cat.id] = cursor + w * 0.5f;
                categoryHalfWidth[cat.id] = w * 0.5f;
                cursor += w;
            }

            var categories = new RigCategoryLayout[rawCategories.Length];
            for (int i = 0; i < rawCategories.Length; i++)
            {
                var c = rawCategories[i];
                categories[i] = new RigCategoryLayout(c.id, c.family, c.icon, categoryX[c.id], rowY[0], categoryHalfWidth[c.id]);
            }

            var abilities = new RigAbilityLayout[rawAbilities.Length];
            for (int i = 0; i < rawAbilities.Length; i++)
            {
                var a = rawAbilities[i];
                float x = categoryX.TryGetValue(a.category, out var cx) ? cx + offsetUnits[a.id] * finalSpacing : a.x;
                int depth = depthOf.TryGetValue(a.id, out var d) ? d : 1;
                float y = rowY[Mathf.Clamp(depth, 1, rowY.Length - 1)];
                abilities[i] = new RigAbilityLayout(a.id, a.category, a.icon, a.label, a.kind, a.parent, x, y, a.maxLevel);
            }

            var fusions = new RigFusionLayout[rawFusions.Length];
            for (int i = 0; i < rawFusions.Length; i++)
            {
                var f = rawFusions[i];
                float x = categoryX.TryGetValue(f.parentA, out var ax) && categoryX.TryGetValue(f.parentB, out var bx)
                    ? (ax + bx) * 0.5f : f.x;
                fusions[i] = new RigFusionLayout(f.id, f.label, f.parentA, f.parentB, f.hudSlot, x, fusionY, f.partCost);
            }

            return new ColumnLayoutResult { Categories = categories, Abilities = abilities, Fusions = fusions };
        }

        // ------------------------------------------------------------------ public reads

        public static IReadOnlyList<RigCategoryLayout> Categories { get { EnsureLoaded(); return s_categories; } }
        public static IReadOnlyList<RigAbilityLayout> Abilities { get { EnsureLoaded(); return s_abilities; } }
        public static IReadOnlyList<RigFusionLayout> Fusions { get { EnsureLoaded(); return s_fusions; } }

        // ------------------------------------------------------------------ phone layout (MV-472)
        //
        // THE RIG's board is authored once (rig_board.json's own topology: which ability belongs to
        // which category, and which ability is whose child) and rendered at TWO independent geometries
        // chosen by WeaponsScreen off the live aspect ratio:
        //  - Standard (the properties above): unchanged positions/radii, just no longer squeezed into a
        //    uniform 1/5-per-family grid — see BuildColumnLayout's own doc comment.
        //  - Phone (below): the SAME topology through the SAME column-layout algorithm, but with radii
        //    and fonts big enough to clear Apple's 44pt tap target / 11pt legibility floors at a real
        //    iPhone's match-by-height physical scale (~393pt tall, see WeaponsScreen.PhonePtScale), and
        //    a taller row schedule (PhoneRowY) that needs a vertical scroll to hold — WeaponsScreen
        //    builds phone-mode nodes inside a ScrollRect for exactly that reason.
        public static IReadOnlyList<RigCategoryLayout> PhoneCategories { get { EnsurePhoneLoaded(); return s_phoneCategories; } }
        public static IReadOnlyList<RigAbilityLayout> PhoneAbilities { get { EnsurePhoneLoaded(); return s_phoneAbilities; } }
        public static IReadOnlyList<RigFusionLayout> PhoneFusions { get { EnsurePhoneLoaded(); return s_phoneFusions; } }

        /// <summary>MV-472: big enough that a node's own 2r x 2r hit rect (the actual tap target —
        /// <see cref="WeaponsScreen"/>'s <c>BuildNodeShell</c> sizes the Hit image to the root's own
        /// square bounds, not the narrower hex silhouette) clears 44pt at a real iPhone's match-by-height
        /// physical scale: 2*64*(393/1080) = 46.6pt. <see cref="RadiusCategoryPhone"/> stays at the
        /// standard 72 — 2*72*(393/1080) = 52.4pt already clears the floor unchanged.</summary>
        public static float RadiusAbilityPhone => 64f;
        public static float RadiusCategoryPhone => 72f;
        public static float RadiusFusionPhone => 64f;   // standard's 40 -> 2*40*(393/1080) = 29.1pt, under floor

        /// <summary>16px labelFontSize -> 5.82pt at a real iPhone's physical scale (16*393/1080), well
        /// under Apple's 11pt floor. 32 clears it with margin (11.6pt) without needing a per-field
        /// scale derivation — every other small caption below uses the same floor-clearing value for the
        /// same reason, sized generously rather than to the exact minimum since phone mode has a whole
        /// scrollable canvas of room to spend.</summary>
        public static float LabelFontSizePhone => 32f;
        public static float CategoryLabelFontSizePhone => 36f;
        public static float LevelPillFontSizePhone => 32f;
        public static float LevelPillWPhone => 108f;
        public static float LevelPillHPhone => 54f;
        public static float FusionSubFontSizePhone => 32f;
        public static float ForgeCaptionFontSizePhone => 32f;

        /// <summary>MV-472: the box <c>WeaponsScreen.BuildNodeShell</c> best-fit-shrinks a phone-mode
        /// label into instead of the standard mode's fixed <c>r*3</c> box — sized so two ADJACENT
        /// siblings' labels (e.g. e_ff "FORCE FIELD" next to e_cel "CELL STORAGE", <see cref="PhoneNodeSpacing"/>
        /// apart at 32pt) can each claim their own half of that gap without touching. Caught live: a
        /// fixed 32pt with no shrink rendered "FORCE FIELDSTORAGE" — two same-family sibling labels
        /// merged into one unreadable string, not clipped by a neighbouring family's column (that was a
        /// separate, already-fixed bug) but literally overlapping each other.</summary>
        public static float PhoneLabelBoxWidth => 190f;

        /// <summary>The floor best-fit is never allowed to shrink a phone label past — 31, not exactly
        /// 30.23 (the literal ref-px->11pt breakeven at a real iPhone's physical scale), for a whole-pixel
        /// margin. A label that still doesn't fit at this size is left to overflow rather than drop
        /// beneath Apple's legibility floor — illegible-but-present beats compliant-but-invisible.</summary>
        public static float PhoneLabelFontSizeMin => 31f;

        /// <summary>MV-472: the phone row schedule — generously spaced (vs. the standard rows) to clear
        /// label text under the bigger phone radii without crowding the next tier; the resulting content
        /// height exceeds a single 1080-tall screen, which is exactly why <c>WeaponsScreen</c> wraps
        /// phone-mode board content in a vertical ScrollRect rather than trying to cram it in.</summary>
        public static float CategoryYPhone => 250f;
        public static float Tier1YPhone => 470f;
        public static float Tier2YPhone => 680f;
        public static float Tier3YPhone => 890f;
        public static float ForgeDividerYPhone => 1050f;
        public static float FusionYPhone => 1180f;

        /// <summary>The scrollable content rect's own height — tall enough to clear
        /// <see cref="FusionYPhone"/> plus the fusion node's own radius and sub-label, with a bottom
        /// margin. A pure constant (not derived) so a future row-schedule tweak has to update it
        /// deliberately rather than silently clipping the last row.</summary>
        public static float PhoneContentHeight => 1360f;

        /// <summary>MV-472 (current spec, defect 3): the standard-mode scrollable content rect's own
        /// height. Standard mode's row schedule (rowY.forge, currently 910) plus the FORGE section's own
        /// fusion sub-caption sits at roughly y=1030 in the 1920x1080 frame — within ~50px of the 1080
        /// bottom edge, tight enough that it was rendering past the visible canvas on some builds with no
        /// way to reach it. A pure constant (not derived), same idiom as <see cref="PhoneContentHeight"/>,
        /// so a future row-schedule tweak has to update it deliberately rather than silently clipping the
        /// last row again.</summary>
        public static float StandardContentHeight => 1120f;

        /// <summary>MV-463 Part 1: the ui-screens harness's own shot sizes, read from
        /// <c>rig_board.json</c>'s <c>captureAspects</c> — replaces the hard-coded 1920x1080/1728x1080
        /// pair so a new aspect (e.g. the phone viewport the game is actually played at) is a data
        /// change, not a code change.</summary>
        public static IReadOnlyList<RigCaptureAspect> CaptureAspects { get { EnsureLoaded(); return s_captureAspects; } }

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

        /// <summary>MV-492: the part-required indicator's own radius — replaces the old amber "+"
        /// PartBadge, moved from the bottom corner (where it overlapped the level pill) into the free
        /// top arc of the hex (see <see cref="PartSlotOffset"/>).</summary>
        public static float PartSlotRadius { get { EnsureLoaded(); return s_geometry.partSlot?.radius ?? 13f; } }

        public static Vector2 PartSlotOffset(float r)
        {
            EnsureLoaded();
            var o = s_geometry.partSlot?.offset;
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
        public static void ResetForTests() { s_loaded = false; s_phoneLoaded = false; }
    }
}
