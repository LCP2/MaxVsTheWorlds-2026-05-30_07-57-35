using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-47 — water-blaster VFX. Covers the pure placement/scaling maths, and guards the
    /// one failure mode that is invisible in the editor but fatal in a build: a particle
    /// system with no material (AddComponent leaves the renderer's material null) or one
    /// bound to the magenta error shader.
    /// </summary>
    public sealed class WaterVfxTests
    {
        // --- tuning maths ---

        [Test]
        public void SplashDroplets_ScalesWithDamage_AndStaysClamped()
        {
            Assert.AreEqual(0, WaterVfxTuning.SplashDroplets(0f), "no damage, no splash");
            Assert.That(WaterVfxTuning.SplashDroplets(1f), Is.InRange(4, 14));
            Assert.That(WaterVfxTuning.SplashDroplets(4f), Is.InRange(4, 14));
            Assert.That(WaterVfxTuning.SplashDroplets(9999f), Is.EqualTo(14),
                "an absurd damage value must not emit an unbounded droplet count");
            Assert.That(WaterVfxTuning.SplashDroplets(20f),
                Is.GreaterThan(WaterVfxTuning.SplashDroplets(2f)),
                "a bigger hit should splash harder");
        }

        [Test]
        public void NearestPointOnRay_ProjectsOntoTheStreamAxis()
        {
            var origin = Vector3.zero;
            var dir = Vector3.forward;

            // Target off to the side at z=3 projects back onto the axis at (0,0,3).
            var p = WaterVfxTuning.NearestPointOnRay(origin, dir, 6f, new Vector3(2f, 0f, 3f));
            Assert.That(Vector3.Distance(p, new Vector3(0f, 0f, 3f)), Is.LessThan(1e-4f));
        }

        [Test]
        public void NearestPointOnRay_ClampsBehindAndBeyondTheStream()
        {
            var origin = Vector3.zero;
            var dir = Vector3.forward;

            var behind = WaterVfxTuning.NearestPointOnRay(origin, dir, 6f, new Vector3(0f, 0f, -5f));
            Assert.That(Vector3.Distance(behind, origin), Is.LessThan(1e-4f),
                "a target behind the muzzle clamps to the muzzle, never behind it");

            var beyond = WaterVfxTuning.NearestPointOnRay(origin, dir, 6f, new Vector3(0f, 0f, 99f));
            Assert.That(Vector3.Distance(beyond, new Vector3(0f, 0f, 6f)), Is.LessThan(1e-4f),
                "the splash never lands past the stream's reach");
        }

        [Test]
        public void NearestPointOnRay_ToleratesAZeroDirection()
        {
            var p = WaterVfxTuning.NearestPointOnRay(Vector3.zero, Vector3.zero, 6f, new Vector3(0f, 0f, 3f));
            Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z));
        }

        [Test]
        public void SplashAxis_SpraysBackTowardTheShooterAndUpward()
        {
            var axis = WaterVfxTuning.SplashAxis(Vector3.forward);

            Assert.That(axis.magnitude, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(axis.y, Is.GreaterThan(0f), "splash must rise, or the ~72° camera can't see it");
            Assert.That(Vector3.Dot(axis, Vector3.forward), Is.LessThan(0f),
                "splash must kick back against the stream, not continue through the target");
        }

        // --- the build-fatal failure mode: unrendered particles ---

        [Test]
        public void ParticleMaterials_ResolveToASupportedShader()
        {
            var mat = VfxMaterials.AlphaBlend(VfxMaterials.Droplet());

            Assert.IsNotNull(mat, "no particle material — every VFX would render as nothing");
            Assert.IsNotNull(mat.shader);
            Assert.IsTrue(mat.shader.isSupported, $"shader not supported on this platform: {mat.shader.name}");
            Assert.That(mat.shader.name, Does.Not.Contain("InternalErrorShader"),
                "fell through to the magenta error shader");
        }

        [Test]
        public void ParticleMaterials_AreCachedPerTextureAndBlendMode()
        {
            var a = VfxMaterials.Additive(VfxMaterials.Glow());
            var b = VfxMaterials.Additive(VfxMaterials.Glow());
            var alpha = VfxMaterials.AlphaBlend(VfxMaterials.Glow());

            Assert.AreSame(a, b, "materials must be cached, not rebuilt per effect");
            Assert.AreNotSame(a, alpha, "additive and alpha-blend are different materials");
        }

        [Test]
        public void Droplet_HasASolidCoreAndATransparentEdge()
        {
            var tex = VfxMaterials.Droplet(64);
            Assert.AreEqual(1f, tex.GetPixel(32, 32).a, 0.01f, "centre must be opaque");
            Assert.AreEqual(0f, tex.GetPixel(0, 0).a, 0.01f, "corner must be fully transparent");
        }

        [Test]
        public void WaterVfx_BuildsEverySystemWithAMaterialAssigned()
        {
            var go = new GameObject("blaster-vfx-test");
            try
            {
                var vfx = go.AddComponent<WaterVfx>();
                vfx.Init(range: 6f, radius: 0.6f, coneHalfAngle: 35f);

                var systems = go.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
                Assert.That(systems.Length, Is.GreaterThanOrEqualTo(3),
                    "expected at least the stream, its core, and the muzzle under the blaster");

                foreach (var ps in systems)
                {
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    Assert.IsNotNull(r.sharedMaterial,
                        $"'{ps.name}' has no material — AddComponent<ParticleSystem> leaves it null, " +
                        "so it would draw nothing in a build");
                    Assert.IsFalse(ps.main.playOnAwake, $"'{ps.name}' must not self-start");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void WaterVfx_SplashIsCappedPerFrame()
        {
            var go = new GameObject("blaster-vfx-cap");
            try
            {
                var vfx = go.AddComponent<WaterVfx>();
                vfx.Init(range: 6f, radius: 0.6f, coneHalfAngle: 35f);

                // Far more impacts in one frame than the budget — a stream raking a crowd of
                // 20–30 enemies does exactly this, and uncapped it would spike the particle count.
                int emitted = 0;
                for (int i = 0; i < WaterVfxTuning.MaxSplashesPerFrame + 40; i++)
                {
                    if (vfx.Splash(new Vector3(i, 0f, 0f), Vector3.forward, damage: 4f)) emitted++;
                }

                Assert.AreEqual(WaterVfxTuning.MaxSplashesPerFrame, emitted,
                    "impacts past the frame's splash budget must be dropped, not queued");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
