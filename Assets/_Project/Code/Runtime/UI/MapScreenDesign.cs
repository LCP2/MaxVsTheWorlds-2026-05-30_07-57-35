using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// The map screen's visual design, as data (MV-567).
    ///
    /// This file is the DESIGN ARTEFACT, not a description of one. Every colour and dimension below
    /// is lifted directly from the reference renderer that produced
    /// <c>C:\Dev\MaxVsTheWorlds-Images\MV-566-map-reference.png</c>. Nothing here is a suggestion to be
    /// interpreted — if the map is drawn with these values, in the order below, it matches the
    /// reference. If it does not match, the bug is in how these are applied, not in the values.
    ///
    /// UNITS. The map content lives in world metres (MapScreen._contentSize is the world
    /// footprint, metres treated 1:1 as local units) and the whole content is then scaled by
    /// fitScale * zoom. So every size below is in WORLD METRES, and stays visually constant relative
    /// to the world as the player zooms — which is the point. A label sized as a fraction of its own
    /// room is the defect this replaces: it makes a 20 m room and a 56 m room carry the same clamped
    /// glyph, and ties legibility to room size rather than to the viewport.
    /// </summary>
    public static class MapScreenDesign
    {
        // ---- ground -----------------------------------------------------------------------------
        /// <summary>Opaque. The map is its own surface, not a tint over live gameplay — a translucent
        /// backdrop is what makes the reference's contrast impossible to hit.</summary>
        public static readonly Color Background = new Color32(0x12, 0x16, 0x0F, 0xFF);

        // ---- areas, by role ---------------------------------------------------------------------
        public static readonly Color AreaFill         = new Color32(0x1D, 0x2A, 0x18, 0xFF);
        public static readonly Color AreaBorder       = new Color32(0x6A, 0x80, 0x60, 0xFF);
        public static readonly Color ShedAreaFill     = new Color32(0x33, 0x2C, 0x14, 0xFF);
        public static readonly Color ShedAreaBorder   = new Color32(0xD6, 0x9A, 0x5C, 0xFF);
        public static readonly Color BossAreaFill     = new Color32(0x3A, 0x1C, 0x1C, 0xFF);
        public static readonly Color BossAreaBorder   = new Color32(0xFF, 0x6B, 0x6B, 0xFF);
        /// <summary>The area the player is standing in. Drawn as a border override, not a fill
        /// override, so a current BOSS arena still reads as a boss arena.</summary>
        public static readonly Color CurrentAreaBorder = new Color32(0x4A, 0xA3, 0xFF, 0xFF);

        /// <summary>World metres. Area borders are a constant thickness in world space.</summary>
        public const float AreaBorderWidth = 0.5f;

        // ---- contents ---------------------------------------------------------------------------
        public static readonly Color Cover        = new Color32(0x4F, 0x8A, 0x4F, 0xFF);
        public static readonly Color ShedStatic   = new Color32(0xA8, 0x6B, 0x34, 0xFF);
        public static readonly Color ShedMobile   = new Color32(0xD9, 0x9A, 0x3A, 0xFF);
        public static readonly Color ShedOutline  = new Color32(0xF0, 0xC8, 0x90, 0xFF);
        public static readonly Color Boss         = new Color32(0xD0, 0x3A, 0x3A, 0xFF);
        public static readonly Color BossOutline  = new Color32(0xFF, 0xBC, 0xBC, 0xFF);
        public static readonly Color Gate         = new Color32(0xFF, 0xD2, 0x4A, 0xFF);
        public static readonly Color BossGate     = new Color32(0xFF, 0x80, 0x80, 0xFF);
        public static readonly Color Player       = new Color32(0x4A, 0xA3, 0xFF, 0xFF);

        /// <summary>World metres. A shed's footprint on the map — matches the 3 x 3 m factory body.</summary>
        public const float ShedSize = 3f;
        /// <summary>World metres, radius. A boss dominates its arena; this is deliberately larger than
        /// the 6 x 6 m boss body so three bosses in a30 read as three at full zoom-out.</summary>
        public const float BossRadius = 3.2f;
        /// <summary>World metres. A gate is drawn as a bar ALONG the wall it sits in, its length the
        /// doorway's own width, its thickness this.</summary>
        public const float GateThickness = 1.1f;
        public const float BossGateThickness = 1.9f;
        /// <summary>World metres, radius, for the player dot.</summary>
        public const float PlayerRadius = 2.2f;

        // ---- labels ------------------------------------------------------------------------------
        public static readonly Color LabelText     = new Color32(0xDC, 0xE8, 0xD4, 0xFF);
        public static readonly Color BossLabelText = new Color32(0xFF, 0xD0, 0xD0, 0xFF);

        /// <summary>World metres. CONSTANT across every area, deliberately: the reference reads because
        /// every label is the same size, not because each is a fraction of its own room. At the opening
        /// fit scale this lands around 11 px, which is the legibility floor the reference was checked at.</summary>
        public const float IndexLabelHeight = 3.4f;
        /// <summary>World metres. The area name sits under the index, smaller.</summary>
        public const float NameLabelHeight = 2.3f;
        /// <summary>World metres, inset from the room's bottom edge. Labels sit at the BOTTOM of a room
        /// in the reference, not the top — the top-left corner is where cover clusters, and a label
        /// there lands on top of it.</summary>
        public const float LabelInset = 1.6f;

        /// <summary>Unity's Text.fontSize is an integer in local units, and the content is scaled by
        /// fitScale * zoom afterwards. Convert a world-metre height to that integer without ever
        /// rounding to zero.</summary>
        public static int FontSizeFor(float worldHeight) => Mathf.Max(1, Mathf.RoundToInt(worldHeight));

        // ---- viewport ----------------------------------------------------------------------------
        /// <summary>Fraction of the viewport left as margin when fitting the world on open, so the
        /// outermost areas are not flush against the screen edge.</summary>
        public const float FitMargin = 0.06f;

        // ---- draw order --------------------------------------------------------------------------
        // Later entries paint over earlier ones. Getting this wrong is what buries sheds under cover.
        //   1. background
        //   2. area fills
        //   3. area borders            (current area's border last, so it wins)
        //   4. cover
        //   5. gates                   (on the wall line, so they sit over the border)
        //   6. sheds
        //   7. bosses
        //   8. area index + name labels
        //   9. player dot
        //  10. legend                  (screen space, never inside the pannable content)
    }
}
