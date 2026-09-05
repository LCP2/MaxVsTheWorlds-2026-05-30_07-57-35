using System.Reflection;
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

        private static readonly BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

        private static Color PrivateStaticColor(string fieldName)
        {
            FieldInfo field = typeof(CombatVfx).GetField(fieldName, PrivateStatic);
            Assert.IsNotNull(field, $"CombatVfx must define a private static Color field named '{fieldName}'");
            return (Color)field.GetValue(null);
        }

        /// <summary>
        /// MV-674: Max's teleport crackle must resolve to Element.Electric's amber-yellow, not the
        /// beat's existing cyan-violet surge/shockwave. CombatVfx's own Awake/OnEnable don't run in
        /// edit mode (see the class comment above), so this can't drive MaxTeleportBeat directly —
        /// instead it reflects out the real ElectricCrackleCore/Hot and MaxTeleportCore/Deep fields
        /// (so it fails if either is renamed or removed, not just if it's absent) and pushes them
        /// through the actual VfxBurst.Emit path, reading back the ENGINE-RESOLVED particle colour
        /// via GetParticles() — a value the RNG-driven Color.Lerp inside Emit() actually computes,
        /// not just an authored constant restated.
        /// </summary>
        [Test]
        public void ElectricCrackleColour_ResolvesAmber_NotTheSurgesCyanViolet()
        {
            Color electricCore = PrivateStaticColor("ElectricCrackleCore");
            Color electricHot = PrivateStaticColor("ElectricCrackleHot");
            Color surgeCore = PrivateStaticColor("MaxTeleportCore");
            Color surgeDeep = PrivateStaticColor("MaxTeleportDeep");

            var crackle = new VfxBurst("test-crackle", VfxMaterials.Additive(VfxMaterials.Glow()),
                maxParticles: 50, gravity: 0f, perFrameCap: 4, stretched: true);
            var surge = new VfxBurst("test-surge", VfxMaterials.Additive(VfxMaterials.Glow()),
                maxParticles: 50, gravity: 0f, perFrameCap: 4, stretched: true);
            try
            {
                crackle.Emit(Vector3.zero, 20, Vector3.up, 150f, 3f, 13f, 0.05f, 0.14f, 0.1f, 0.2f,
                    electricHot, electricCore);
                surge.Emit(Vector3.zero, 20, Vector3.up, 100f, 2.5f, 7f, 0.16f, 0.4f, 0.3f, 0.55f,
                    surgeCore, surgeDeep);

                var crackleSystem = crackle.GameObject.GetComponent<ParticleSystem>();
                var crackleParticles = new ParticleSystem.Particle[crackleSystem.particleCount];
                int crackleCount = crackleSystem.GetParticles(crackleParticles);

                var surgeSystem = surge.GameObject.GetComponent<ParticleSystem>();
                var surgeParticles = new ParticleSystem.Particle[surgeSystem.particleCount];
                int surgeCount = surgeSystem.GetParticles(surgeParticles);

                Assert.That(crackleCount, Is.GreaterThan(0), "the crackle burst must actually emit particles to read back");
                Assert.That(surgeCount, Is.GreaterThan(0), "the surge burst must actually emit particles to read back");

                for (int i = 0; i < crackleCount; i++)
                {
                    Color32 c = crackleParticles[i].startColor;
                    Assert.That(c.r, Is.GreaterThan(c.b),
                        "the crackle must resolve amber-yellow (red above blue) like Element.Electric");
                }

                for (int i = 0; i < surgeCount; i++)
                {
                    Color32 c = surgeParticles[i].startColor;
                    Assert.That(c.b, Is.GreaterThan(c.r),
                        "the existing surge must still resolve cyan-violet (blue above red) — sanity check on the comparison itself");
                }
            }
            finally
            {
                Object.DestroyImmediate(crackle.GameObject);
                Object.DestroyImmediate(surge.GameObject);
            }
        }
    }
}
