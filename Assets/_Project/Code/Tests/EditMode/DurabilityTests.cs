using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The durability defaults and the live-retune the YT-126 sliders drive.
    /// </summary>
    public sealed class DurabilityTests
    {
        [SetUp]
        [TearDown]
        public void Clear() => DevTuning.Reset();

        [Test]
        public void FactoryHealthDefaultIsTheRaisedValue()
        {
            var go = new GameObject("Hutch");
            try
            {
                var hutch = go.AddComponent<MowerHutch>();   // RequireComponent brings EnemySpawner
                Assert.That(hutch.AuthoredMax, Is.EqualTo(350f).Within(0.001f),
                    "factories were dying instantly; the default should be ~2.5x the old 140");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RaisingTheCeiling_GivesHeadroom_NotAHeal()
        {
            var h = new DestructibleHealth(100f);
            h.TakeDamage(40f);   // Current 60 of 100
            Assert.That(h.Current, Is.EqualTo(60f).Within(0.001f));

            h.Retune(200f);
            Assert.That(h.Max, Is.EqualTo(200f).Within(0.001f), "the ceiling did not rise");
            Assert.That(h.Current, Is.EqualTo(60f).Within(0.001f),
                "retuning topped the structure up — that would be a heal nobody asked for");
            Assert.That(h.Normalized, Is.EqualTo(0.3f).Within(0.001f), "the bar should now read 30%");
        }

        [Test]
        public void LoweringTheCeilingBelowCurrent_ClampsSoTheBarNeverReadsPastFull()
        {
            var h = new DestructibleHealth(500f);   // Current 500
            h.Retune(120f);
            Assert.That(h.Current, Is.EqualTo(120f).Within(0.001f),
                "current above the new ceiling would draw a bar past 100%");
            Assert.That(h.Normalized, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void TheBossDefaultIsItsAuthoredMax()
        {
            // The Boss-health slider's 100% reference is BossTuning.Health, not the stale scene field.
            Assert.That(BossTuning.Health, Is.GreaterThan(0f));
            Assert.That(DevTuning.Or(DevTuning.BossHealth, BossTuning.Health), Is.EqualTo(BossTuning.Health),
                "a fresh session must use the authored boss HP until the slider moves");
        }
    }
}
