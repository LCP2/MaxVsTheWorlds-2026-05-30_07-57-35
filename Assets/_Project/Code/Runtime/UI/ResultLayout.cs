namespace MaxWorlds.UI
{
    /// <summary>
    /// The Result card's geometry (YT-31/YT-81) — one content column, derived, not typed in twice.
    ///
    /// The REPLAY button used to be placed at a hand-written x of -300. Its pivot is its CENTRE, so
    /// a 300-wide button at -300 spans -450..-150 — and the panel's left edge is at -360. It hung 90
    /// px out into open space. Nothing caught it, because every number in the card was an
    /// independent literal: the stat rows happened to sit on a 40 px margin, the buttons happened
    /// not to, and no single place said what the margin WAS.
    ///
    /// So it's said here, once. Every row and every button is derived from <see cref="ContentLeft"/>
    /// and <see cref="ContentRight"/>, which means "aligned to the same margins" is now true by
    /// construction rather than by coincidence — you cannot nudge one without the other following.
    /// Pure, so it's unit-testable with no canvas.
    ///
    /// Local space, panel-centred: the panel spans ±<see cref="PanelWidth"/>/2 about x = 0.
    /// </summary>
    public static class ResultLayout
    {
        public const float PanelWidth = 720f;
        public const float PanelHeight = 560f;

        /// <summary>Gap between the panel's edge and its content column. The TIME / ROBOTS DESTROYED
        /// rows already sat on this; the CTAs now do too.</summary>
        public const float ContentMargin = 40f;

        public const float ButtonWidth = 300f;
        public const float ButtonHeight = 64f;

        /// <summary>Width of a stat row's label (and of its value, mirrored).</summary>
        public const float StatCellWidth = 320f;

        public static float PanelLeft => -PanelWidth * 0.5f;
        public static float PanelRight => PanelWidth * 0.5f;

        public static float ContentLeft => PanelLeft + ContentMargin;
        public static float ContentRight => PanelRight - ContentMargin;
        public static float ContentWidth => ContentRight - ContentLeft;

        // Rects here are anchored with a CENTRED pivot, so these are centre-x values, not edges.
        // That distinction is the entire bug this file exists to prevent.

        /// <summary>Centre-x of the left CTA (REPLAY), flush with the content column's left edge.</summary>
        public static float LeftButtonX => ContentLeft + ButtonWidth * 0.5f;

        /// <summary>Centre-x of the right CTA (NEXT WORLD), flush with the column's right edge.</summary>
        public static float RightButtonX => ContentRight - ButtonWidth * 0.5f;

        /// <summary>Clear space between the two CTAs. Falls out of the column — it isn't chosen.</summary>
        public static float ButtonGap => (RightButtonX - ButtonWidth * 0.5f)
                                       - (LeftButtonX + ButtonWidth * 0.5f);

        /// <summary>Centre-x of a stat row's label, left-aligned to the content column.</summary>
        public static float StatLabelX => ContentLeft + StatCellWidth * 0.5f;

        /// <summary>Centre-x of a stat row's value, right-aligned to the content column.</summary>
        public static float StatValueX => ContentRight - StatCellWidth * 0.5f;
    }
}
