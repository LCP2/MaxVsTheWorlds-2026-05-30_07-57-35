using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-525. MV-513 re-paced the escalation curve (<see cref="DifficultyDirector.AuthoredRunLengthSeconds"/>
    /// 360f -> 2750f, <see cref="DifficultyDirector.AuthoredPerShedBump"/> to 4/7 of that) but left two
    /// Settings sliders — Run length and Shed clock skip — on their old [Min,Max], so each one's own
    /// authored Default sat outside its own slider's range: a single touch of either slider could only
    /// ever produce a value inside the STALE range, silently undoing MV-513's retune and (per
    /// <c>DevTuning</c>'s persistence) surviving every relaunch on that device.
    ///
    /// One test, per the project's one-new-test-per-ticket rule: the general sweep (AC1) is the real
    /// regression guard — it fails on any FUTURE re-pace too, not just this one — and the two round-trip
    /// assertions (AC2) are additional Asserts inside the same test method, not a second test.
    /// </summary>
    public sealed class MV525SliderBoundsInvariantTests
    {
        private sealed class KnobBounds
        {
            public string Name;
            public float Min;
            public float Max;
            public float Default;
        }

        /// <summary>
        /// <see cref="SettingsPanel.BuildKnobs"/> is where every knob's Min/Max/Default is declared, but
        /// it's a private instance method reading live scene objects — the same reflection idiom
        /// MV506TimeScaleCaptureGuardTests already uses to call SettingsPanel's private "Build". Called
        /// directly (not via "Build") so this needs no Canvas/EventSystem/uGUI at all — just the knob
        /// data.
        /// </summary>
        private static List<KnobBounds> BuildAllKnobBounds()
        {
            var go = new GameObject("SettingsPanel Knob Probe");
            try
            {
                var panel = go.AddComponent<SettingsPanel>();
                var panelType = panel.GetType();
                panelType.GetMethod("BuildKnobs", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(panel, null);

                var knobsField = panelType.GetField("_knobs", BindingFlags.NonPublic | BindingFlags.Instance);
                var knobs = (IEnumerable)knobsField.GetValue(panel);

                var result = new List<KnobBounds>();
                foreach (var knob in knobs)
                {
                    var knobType = knob.GetType();
                    result.Add(new KnobBounds
                    {
                        Name = (string)knobType.GetField("Name").GetValue(knob),
                        Min = (float)knobType.GetField("Min").GetValue(knob),
                        Max = (float)knobType.GetField("Max").GetValue(knob),
                        Default = (float)knobType.GetField("Default").GetValue(knob),
                    });
                }
                return result;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [SetUp]
        [TearDown]
        public void ClearState()
        {
            DevMode.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void EveryKnobsDefaultLiesWithinItsOwnRange_AndTheEscalationSlidersRoundTripTheirAuthoredValue()
        {
            var knobs = BuildAllKnobBounds();
            Assert.That(knobs.Count, Is.GreaterThan(0), "sanity: BuildKnobs produced no knobs at all");

            // AC1: the general guard — sweep every registered knob, not just the two this ticket names.
            var violations = new List<string>();
            foreach (var k in knobs)
            {
                if (k.Default < k.Min || k.Default > k.Max)
                    violations.Add($"{k.Name}: default={k.Default} outside [{k.Min}, {k.Max}]");
            }
            Assert.That(violations, Is.Empty,
                "every registered knob must satisfy Min <= Default <= Max: " + string.Join("; ", violations));

            // AC2: Run length and Shed clock skip must still round-trip their authored default through
            // the piecewise-normalised slider mapping (YT-205) at their (now widened) bounds.
            var runLength = knobs.Find(k => k.Name == "Run length");
            Assert.That(runLength, Is.Not.Null, "Run length knob must exist");
            float runLengthPos = SettingsPanel.ValueToPos(runLength.Min, runLength.Max, runLength.Default,
                DifficultyDirector.AuthoredRunLengthSeconds);
            float runLengthRoundTrip = SettingsPanel.PosToValue(runLength.Min, runLength.Max, runLength.Default,
                runLengthPos);
            Assert.That(runLengthRoundTrip, Is.EqualTo(DifficultyDirector.AuthoredRunLengthSeconds).Within(0.01f),
                "Run length must round-trip its authored default");

            var shedSkip = knobs.Find(k => k.Name == "Shed clock skip");
            Assert.That(shedSkip, Is.Not.Null, "Shed clock skip knob must exist");
            float shedSkipPos = SettingsPanel.ValueToPos(shedSkip.Min, shedSkip.Max, shedSkip.Default,
                DifficultyDirector.AuthoredPerShedBump);
            float shedSkipRoundTrip = SettingsPanel.PosToValue(shedSkip.Min, shedSkip.Max, shedSkip.Default,
                shedSkipPos);
            Assert.That(shedSkipRoundTrip, Is.EqualTo(DifficultyDirector.AuthoredPerShedBump).Within(0.01f),
                "Shed clock skip must round-trip its authored default");
        }
    }
}
