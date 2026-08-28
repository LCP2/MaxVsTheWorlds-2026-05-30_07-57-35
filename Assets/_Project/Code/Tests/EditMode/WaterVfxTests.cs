using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

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

        // --- YT-177: reach must not shrink as the cone widens ---

        [Test]
        public void ReachForCone_AtZeroAngle_MatchesTheOldStraightLineSubtraction()
        {
            float reach = WaterVfxTuning.ReachForCone(range: 4.5f, muzzleOffset: 2.09f, halfAngleDeg: 0f);
            Assert.That(reach, Is.EqualTo(4.5f - 2.09f).Within(0.001f));
        }

        [Test]
        public void ReachForCone_TheConeEdgeLandsExactlyOnTheOutline()
        {
            const float range = 4.5f, offset = 2.09f, angleDeg = 24f;   // the base/wide weapon's stream edge
            float reach = WaterVfxTuning.ReachForCone(range, offset, angleDeg);

            // Reconstruct the edge particle's landing point: it leaves the muzzle (offset along
            // local Z from the weapon's origin) at angleDeg and travels `reach` in a straight line.
            float rad = angleDeg * Mathf.Deg2Rad;
            var muzzle = new Vector3(0f, 0f, offset);
            var dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            var landing = muzzle + dir * reach;

            Assert.That(landing.magnitude, Is.EqualTo(range).Within(0.01f),
                "the widest visible particle must land exactly on the aim outline, not short of it (YT-177)");
        }

        [Test]
        public void ReachForCone_GrowsAsTheConeWidens_ToCompensateForTheAngle()
        {
            const float range = 4.5f, offset = 2.09f;
            float r0 = WaterVfxTuning.ReachForCone(range, offset, 0f);
            float r24 = WaterVfxTuning.ReachForCone(range, offset, 24f);
            float r45 = WaterVfxTuning.ReachForCone(range, offset, 45f);

            Assert.That(r24, Is.GreaterThan(r0), "a wider cone must get more reach, not the same fixed amount");
            Assert.That(r45, Is.GreaterThan(r24));
        }

        [Test]
        public void ReachForCone_NeverGoesNegativeForAnExtremeAngle()
        {
            float reach = WaterVfxTuning.ReachForCone(range: 1f, muzzleOffset: 5f, halfAngleDeg: 89f);
            Assert.That(reach, Is.GreaterThanOrEqualTo(0.1f));
        }

        // --- MV-403: density must scale with reach, so a longer beam doesn't thin into a "fan" ---

        [Test]
        public void DensityScaleForReach_IsOneAtTheBaseReach()
        {
            Assert.That(WaterVfxTuning.DensityScaleForReach(5f, 5f), Is.EqualTo(1f).Within(1e-5f),
                "an un-upgraded weapon's stream density must be untouched");
        }

        [Test]
        public void DensityScaleForReach_GrowsLinearlyPastTheBaseReach()
        {
            float atBase = WaterVfxTuning.DensityScaleForReach(5f, 5f);
            float atDouble = WaterVfxTuning.DensityScaleForReach(10f, 5f);

            Assert.That(atDouble, Is.EqualTo(atBase * 2f).Within(1e-4f),
                "doubling reach must double the emission rate, holding droplets-per-metre constant");
        }

        [Test]
        public void DensityScaleForReach_NeverGoesNonPositiveForAnExtremeReach()
        {
            Assert.That(WaterVfxTuning.DensityScaleForReach(0f, 5f), Is.GreaterThan(0f));
            Assert.That(WaterVfxTuning.DensityScaleForReach(-3f, 5f), Is.GreaterThan(0f));
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
        public void WaterVfx_AWiderConeGetsMoreEmitterSpeed_AtTheSameRange()
        {
            // The base/wide weapon (YT-177): at the same range, a wider cone loses more reach to the
            // muzzle-offset geometry, so its emitter must be given more speed to still land on the
            // outline at its own (wider) edge angle.
            var narrowGo = new GameObject("blaster-vfx-narrow");
            var wideGo = new GameObject("blaster-vfx-wide");
            try
            {
                var narrow = narrowGo.AddComponent<WaterVfx>();
                narrow.Init(range: 4.5f, radius: 1.1f, coneHalfAngle: 6f);

                var wide = wideGo.AddComponent<WaterVfx>();
                wide.Init(range: 4.5f, radius: 1.1f, coneHalfAngle: 48f);

                Assert.That(wide.EmitterSpeed, Is.GreaterThan(narrow.EmitterSpeed),
                    "the wide weapon's stream must be sped up to cover the extra distance its angle costs it");
            }
            finally
            {
                Object.DestroyImmediate(narrowGo);
                Object.DestroyImmediate(wideGo);
            }
        }

        // --- MV-379: visual-only strength scaling, decoupled from the cone ---

        [Test]
        public void VisualStrength_ScalesStreamAndMuzzleSizeAndRate()
        {
            var weakGo = new GameObject("blaster-vfx-weak");
            var fullGo = new GameObject("blaster-vfx-full");
            try
            {
                var weak = weakGo.AddComponent<WaterVfx>();
                weak.Init(range: 6f, radius: 1.1f, coneHalfAngle: 8f, visualStrength: 0f);

                var full = fullGo.AddComponent<WaterVfx>();
                full.Init(range: 6f, radius: 1.1f, coneHalfAngle: 8f, visualStrength: 1f);

                var weakStream = weakGo.transform.Find("WaterStream").GetComponent<ParticleSystem>();
                var fullStream = fullGo.transform.Find("WaterStream").GetComponent<ParticleSystem>();

                Assert.That(weakStream.main.startSize.constantMax, Is.LessThan(fullStream.main.startSize.constantMax),
                    "an un-upgraded (visualStrength 0) stream must read thinner near the muzzle than the fully-invested one");
                Assert.That(weakStream.emission.rateOverTime.constant, Is.LessThan(fullStream.emission.rateOverTime.constant),
                    "an un-upgraded stream must also be sparser (lower particle rate), not just thinner");

                var weakMuzzle = weakGo.transform.Find("WaterMuzzle").GetComponent<ParticleSystem>();
                var fullMuzzle = fullGo.transform.Find("WaterMuzzle").GetComponent<ParticleSystem>();

                Assert.That(weakMuzzle.main.startSize.constant, Is.LessThan(fullMuzzle.main.startSize.constant),
                    "an un-upgraded weapon's muzzle burst must be visibly smaller");
                Assert.That(weakMuzzle.emission.rateOverTime.constant, Is.LessThan(fullMuzzle.emission.rateOverTime.constant),
                    "an un-upgraded weapon's muzzle burst must be sparser too");
            }
            finally
            {
                Object.DestroyImmediate(weakGo);
                Object.DestroyImmediate(fullGo);
            }
        }

        [Test]
        public void VisualStrength_NeverMovesTheStreamAngleAwayFromTheRealCone()
        {
            // AC3: the visual size/density dials are decoupled from the cone, but the stream's ANGLE
            // must still describe the same weapon the hit test and reticle use, at any visual strength.
            var go = new GameObject("blaster-vfx-angle");
            try
            {
                var vfx = go.AddComponent<WaterVfx>();
                vfx.Init(range: 6f, radius: 1.1f, coneHalfAngle: 8f, visualStrength: 0f);

                Assert.That(vfx.StreamHalfAngle, Is.EqualTo(WaterVfx.SprayHalfAngleFor(8f)).Within(0.01f),
                    "the stream's angle must still match the real cone even at the weakest visual strength");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void VisualStrength_DefaultsToFullForCallersThatDoNotPassIt()
        {
            // Backward compatibility: every pre-MV-379 call site (and test) that doesn't pass
            // visualStrength must keep behaving exactly as before.
            var withDefault = new GameObject("blaster-vfx-default");
            var explicitFull = new GameObject("blaster-vfx-explicit-full");
            try
            {
                var a = withDefault.AddComponent<WaterVfx>();
                a.Init(range: 6f, radius: 1.1f, coneHalfAngle: 8f);

                var b = explicitFull.AddComponent<WaterVfx>();
                b.Init(range: 6f, radius: 1.1f, coneHalfAngle: 8f, visualStrength: 1f);

                var streamA = withDefault.transform.Find("WaterStream").GetComponent<ParticleSystem>();
                var streamB = explicitFull.transform.Find("WaterStream").GetComponent<ParticleSystem>();

                Assert.That(streamA.main.startSize.constantMax, Is.EqualTo(streamB.main.startSize.constantMax).Within(1e-4f));
                Assert.That(streamA.emission.rateOverTime.constant, Is.EqualTo(streamB.emission.rateOverTime.constant).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(withDefault);
                Object.DestroyImmediate(explicitFull);
            }
        }

        [Test]
        public void WaterVfx_ALongerReachRaisesStreamDensity_WithoutMovingTheAngle_MV403()
        {
            // Reproduces Lee's MV-403 report end-to-end: Range maxed, Spread untouched. The stream's
            // ANGLE must stay exactly the base cone's — Range never touches ConeHalfAngle — but its
            // emission rate must climb so the same beam doesn't thin into a sparse, gappy "fan" once
            // it's stretched to the Range track's extended reach.
            var shortGo = new GameObject("blaster-vfx-short-reach");
            var longGo = new GameObject("blaster-vfx-long-reach");
            try
            {
                var atBase = shortGo.AddComponent<WaterVfx>();
                atBase.Init(range: 5f, radius: 0.6f, coneHalfAngle: 8f);   // WaterBlaster.DefaultRange

                var atMaxedRange = longGo.AddComponent<WaterVfx>();
                atMaxedRange.Init(range: 10f, radius: 0.6f, coneHalfAngle: 8f);   // Range track maxed, Spread untouched

                Assert.That(atMaxedRange.StreamHalfAngle, Is.EqualTo(atBase.StreamHalfAngle).Within(0.01f),
                    "Range must never move the stream's angle — only Spread does");

                var baseStream = shortGo.transform.Find("WaterStream").GetComponent<ParticleSystem>();
                var longStream = longGo.transform.Find("WaterStream").GetComponent<ParticleSystem>();

                Assert.That(longStream.emission.rateOverTime.constant,
                    Is.GreaterThan(baseStream.emission.rateOverTime.constant),
                    "a beam stretched by the Range track must emit more droplets, or it reads as widening/fanning out");
            }
            finally
            {
                Object.DestroyImmediate(shortGo);
                Object.DestroyImmediate(longGo);
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

        // --- MV-555: the ground trail must read as part of the wedge, at every firing angle ---

        [Test]
        public void GroundTrail_EveryPointStaysInsideTheWedgesOwnFootprint_WhenAimingSideways()
        {
            // Lee's exact report: firing due LEFT/RIGHT is where the jet used to read as floating
            // clear of the outline. Build the trail's placement the same way WaterVfx.LateUpdate
            // does (WaterGroundTrailTuning.Placement — no live frame needed) and prove every vertex
            // of its mesh — which is AimReticleMesh's own wedge, reused verbatim — lands inside the
            // identical cone SprayHit.InCone (and therefore FireTick) uses to decide a hit. This is
            // resolved-world-position coverage (Tier 2), not a check that a component exists.
            const float range = 6f, coneHalfAngle = 24f;
            Vector3 ownerPos = new Vector3(3f, 1f, -2f);

            foreach (Vector3 dir in new[] { Vector3.left, Vector3.right })
            {
                WaterGroundTrailTuning.Placement(ownerPos, dir, WaterVfx.GroundTrailLift,
                    out Vector3 pos, out Quaternion rot);

                var mesh = AimReticleMesh.Build(range, coneHalfAngle);
                try
                {
                    foreach (Vector3 v in mesh.vertices)
                    {
                        Vector3 world = pos + rot * v;
                        // A tiny slack on the range: the fade ring's outermost vertices sit AT
                        // DrawnReach exactly, and the LookRotation used to place the trail (see
                        // WaterGroundTrailTuning.Placement) can perturb that by float-precision noise
                        // — not a real gap between the trail and the wedge it's built from.
                        Assert.IsTrue(
                            SprayHit.InCone(ownerPos, dir, world, AimReticleMesh.DrawnReach(range) + 0.01f, coneHalfAngle + 0.5f),
                            $"ground trail vertex {world} (aiming {dir}) fell outside the wedge's own footprint");
                    }
                }
                finally { Object.DestroyImmediate(mesh); }
            }

            // Stacking (same rule AimReticleTests.EveryGroundMarkThatMattersDrawsOverTheReticle
            // pins for the wedge itself): the trail must draw OVER the idle wedge but under anything
            // an actor's own ground marks use, or it could bury a contact shadow/anchor/telegraph.
            Assert.Greater(WaterVfx.GroundTrailLift, AimReticle.GroundLift,
                "the trail must draw over the wedge it's tied to, or it reads as buried under it");
            Assert.Less(WaterVfx.GroundTrailLift, GroundAnchorTuning.ShadowLift,
                "the trail must never be able to cover an actor's contact shadow, anchor ring, or telegraph");
        }

        // --- MV-617: the visible stream must still land on the outline after a nozzle/track refit ---

        // Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode (confirmed
        // empirically for AreaGate — see WaterBlasterGateDamageTests's MV-386 note) — drive Awake
        // directly, then call the same public RefreshUpgrades() a real RCDA/nozzle spend calls
        // (normally wired through WeaponSystemState.Changed, which needs OnEnable to subscribe).
        private static void InvokeAwake(Object component)
        {
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
        }

        [Test]
        public void ComputedLandingDistance_MatchesRange_BeforeAndAfterASizeScaleRefit_MV617()
        {
            // Reproduces Lee's report end-to-end via the real WaterBlaster/RigState path (same idiom
            // as WaterBlasterConeWidthTests): at both the base and maxed Range track level, the
            // stream's own emitter must still land on the aim outline after a SizeScale change
            // (driven by a Spread spend, MV-379) forces a live Refit() — WaterVfx.Refit() previously
            // rescaled the emitter's speed/lifetime but never moved its transform, so after any
            // _radius change the nozzle kept firing from its OLD position while the reach math
            // assumed the NEW one.
            foreach (bool maxRange in new[] { false, true })
            {
                WeaponSystemState.Reset();
                RigState.Reset();
                var go = new GameObject("blaster-vfx-landing");
                try
                {
                    var blaster = go.AddComponent<WaterBlaster>();
                    InvokeAwake(blaster);

                    if (maxRange)
                    {
                        RigState.AcquireCap("p_rng");
                        for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Range); i++)
                            WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range);
                    }
                    blaster.RefreshUpgrades();

                    var vfx = go.GetComponent<WaterVfx>();
                    float tolerance = blaster.Range * 0.05f;
                    string level = maxRange ? "max" : "1";

                    Assert.That(vfx.ComputedLandingDistance, Is.EqualTo(blaster.Range).Within(tolerance),
                        $"stream must land on the outline before any SizeScale refit (p_rng level {level})");

                    // Trigger a live Refit() via a SizeScale change — a Spread spend (MV-379), the
                    // same path a real RCDA/nozzle upgrade takes.
                    RigState.AcquireCap("p_rng");
                    RigState.AcquireCap("p_spr");
                    WeaponSystemState.LevelUpTrack(WeaponTrackKind.Spread);
                    blaster.RefreshUpgrades();

                    Assert.That(vfx.ComputedLandingDistance, Is.EqualTo(blaster.Range).Within(tolerance),
                        $"stream must still land on the outline AFTER a SizeScale-triggered Refit " +
                        $"(p_rng level {level}) — Refit() must reposition the emitter, not just its speed/lifetime");
                }
                finally
                {
                    Object.DestroyImmediate(go);
                    WeaponSystemState.Reset();
                    RigState.Reset();
                }
            }
        }
    }
}
