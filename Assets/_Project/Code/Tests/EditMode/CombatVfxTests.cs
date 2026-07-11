using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-48 — combat feedback VFX. Covers the tuning maths and the material guard (a
    /// particle system with no material draws nothing).
    ///
    /// The director's own lifecycle is NOT tested here: Awake/OnEnable do not run in edit
    /// mode, so an EditMode test of "does it unsubscribe" would pass without it ever having
    /// subscribed — i.e. prove nothing. That lives in CombatVfxPlayTests.
    /// </summary>
    public sealed class CombatVfxTests
    {
        [Test]
        public void HitSparkCount_ScalesWithDamage_CritsHarder_AndStaysClamped()
        {
            Assert.AreEqual(0, CombatVfxTuning.HitSparkCount(0f, false), "no damage, no sparks");
            Assert.That(CombatVfxTuning.HitSparkCount(4f, true),
                Is.GreaterThan(CombatVfxTuning.HitSparkCount(4f, false)), "a crit should throw more sparks");
            Assert.That(CombatVfxTuning.HitSparkCount(9999f, true), Is.EqualTo(12),
                "a huge hit must not emit an unbounded spark count");
            Assert.That(CombatVfxTuning.HitSparkCount(1f, false), Is.InRange(2, 12));
        }

        [Test]
        public void TrailSteps_FillsTheGapAcrossADashStep()
        {
            Assert.AreEqual(1, CombatVfxTuning.TrailSteps(0f), "a stationary frame still lays one puff");
            Assert.That(CombatVfxTuning.TrailSteps(2f), Is.GreaterThan(CombatVfxTuning.TrailSteps(0.2f)),
                "covering more ground in one frame needs more puffs, or the trail is dotted");
            Assert.That(CombatVfxTuning.TrailSteps(999f), Is.EqualTo(8), "clamped — no unbounded emission");
        }

        [Test]
        public void Burst_EmitsUpToItsPerFrameBudgetThenStops()
        {
            var burst = new VfxBurst("test-burst", VfxMaterials.Additive(VfxMaterials.Glow()),
                maxParticles: 100, gravity: 1f, perFrameCap: 3);
            try
            {
                int ok = 0;
                for (int i = 0; i < 20; i++)
                {
                    if (burst.Emit(Vector3.zero, 4, Vector3.up, 45f, 1f, 2f, 0.1f, 0.2f, 0.2f, 0.4f,
                            Color.white, Color.white)) ok++;
                }
                Assert.AreEqual(3, ok, "bursts past the frame budget must be dropped");

                burst.EndFrame();
                Assert.IsTrue(burst.Emit(Vector3.zero, 4, Vector3.up, 45f, 1f, 2f, 0.1f, 0.2f, 0.2f, 0.4f,
                    Color.white, Color.white), "the budget must refill on the next frame");
            }
            finally
            {
                Object.DestroyImmediate(burst.GameObject);
            }
        }

        [Test]
        public void Burst_HasAMaterial()
        {
            var burst = new VfxBurst("test-mat", VfxMaterials.Additive(VfxMaterials.Glow()),
                maxParticles: 10, gravity: 0f, perFrameCap: 1);
            try
            {
                var r = burst.GameObject.GetComponent<ParticleSystemRenderer>();
                Assert.IsNotNull(r.sharedMaterial,
                    "no material — AddComponent<ParticleSystem> leaves it null and the burst would be invisible");
            }
            finally
            {
                Object.DestroyImmediate(burst.GameObject);
            }
        }

    }
}
