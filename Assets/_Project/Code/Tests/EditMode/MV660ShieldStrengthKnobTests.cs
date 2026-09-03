using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-660: every Force Field STRENGTH knob (absorb cap, cap/level, cooldown, radius, pop damage)
    /// already lived in <see cref="DevTuning"/> and was already read live by gameplay, but none had a
    /// Settings-panel slider — the shield could not be made stronger from the panel at all. Tier 2
    /// (resolved value), same reflection idiom as MV525SliderBoundsInvariantTests: builds the panel's
    /// private knob table and drives the found knob's own setter, then asserts what
    /// <see cref="AbilityTuning.ForceFieldAbsorbCap"/> resolves to actually changed — not merely that
    /// a slider "exists" (Rule 3).
    /// </summary>
    public sealed class MV660ShieldStrengthKnobTests
    {
        [SetUp]
        [TearDown]
        public void ClearState()
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
        public void MovingTheShieldAbsorbCapSliderChangesTheResolvedAbsorbCap()
        {
            var go = new GameObject("SettingsPanel Knob Probe");
            try
            {
                var panel = go.AddComponent<SettingsPanel>();
                var setter = FindKnobSetter(panel, "Shield absorb cap");
                Assert.That(setter, Is.Not.Null, "the WEAPONS tab must carry a 'Shield absorb cap' knob");

                float before = AbilityTuning.ForceFieldAbsorbCap(1,
                    DevTuning.Or(DevTuning.ForceFieldAbsorbCap, AbilityTuning.DefaultForceFieldAbsorbCap),
                    AbilityTuning.DefaultForceFieldAbsorbCapPerLevel);
                Assert.That(before, Is.EqualTo(AbilityTuning.DefaultForceFieldAbsorbCap),
                    "precondition: no override yet, so the resolved cap is the authored default");

                setter(before + 100f);

                float after = AbilityTuning.ForceFieldAbsorbCap(1,
                    DevTuning.Or(DevTuning.ForceFieldAbsorbCap, AbilityTuning.DefaultForceFieldAbsorbCap),
                    AbilityTuning.DefaultForceFieldAbsorbCapPerLevel);
                Assert.That(after, Is.EqualTo(before + 100f),
                    "driving the knob's setter must change the resolved absorb cap gameplay reads");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
