using NUnit.Framework;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-433/MV-445/MV-472 — THE RIG board's scale-to-fit clamp and, as of MV-472, its two numeric
    /// legibility/clipping floors. Most of the chrome's visual assertions (backdrop opacity, region
    /// tint, node glow, connectors) were culled by MV-465 as EditMode appearance/presence tests, gated
    /// by the PNG-vs-spec harness instead — but MV-472's REGRESSION NOTE explicitly restores the two
    /// tests below and exempts them from any future cull: both assert a pure numeric measurement
    /// (on-screen point size, ref-px window bounds) with no rendered object involved, not an appearance
    /// judgement, so Rule 2/3 of the testing policy (MV-465) doesn't apply to them.
    /// </summary>
    public sealed class RigBoardChromeTests
    {
        /// <summary>
        /// What's actually enforced: the clamp itself never drops below <see cref="WeaponsScreen.BoardScaleFloor"/>
        /// (MV-472: 0.70, was MV-445's 0.83 — see that constant's own doc comment for why) at any aspect,
        /// however narrow.
        /// </summary>
        [Test]
        public void BoardScaleNeverDropsBelowItsOwnFloor()
        {
            float[] aspects = { 2.17f, 16f / 9f, 1.60f, 1.50f, 1.4f, 1.33f, 1.0f };
            foreach (float aspect in aspects)
                Assert.That(WeaponsScreen.ComputeBoardScale(aspect), Is.GreaterThanOrEqualTo(0.70f - 1e-4f), $"aspect {aspect}");
        }

        // ------------------------------------------------------------------ MV-472 AC1: the 44pt/11pt floors, MET

        /// <summary>
        /// Restores (in spirit — the old version documented the shortfall, this one proves the fix) the
        /// test MV-465 culled: <c>DocumentsTheClampFloorsKnownShortfallAgainstTheLiteralFortyFourPointAc</c>.
        /// Every tappable hex and every rendered caption on THE RIG's board must clear Apple's 44pt tap
        /// target / 11pt legibility floors at the narrowest supported DEVICE — which, under this canvas's
        /// match-by-height CanvasScaler, is governed by physical device HEIGHT in points, not aspect
        /// ratio: 1 reference pixel = deviceHeightPt / 1080. A real iPhone lands around 393pt tall in
        /// landscape (this ticket's own derivation), well under iPad mini's ~744pt, so iPhone is the
        /// worst case this converts against. <c>WeaponsScreen.BuildNodeShell</c>'s own Hit image
        /// fills the node's r*2 x r*2 SQUARE root rect (not the narrower hex silhouette), so 2r is the
        /// real tap-target dimension, not the hex's own width.
        /// </summary>
        [Test]
        public void EveryTappableHexClearsFortyFourPointsAndEveryGlyphClearsElevenPointsAtThePhoneAspect()
        {
            const float phoneAspect = 2340f / 1080f;   // rig_board.json's own registered "phone" captureAspect
            const float iPhoneHeightPt = 393f;
            float physicalScale = iPhoneHeightPt / 1080f;

            float boardScale = WeaponsScreen.ComputeBoardScale(phoneAspect);
            Assert.That(boardScale, Is.EqualTo(1f).Within(1e-4f),
                "fixture assumption: phone aspect is wider than 16:9, so ComputeBoardScale never shrinks it — the phone floors are cleared by RigBoardLayout's own phone radii/fonts alone, never by this clamp");

            float PtSize(float refPx) => refPx * boardScale * physicalScale;

            Assert.That(PtSize(RigBoardLayout.RadiusAbilityPhone * 2f), Is.GreaterThanOrEqualTo(44f), "ability node tap target");
            Assert.That(PtSize(RigBoardLayout.RadiusCategoryPhone * 2f), Is.GreaterThanOrEqualTo(44f), "category node tap target");
            Assert.That(PtSize(RigBoardLayout.RadiusFusionPhone * 2f), Is.GreaterThanOrEqualTo(44f), "fusion node tap target");

            Assert.That(PtSize(RigBoardLayout.LabelFontSizePhone), Is.GreaterThanOrEqualTo(11f), "ability label");
            Assert.That(PtSize(RigBoardLayout.CategoryLabelFontSizePhone), Is.GreaterThanOrEqualTo(11f), "category label");
            Assert.That(PtSize(RigBoardLayout.LevelPillFontSizePhone), Is.GreaterThanOrEqualTo(11f), "level pill");
            Assert.That(PtSize(RigBoardLayout.FusionSubFontSizePhone), Is.GreaterThanOrEqualTo(11f), "fusion sub-caption");
            Assert.That(PtSize(RigBoardLayout.ForgeCaptionFontSizePhone), Is.GreaterThanOrEqualTo(11f), "FORGE caption");

            // Proof this floor was really missed pre-fix, not a hypothetical: the STANDARD geometry (the
            // only geometry that existed before MV-472 added a phone mode) at this same phone aspect —
            // ability tap target 100 ref-px * 1.0 * (393/1080) = 36.4pt; label 16 ref-px * 1.0 * (393/1080)
            // = 5.8pt. Both numbers match the ticket's own "WHY IT FAILS" arithmetic exactly.
            Assert.That(PtSize(RigBoardLayout.RadiusAbility * 2f), Is.LessThan(44f),
                "sanity: the pre-MV-472 standard geometry really did fail the tap-target floor at phone aspect");
            Assert.That(PtSize(RigBoardLayout.LabelFontSize), Is.LessThan(11f),
                "sanity: the pre-MV-472 standard geometry really did fail the legibility floor at phone aspect");
        }

        // ------------------------------------------------------------------ MV-472 AC1: nothing clips, at every tested aspect

        /// <summary>
        /// Restores (in spirit) the test MV-465 culled:
        /// <c>EveryNodeAndRegionPanelFitsInsideTheVisibleWindowAtEveryTestedAspect</c>. Mode-aware (picks
        /// phone or standard geometry the same way <see cref="WeaponsScreen.IsPhoneLayout"/> does in
        /// production) so it covers both halves of this ticket: iPad mini (1078x815, this ticket's own
        /// SG1 evidence for the pre-fix SUPPORT clip) on the standard side, and the registered "phone"
        /// captureAspect on the phone side. <see cref="RigCategoryLayout.ColumnHalfWidth"/> is exactly
        /// the panel's own half-extent (MV-472's content-proportional column layout), so no boundary math
        /// needs re-deriving here the way the old test had to.
        ///
        /// MV-480 adds the Y term (X-only before this ticket, so a node could clip off the TOP/BOTTOM of
        /// the frame with nothing here to catch it — see the FORGE fusion node's own ~y=1030 margin,
        /// this ticket's own motivating evidence). Standard mode's own bound is the visible [0, 1080]
        /// board frame: <c>WeaponsScreen.BuildStandardBoardContent</c> masks a little further than that
        /// (<c>RigBoardLayout.StandardContentHeight</c>=1120) as a defensive buffer nothing on the board
        /// today actually uses, so 1080 is still the right "did this genuinely overrun" line. Phone mode
        /// is the deliberate exception this ticket calls out: its own viewport
        /// (<c>WeaponsScreen.BuildPhoneScrollViewport</c>, board-frame y [140, 1050]) is real content
        /// clipped shorter than the board frame by design, and a node below that fold is still reachable
        /// by scrolling — genuinely NOT clipped — so phone mode is checked against the taller scrollable
        /// CONTENT rect (<see cref="RigBoardLayout.PhoneContentHeight"/>, content-local so no board-centre
        /// pivot term the way X needs one) instead of the shorter viewport.
        /// </summary>
        [Test]
        public void EveryNodeAndRegionPanelFitsInsideTheVisibleWindowAtEveryTestedAspect()
        {
            float[] aspects = { 2340f / 1080f, 2.0f, 16f / 9f, 1.6f, 1.5f, 1.4f, 1078f / 815f };
            const float boardCentreX = 1920f * 0.5f;

            foreach (float aspect in aspects)
            {
                bool phoneMode = WeaponsScreen.IsPhoneLayout(aspect);
                var categories = phoneMode ? RigBoardLayout.PhoneCategories : RigBoardLayout.Categories;
                var abilities = phoneMode ? RigBoardLayout.PhoneAbilities : RigBoardLayout.Abilities;
                var fusions = phoneMode ? RigBoardLayout.PhoneFusions : RigBoardLayout.Fusions;
                float abR = phoneMode ? RigBoardLayout.RadiusAbilityPhone : RigBoardLayout.RadiusAbility;
                float fuR = phoneMode ? RigBoardLayout.RadiusFusionPhone : RigBoardLayout.RadiusFusion;

                float scale = WeaponsScreen.ComputeBoardScale(aspect);
                var window = WeaponsScreen.VisibleRefXWindow(aspect);
                float yMax = phoneMode ? RigBoardLayout.PhoneContentHeight : 1080f;

                void AssertFitsX(string id, float rawX, float halfExtent)
                {
                    float scaledX = boardCentreX + (rawX - boardCentreX) * scale;
                    float scaledHalf = halfExtent * scale;
                    Assert.That(scaledX - scaledHalf, Is.GreaterThanOrEqualTo(window.MinX - 0.5f),
                        $"'{id}' left edge clipped at aspect {aspect} (phoneMode={phoneMode})");
                    Assert.That(scaledX + scaledHalf, Is.LessThanOrEqualTo(window.MaxX + 0.5f),
                        $"'{id}' right edge clipped at aspect {aspect} (phoneMode={phoneMode})");
                }

                void AssertFitsY(string id, float rawY, float halfExtent)
                {
                    // MV-516: standard mode's Y no longer scales from the board's own vertical CENTRE —
                    // WeaponsScreen.Build's boardScaleRoot pivot moved to the TOP (see its own doc
                    // comment) so a narrower-than-16:9 aspect's width squeeze stops dragging content
                    // above the midpoint DOWN toward it (the dead-band bug this ticket fixes). Phone mode
                    // was already unscaled Y (its own boardScale is always 1); standard mode now matches
                    // that same top-anchored formula whenever scale < 1.
                    float top, bottom;
                    if (phoneMode) { top = rawY - halfExtent; bottom = rawY + halfExtent; }
                    else
                    {
                        float scaledY = rawY * scale;
                        float scaledHalf = halfExtent * scale;
                        top = scaledY - scaledHalf; bottom = scaledY + scaledHalf;
                    }
                    Assert.That(top, Is.GreaterThanOrEqualTo(-0.5f),
                        $"'{id}' top edge clipped at aspect {aspect} (phoneMode={phoneMode})");
                    Assert.That(bottom, Is.LessThanOrEqualTo(yMax + 0.5f),
                        $"'{id}' bottom edge clipped at aspect {aspect} (phoneMode={phoneMode})");
                }

                void AssertFits(string id, float rawX, float rawY, float halfExtent)
                {
                    AssertFitsX(id, rawX, halfExtent);
                    AssertFitsY(id, rawY, halfExtent);
                }

                foreach (var cat in categories)
                {
                    float catR = phoneMode ? RigBoardLayout.RadiusCategoryPhone : RigBoardLayout.RadiusCategory;
                    AssertFits(cat.Id, cat.X, cat.Y, catR);
                    AssertFitsX($"{cat.Id} Panel", cat.X, cat.ColumnHalfWidth);
                }
                foreach (var ab in abilities) AssertFits(ab.Id, ab.X, ab.Y, abR);
                foreach (var fu in fusions) AssertFits(fu.Id, fu.X, fu.Y, fuR);
            }
        }
    }
}
