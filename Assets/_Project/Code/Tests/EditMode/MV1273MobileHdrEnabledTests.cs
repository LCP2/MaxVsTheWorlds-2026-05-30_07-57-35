using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1273 (the one new test) — Lee's decision, 21 Oct 2026: turn HDR on for the Mobile quality
    /// tier. <c>BackyardLighting</c> already runs Bloom (threshold 1.35, <c>BackyardLook</c>) and
    /// Neutral tonemapping on every platform; with HDR off no pixel can exceed 1.0, so bloom can never
    /// fire on iPhone or WebGL and every neon/emissive glow tuned on Windows (HDR on) was dead on the
    /// phone.
    ///
    /// Resolves the render pipeline asset <see cref="QualitySettings"/> actually binds to iPhone's and
    /// WebGL's own default quality level — the level index is read from
    /// <c>ProjectSettings/QualitySettings.asset</c>'s <c>m_PerPlatformDefaultQuality</c> rather than
    /// assumed to be "Mobile" by name or hard-coded to index 0 — via <see cref="QualitySettings.renderPipeline"/>
    /// after switching to that level. This is the resolved asset the engine actually uses for that
    /// level (Tier 2), not a re-read of <c>Mobile_RPAsset.asset</c>'s own authored text (Tier 1).
    /// </summary>
    public sealed class MV1273MobileHdrEnabledTests
    {
        private static string RepoRoot => Directory.GetParent(Application.dataPath).FullName;

        private static int DefaultQualityLevelForPlatform(string platformKey)
        {
            string path = Path.Combine(RepoRoot, "ProjectSettings", "QualitySettings.asset");
            string yaml = File.ReadAllText(path);
            var match = Regex.Match(yaml, $@"^\s+{Regex.Escape(platformKey)}:\s*(\d+)\s*$", RegexOptions.Multiline);
            Assert.IsTrue(match.Success, $"m_PerPlatformDefaultQuality carries no entry for '{platformKey}'");
            return int.Parse(match.Groups[1].Value);
        }

        [Test]
        public void IPhoneAndWebGLDefaultQualityLevel_ResolveToAnHdrCapablePipeline()
        {
            int iphoneLevel = DefaultQualityLevelForPlatform("iPhone");
            int webglLevel = DefaultQualityLevelForPlatform("WebGL");
            Assert.AreEqual(iphoneLevel, webglLevel,
                "iPhone and WebGL are expected to default to the same quality level in this project");

            int original = QualitySettings.GetQualityLevel();
            try
            {
                QualitySettings.SetQualityLevel(iphoneLevel, applyExpensiveChanges: false);

                var pipeline = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
                Assert.IsNotNull(pipeline,
                    $"quality level {iphoneLevel} ('{QualitySettings.names[iphoneLevel]}') resolves no UniversalRenderPipelineAsset");

                Assert.IsTrue(pipeline.supportsHDR,
                    "the iPhone/WebGL default quality level must resolve to an HDR-capable pipeline -- " +
                    "with HDR off no pixel can exceed 1.0, so BackyardLighting's Bloom (threshold 1.35) " +
                    "and every neon/emissive glow tuned on Windows never fires on iPhone or WebGL");
            }
            finally
            {
                QualitySettings.SetQualityLevel(original, applyExpensiveChanges: false);
            }
        }
    }
}
