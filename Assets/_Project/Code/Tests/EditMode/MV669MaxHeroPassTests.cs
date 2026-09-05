using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-669 — Lee: "Make max look way cooler and 10% bigger." This covers the size half, which is
    /// the half with a measurable answer: the rig's model root (<c>MaxRig.Build</c>'s <c>_body</c>
    /// pivot) now carries a <see cref="MaxRig.VisualScale"/> of 1.1, applied to the RENDERED body
    /// only — never to <see cref="EnemyArchetype.PlayerHeight"/>/<see
    /// cref="EnemyArchetype.PlayerRadius"/> or Max's <c>CharacterController</c>, which every robot's
    /// body-separation clamp, the spawn-height maths and the YT-74 "nothing out-sizes Max" rule all
    /// key off.
    ///
    /// EditMode, because every claim here is about BUILT GEOMETRY (a renderer's bounds after
    /// <see cref="MaxBody.Build"/>/<see cref="RobotBodies.Build"/> place it), not about a running
    /// scene — the same idiom <see cref="MV474MaxWalkTests"/> and <see cref="MaxRigTests"/> already
    /// use for this rig. Must fail to even compile on the base commit (pre-MV-669): <c>MaxRig</c>
    /// has no <c>VisualScale</c> member there, the same "doesn't exist yet" failure mode MV-474's own
    /// test documents for this file.
    /// </summary>
    public sealed class MV669MaxHeroPassTests
    {
        private const float HipY = 0.74f; // MaxRig.HipY (private) — the waist height the rig builds at.

        private static MaxPalette NullPalette() =>
            new MaxPalette(null, null, null, null, null, null, null, null, null, null, null);

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

        /// <summary>AC1: the rendered bounds height is 10% (+/-1%) greater than the unscaled build —
        /// the same geometry, the only difference being <see cref="MaxRig.VisualScale"/> at the model
        /// root, which is exactly what shipped.</summary>
        [Test]
        public void MaxIsRenderedTenPercentTaller_AtTheModelRootPivot()
        {
            float baselineHeight = BuildAndMeasure(1f).size.y;
            float scaledHeight = BuildAndMeasure(MaxRig.VisualScale).size.y;

            float ratio = scaledHeight / baselineHeight;
            Assert.That(ratio, Is.EqualTo(1.10f).Within(0.01f),
                $"Max's rendered height scaled by {ratio:0.000}x, not the 10% (+/-1%) the ticket asks " +
                "for.");
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
    }
}
