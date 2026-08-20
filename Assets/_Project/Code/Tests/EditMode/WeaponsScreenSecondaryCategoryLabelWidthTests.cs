using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-491 — sole guard on the SECONDARY category label's box width. By the time this ticket was
    /// picked up, <c>BuildCategoryNode</c> already derived the box from <c>cat.ColumnHalfWidth</c>
    /// (MV-472, predating this ticket's own stale evidence — see the ticket's "verify the current line
    /// number before editing" caveat) instead of the fixed <see cref="RigBoardLayout.PhoneLabelBoxWidth"/>
    /// (190) MV-491 was raised against, so no production fix landed here. What was still missing was
    /// the Tier 2 assertion the ticket's own AC #2 calls for: a resolved-value check tying the label box
    /// to <c>ColumnHalfWidth</c>, per the MV-465 testing policy's Rule 3 (no presence tests — assert a
    /// measured property, not that a field merely exists). This is the sole new test MV-491 adds.
    /// </summary>
    public sealed class WeaponsScreenSecondaryCategoryLabelWidthTests
    {
        private GameObject _go;
        private WeaponsScreen _screen;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();
            _go = new GameObject("WeaponsScreen");
            _screen = _go.AddComponent<WeaponsScreen>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();
        }

        /// <summary>Resolves the SECONDARY category label's actual box width and wrap behaviour the
        /// same way Unity's renderer does — <see cref="RectTransform.sizeDelta"/> (what layout actually
        /// used, not an authored constant) and <see cref="TextGenerator.Populate"/> against it (what
        /// <see cref="Text.OnPopulateMesh"/> actually renders) — and checks both against
        /// <see cref="RigCategoryLayout.ColumnHalfWidth"/>, the real per-category layout data
        /// <c>cat.ColumnHalfWidth</c>, never a fixed pixel width like the old, pre-MV-472
        /// <see cref="RigBoardLayout.PhoneLabelBoxWidth"/> (190) that produced "SECONDAR"/"Y". A fixed
        /// box (any value not proportional to <c>ColumnHalfWidth</c>) would either fail the width check
        /// outright or, for the specific 190px case, still wrap SECONDARY into two lines — either way
        /// this test cannot pass against a fixed-width box.</summary>
        [Test]
        public void SecondaryCategoryLabelBoxWidthTracksColumnHalfWidthAndDoesNotWrap()
        {
            _screen.Open();
            _screen.ApplyBoardScale(977f / 458f);   // MV-472's own registered phone capture aspect

            var cat = RigBoardLayout.PhoneCategories.FirstOrDefault(c => c.Id == "SECONDARY");
            Assert.That(cat, Is.Not.Null, "SECONDARY must be a registered phone-mode category");

            var node = _screen.BoardNode("SECONDARY");
            Assert.That(node, Is.Not.Null, "SECONDARY category node must exist");
            var label = node.Find("Text")?.GetComponent<Text>();
            Assert.That(label, Is.Not.Null, "SECONDARY category node must carry a label Text");
            Assert.That(label.resizeTextForBestFit, Is.True, "fixture assumption: label must be best-fit driven");

            float expectedWidth = Mathf.Max(2f * cat.ColumnHalfWidth - 16f, 120f);
            Assert.That(label.rectTransform.sizeDelta.x, Is.EqualTo(expectedWidth).Within(0.5f),
                $"resolved label box width must track cat.ColumnHalfWidth ({cat.ColumnHalfWidth}), " +
                $"not a fixed pixel width like the pre-MV-472 PhoneLabelBoxWidth ({RigBoardLayout.PhoneLabelBoxWidth})");

            var settings = label.GetGenerationSettings(label.rectTransform.rect.size);
            label.cachedTextGenerator.Populate(label.text, settings);
            Assert.That(label.cachedTextGenerator.lineCount, Is.EqualTo(1),
                "SECONDARY must resolve to a single line — a box too narrow for its own column " +
                "(the pre-MV-472 defect) breaks it into \"SECONDAR\"/\"Y\"");
        }
    }
}
