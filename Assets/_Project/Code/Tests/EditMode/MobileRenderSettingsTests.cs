using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using MaxWorlds.Core;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-98 — guards the mobile URP tier the iOS build runs on. The iPhone quality level uses
    /// <c>Mobile_RPAsset</c>; these are the settings that keep it at 60fps on a phone.
    /// On-device profiling of the look-vs-cost knobs (SSAO, HDR) is Lee's device pass.
    ///
    /// MV-507 culled <c>MobileTier_HasMsaaDisabled</c> from here — MSAA is now deliberately on for
    /// this tier (measured close to free on tile-based mobile GPUs; see <see cref="MsaaRenderSettingsTests"/>),
    /// so an assertion that it must stay off directly contradicted the shipped setting.
    /// </summary>
    public sealed class MobileRenderSettingsTests
    {
        private const string MobileAsset = "Assets/Settings/Mobile_RPAsset.asset";

        private static UniversalRenderPipelineAsset Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileAsset);
            Assert.IsNotNull(asset, $"Mobile URP asset missing: {MobileAsset}");
            return asset;
        }

        [Test]
        public void MobileTier_DoesNotSupersampleAbove1()
        {
            // renderScale > 1 supersamples — a straight framerate killer on a phone.
            Assert.LessOrEqual(Load().renderScale, 1f,
                "Mobile render scale must not exceed 1 — supersampling a phone drops it below 60fps.");
        }

        // MV-662 — HDR forces a wide colour buffer to serve grading this LDR-graded tier never uses,
        // pure bandwidth cost on a tile-based iOS GPU with no visible benefit. Same reflection idiom
        // as MV660ShieldStrengthKnobTests: builds the panel's private knob table and drives the found
        // knob's own setter, then asserts what the ACTIVE UniversalRenderPipelineAsset resolves to
        // actually changed — never a literal in SettingsPanel.cs (Rule 2).
        [SetUp]
        [TearDown]
        public void ClearDevTuning()
        {
            DevMode.Reset();
            DevTuning.Reset();
        }

        private static Action<float> FindKnobSetter(SettingsPanel panel, string name)
        {
            var panelType = panel.GetType();
            panelType.GetMethod("BuildKnobs", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(panel, null);

            var knobsField = panelType.GetField("_knobs", BindingFlags.NonPublic | BindingFlags.Instance);
            var knobs = (IEnumerable)knobsField.GetValue(panel);

            foreach (var knob in knobs)
            {
                var knobType = knob.GetType();
                if ((string)knobType.GetField("Name").GetValue(knob) == name)
                    return (Action<float>)knobType.GetField("Set").GetValue(knob);
            }
            return null;
        }

        [Test]
        public void MobileTier_HasHdrDisabled_AndRenderScaleKnobDrivesTheActiveAsset()
        {
            Assert.IsFalse(Load().supportsHDR,
                "Mobile URP tier must not support HDR — bandwidth cost for grading this LDR tier never does.");

            var go = new GameObject("SettingsPanel Knob Probe");
            try
            {
                var panel = go.AddComponent<SettingsPanel>();
                var setter = FindKnobSetter(panel, "Render scale");
                Assert.That(setter, Is.Not.Null, "the WEAPONS tab must carry a 'Render scale' knob");

                var urp = UniversalRenderPipeline.asset;
                Assert.IsNotNull(urp, "no active UniversalRenderPipelineAsset — cannot resolve the live effect of the knob");

                setter(0.7f);

                Assert.That(urp.renderScale, Is.EqualTo(0.7f),
                    "driving the 'Render scale' knob's setter must change the active URP asset's resolved renderScale");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
