using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-669 — Lee: "Make max look way cooler and 10% bigger." This covers the size half, which is
    /// the half with a measurable answer.
    ///
    /// EditMode, because every claim here is about BUILT GEOMETRY (a renderer's bounds after
    /// <see cref="MaxBody.Build"/>/<see cref="RobotBodies.Build"/> place it), not about a running
    /// scene — the same idiom <see cref="MV474MaxWalkTests"/> and <see cref="MaxRigTests"/> already
    /// use for this rig.
    ///
    /// Extended for the approved-geometry follow-up (Lee's second comment on the ticket, 2026-09-05):
    /// <see cref="GadgetGlowSurvivesTheRebuild"/> (A2), <see
    /// cref="BuiltPartCountIsTheApprovedThirtyTwoPlusThePortedGadget"/> (A3) and <see
    /// cref="MaxPaletteCarriesExactlyTheTitleRevealBodysTwelveMaterialSlots"/> (A4, re-pointed at the
    /// MV-851 title-reveal palette — see that test's own doc). A1 (the hip pivots survive) is already
    /// covered by <see cref="MV474MaxWalkTests"/>, which asserts the same claim against whatever
    /// geometry <c>MaxBody.Build</c> currently produces.
    ///
    /// Revision 2 (Lee's playtest, 2026-09-06): the +10% was rejected on device, so <see
    /// cref="MaxIsBackToHisPreMV669Height"/> replaces the old "10% taller" assertion — it must fail
    /// on the base commit for this revision (524fad5, <c>VisualScale</c> still 1.1) and pass once
    /// <c>VisualScale</c> is reverted to 1 and the re-tuned block lands back within 2% of the
    /// pre-MV-669 ~1.95 m crown.
    ///
    /// Revision 3 (the Heavy conflict, 2026-09-06): reverting the +10% re-exposed a pre-existing
    /// YT-74 gap the inflation had been masking — <see cref="NothingInTheSwarmOutSizesTheTallerMax"/>
    /// went red on Heavy (1.997 m against the reverted Max's 1.9876 m), the base commit for THIS
    /// revision (a642436). Decision: trim Heavy (<see cref="RobotBodies.VisualTrimRootFor"/>), not
    /// re-inflate Max. No new test method — this already-existing parametrized case is the evidence:
    /// red on a642436, green once the trim lands. R5 (Heavy's collider/health/damage/speed untouched)
    /// and R6 (no amber goggle literal survives outside <see cref="MaxRig.LensGlass"/>) are satisfied
    /// structurally — <c>EnemyArchetype.cs</c> is untouched by this revision's diff, and a grep for
    /// the old amber value (1, 0.72, 0.24) across Assets/_Project/Code returns zero hits — rather than
    /// by a new test asserting an authored constant, which the testing policy (MV-465, Tier 1) bans
    /// and which could not be made to fail first (there is no defect in those fields to prove).
    /// </summary>
    public sealed class MV669MaxHeroPassTests
    {
        private const float HipY = 0.74f; // MaxRig.HipY (private) — the waist height the rig builds at.

        private static MaxPalette NullPalette() =>
            new MaxPalette(null, null, null, null, null, null, null, null, null, null, null, null);

        /// <summary>
        /// Builds Max's body under a "Body" pivot scaled by <paramref name="bodyScale"/>, mirroring
        /// <c>MaxRig.Build</c>'s own hierarchy (Body -&gt; Torso -&gt; Feet -&gt; the generated mesh),
        /// and hands back the combined RENDERED bounds of every part — read off the real
        /// <see cref="MeshRenderer"/>s, not an authored constant.
        /// </summary>
        private static Bounds BuildAndMeasure(float bodyScale)
        {
            var body = new GameObject("Body").transform;
            try
            {
                body.localScale = Vector3.one * bodyScale;
                var torso = new GameObject("Torso").transform;
                torso.SetParent(body, worldPositionStays: false);
                torso.localPosition = new Vector3(0f, HipY, 0f);
                var feet = new GameObject("Feet").transform;
                feet.SetParent(torso, worldPositionStays: false);
                feet.localPosition = new Vector3(0f, -HipY, 0f);

                MaxBody.Build(feet, NullPalette(), HipY);

                var renderers = body.GetComponentsInChildren<MeshRenderer>();
                Assert.That(renderers.Length, Is.GreaterThan(0), "MaxBody built no renderers to measure.");

                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                return b;
            }
            finally
            {
                Object.DestroyImmediate(body.gameObject);
            }
        }

        /// <summary>R1 (revision 2 — supersedes the old AC1): Lee rejected the +10% on device, so
        /// <see cref="MaxRig.VisualScale"/> is reverted to 1 and Max must render within 2% of the
        /// pre-MV-669 crown height (~1.95 m, the value <c>MaxBody</c>'s own doc comment carried before
        /// this ticket touched the geometry). The re-emitted block's own shorter/thinner legs and
        /// dropped upper body are what actually land this, not a compensating root scale.</summary>
        [Test]
        public void MaxIsBackToHisPreMV669Height()
        {
            const float PreMV669CrownHeight = 1.95f;

            float builtHeight = BuildAndMeasure(MaxRig.VisualScale).size.y;

            Assert.That(builtHeight, Is.EqualTo(PreMV669CrownHeight).Within(PreMV669CrownHeight * 0.02f),
                $"Max's rendered height is {builtHeight:0.000} m, not within 2% of the pre-MV-669 " +
                $"{PreMV669CrownHeight:0.00} m crown — the +10% revert or the block's re-tuning didn't " +
                "land where the ticket expects.");
        }

        /// <summary>AC3: scaling about the ground pivot must not float or sink him.</summary>
        [Test]
        public void MaxsFeetStayOnTheGround_AfterTheScale()
        {
            float lowestY = BuildAndMeasure(MaxRig.VisualScale).min.y;

            Assert.That(lowestY, Is.EqualTo(0f).Within(0.02f),
                $"Max's lowest rendered vertex sits at y={lowestY:0.000} in local space — more than " +
                "2 cm off the ground. The scale is pivoting from somewhere other than the ground plane.");
        }

        /// <summary>AC2: the collider and the archetype it is built from never moved. The rig scales a
        /// transform that follows Max (<c>MaxRig.Follow</c>) and never touches his own GameObject, so
        /// this should hold trivially — this is the regression guard that keeps it that way.</summary>
        [Test]
        public void TheColliderAndArchetypeConstants_AreUntouchedByTheVisualScale()
        {
            Assert.That(EnemyArchetype.PlayerHeight, Is.EqualTo(2f),
                "PlayerHeight moved. The ticket is explicit: scale the VISUAL only.");
            Assert.That(EnemyArchetype.PlayerRadius, Is.EqualTo(0.5f),
                "PlayerRadius moved. The ticket is explicit: scale the VISUAL only.");

            var go = new GameObject("MaxCapsule");
            try
            {
                var cc = go.AddComponent<CharacterController>();
                Assert.That(cc.height, Is.EqualTo(EnemyArchetype.PlayerHeight).Within(0.001f),
                    "Max's CharacterController height no longer matches PlayerHeight.");
                Assert.That(cc.radius, Is.EqualTo(EnemyArchetype.PlayerRadius).Within(0.001f),
                    "Max's CharacterController radius no longer matches PlayerRadius.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>AC4: YT-74 still holds once Max is 10% taller — every robot kind's own rendered
        /// bounds height (built the same real-geometry way <see cref="RobotBodies"/> always has:
        /// <c>ParentScale.MakeMetreSpace</c> already cancels <see cref="EnemyArchetype.BodyScale"/>
        /// before placement, so this is the actual on-screen size) must stay below the now-taller
        /// Max.</summary>
        [TestCase(EnemyKind.Rusher)]
        [TestCase(EnemyKind.Bruiser)]
        [TestCase(EnemyKind.Heavy)]
        [TestCase(EnemyKind.Brute)]
        [TestCase(EnemyKind.Gunner)]
        [TestCase(EnemyKind.Launcher)]
        [TestCase(EnemyKind.Blinker)]
        [TestCase(EnemyKind.Bolter)]
        public void NothingInTheSwarmOutSizesTheTallerMax(EnemyKind kind)
        {
            float maxHeight = BuildAndMeasure(MaxRig.VisualScale).size.y;

            var root = new GameObject("Robot").transform;
            try
            {
                RobotBodies.Build(kind, root, new RobotPalette(null, null, null, null));

                var renderers = root.GetComponentsInChildren<MeshRenderer>();
                Assert.That(renderers.Length, Is.GreaterThan(0), $"{kind} built no renderers to measure.");

                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

                Assert.That(b.size.y, Is.LessThan(maxHeight),
                    $"{kind}'s rendered bounds height ({b.size.y:0.00} m) is not below the now-taller " +
                    $"Max's ({maxHeight:0.00} m) — YT-74 says nothing in the swarm may out-size him.");
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        /// <summary>A2 (approved-geometry follow-up): the gadget glow (the tank window and the nozzle
        /// tip) must survive the rebuild, or <c>MaxRig.TickGadget</c>'s present/aim tint has nothing to
        /// colour.</summary>
        [Test]
        public void GadgetGlowSurvivesTheRebuild()
        {
            var root = new GameObject("Root").transform;
            try
            {
                var body = MaxBody.Build(root, NullPalette(), HipY);
                Assert.That(body.GadgetGlow, Is.Not.Empty,
                    "MaxBody.Build returned no gadget glow renderers — MaxRig.TickGadget tints these " +
                    "every frame and the present/aim lerp has nothing to colour without at least one.");
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        /// <summary>A3 (approved-geometry follow-up), superseded by MV-851: the title-reveal body
        /// builds 37 renderers — legs (5 per leg: thigh, shorts hem, shin, sole, boot × 2 = 10), torso
        /// (tunic, collar, belt, one pouch, neck = 5), head (skin + per-side eye/pupil/brow × 2 + hair
        /// cap + back-of-head mass = 9), arms (one bare-skin beam each = 2) and the unchanged RCDA
        /// gadget (7 solid parts + 2 glow lenses = 9) plus its two hand-grip knuckle balls (2). This is
        /// exactly the "a ticket's own changes make an existing count stale" case the culling policy
        /// allows for; the count itself is still a resolved value read off real renderers, not an
        /// authored constant.</summary>
        [Test]
        public void BuiltPartCountIsTheApprovedThirtyTwoPlusThePortedGadget()
        {
            var root = new GameObject("Root").transform;
            try
            {
                MaxBody.Build(root, NullPalette(), HipY);
                var renderers = root.GetComponentsInChildren<MeshRenderer>();
                Assert.That(renderers.Length, Is.EqualTo(37),
                    $"Built {renderers.Length} renderers, not the MV-851 title-reveal body's 37 (10 leg " +
                    "parts + 5 torso parts + 9 head parts + 2 arm beams + 9 gadget parts + 2 hand-grip " +
                    "knuckle balls).");
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        /// <summary>A4 (approved-geometry follow-up), superseded by MV-851: <c>MaxPalette</c> now
        /// carries exactly the title-reveal body's twelve material slots — <c>Jacket</c>, <c>Hood</c>,
        /// <c>Fabric</c>, <c>Goggle</c> and <c>Pouch</c> are gone (no hood, no goggles, and the one
        /// pouch now shares the belt's own material); <c>Tunic</c>, <c>TunicDark</c>, <c>Glove</c> and
        /// <c>Pupil</c> are new.</summary>
        [Test]
        public void MaxPaletteCarriesExactlyTheTitleRevealBodysTwelveMaterialSlots()
        {
            var fields = typeof(MaxPalette).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var names = new System.Collections.Generic.HashSet<string>();
            foreach (var f in fields) names.Add(f.Name);

            Assert.That(fields.Length, Is.EqualTo(12),
                $"MaxPalette has {fields.Length} public fields, not the MV-851 title-reveal body's 12.");

            foreach (var expected in new[] { "Skin", "Hair", "Tunic", "TunicDark", "Belt", "Boot",
                                             "Sole", "Glove", "Eye", "Pupil", "Dark", "Metal" })
            {
                Assert.That(names, Does.Contain(expected), $"MaxPalette lost or renamed its '{expected}' field.");
            }

            foreach (var retired in new[] { "Jacket", "Hood", "Fabric", "Goggle", "Pouch" })
            {
                Assert.That(names, Does.Not.Contain(retired),
                    $"MaxPalette still carries the retired '{retired}' field — MV-851 removed it.");
            }
        }
    }
}
