using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The durability defaults and the live-retune the YT-126 sliders drive.
    ///
    /// MV-464: <see cref="MovingTheFactorySliderRetunesALiveHutch"/> moved in from a PlayMode
    /// fixture — <see cref="MowerHutch.Build"/> is called directly instead of relying on Awake,
    /// which never runs from AddComponent outside Play mode.
    /// </summary>
    public sealed class DurabilityTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            DevTuning.Reset();
            FactoryCensus.Reset();
        }

        [Test]
        public void FactoryHealthDefaultIsTheRaisedValue()
        {
            var go = new GameObject("Hutch");
            try
            {
                var hutch = go.AddComponent<MowerHutch>();   // RequireComponent brings EnemySpawner
                Assert.That(hutch.AuthoredMax, Is.EqualTo(915.915f).Within(0.001f),
                    "MV-315: re-baked to 61% of YT-200's 1501.5 (was 350 before that)");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void MovingTheFactorySliderRetunesALiveHutch()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var hutch = go.AddComponent<MowerHutch>();
                hutch.Build();

                Assert.That(hutch.Normalized, Is.EqualTo(1f).Within(0.001f), "a fresh hutch starts full");

                // Raise the ceiling: the dented amount stays, so the same HP now reads as half.
                // MV-464: was a literal 700 against a stale "authored 350" assumption (the original
                // PlayMode fixture's own comment) — MV-315 re-baked the authored default up to
                // 915.915, so 700 had quietly become a LOWER ceiling than a fresh hutch's current HP,
                // and this assertion would fail the moment it actually ran. Never caught because
                // playcheck (PlayMode CI) has been parked since MV-460. Anchored to AuthoredMax now
                // so a future re-bake can't silently invert this test's meaning again.
                DevTuning.FactoryHealth = hutch.AuthoredMax * 2f;
                hutch.RefreshMax();
                Assert.That(hutch.Normalized, Is.EqualTo(0.5f).Within(0.001f),
                    "raising factory health mid-session must give headroom, not top it up");

                // Lower it below current: clamps, so the bar never reads past full.
                DevTuning.FactoryHealth = 200f;
                hutch.RefreshMax();
                Assert.That(hutch.Normalized, Is.EqualTo(1f).Within(0.001f),
                    "lowering the ceiling below current HP must clamp");
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
        public void HealRestoresHpWithoutOverfillingOrRevivingACorpse()
        {
            var h = new DestructibleHealth(100f);
            h.TakeDamage(40f);   // Current 60 of 100

            h.Heal(15f);
            Assert.That(h.Current, Is.EqualTo(75f).Within(0.001f));

            h.Heal(1000f);
            Assert.That(h.Current, Is.EqualTo(100f).Within(0.001f), "heal must not overfill past Max");

            h.TakeDamage(100f);   // destroyed
            Assert.That(h.IsAlive, Is.False);
            h.Heal(50f);
            Assert.That(h.Current, Is.EqualTo(0f).Within(0.001f), "a destroyed structure must not be healed back to life");
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
