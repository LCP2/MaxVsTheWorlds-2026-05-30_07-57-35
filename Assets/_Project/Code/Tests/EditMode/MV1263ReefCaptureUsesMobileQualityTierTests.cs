using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Dev;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1263 (the one new test): every Reef <c>CapturePreset</c> (MV-1250/1251/1255-1258) now carries
    /// a <c>QualityLevelName</c> ("Mobile") that <see cref="CaptureDirector.ApplyQualityLevel"/> resolves
    /// BY NAME against <see cref="QualitySettings.names"/> and applies before a capture runs. Every Reef
    /// capture up to and including TestFlight 0.17.0 ran whatever quality level Standalone defaults to
    /// ("PC", HDR on) -- never the Mobile/LDR tier the real iPhone/WebGL build ships under -- which is
    /// why those captures measured a dark navy room with bright cyan floor circuitry while Lee's own
    /// iPhone showed flat mauve with none (MV-1263's own ticket).
    ///
    /// This asserts the RESOLVED <see cref="QualitySettings"/> state after
    /// <see cref="CaptureDirector.ApplyQualityLevel"/> runs -- not a re-read of the authored "Mobile"
    /// string back at itself (CLAUDE.md's testing policy, Tier 1) -- so it would still fail if
    /// <c>ApplyQualityLevel</c> silently no-op'd or resolved the wrong index.
    ///
    /// Fails on base commit acb6c82: <c>CapturePreset</c> carries no <c>QualityLevelName</c> field and
    /// <c>CaptureDirector.ApplyQualityLevel</c> doesn't exist -- a compile failure, the same "doesn't
    /// exist yet" shape <c>MV1032CaptureNeverResumesLiveRunTests</c> already used for this same class.
    /// </summary>
    public sealed class MV1263ReefCaptureUsesMobileQualityTierTests
    {
        private int _restoreTo;

        [SetUp]
        public void SetUp() => _restoreTo = QualitySettings.GetQualityLevel();

        [TearDown]
        public void TearDown() => QualitySettings.SetQualityLevel(_restoreTo, true);

        [Test]
        public void Mv1250ReefLookCheck_AppliesTheMobileQualityTier()
        {
            CapturePreset preset = CapturePresets.All["mv1250reeflook"];

            CaptureDirector.ApplyQualityLevel(preset.QualityLevelName);

            string resolved = QualitySettings.names[QualitySettings.GetQualityLevel()];
            Assert.That(resolved, Is.EqualTo("Mobile"),
                "CaptureDirector.ApplyQualityLevel must leave QualitySettings actually resolved to the " +
                "'Mobile' tier -- every Reef capture before this ticket measured the Standalone default " +
                "('PC', HDR on), not the LDR tier the real iPhone/WebGL build ships under.");
        }
    }
}
