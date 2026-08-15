using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-361, and MV-378's own lesson applied up front rather than after a playtest finds it: a
    /// shield that exists to physically block robot bodies must actually BE solid at runtime — a
    /// disabled/trigger/mispositioned collider is a silent no-op, not a build failure. Mirrors
    /// <see cref="GateSolidityTests"/>'s checklist for <see cref="ForceFieldBubble"/>, plus the
    /// owner-only collision exemption (MV-361: "Max can still move... out of it") that a gate never
    /// needed.
    /// </summary>
    public sealed class ForceFieldBubbleTests
    {
        [Test]
        public void TheBubbleColliderIsSolidNonTriggerAndTheAuthoredRadius()
        {
            var owner = new GameObject("Owner");
            var ownerCc = owner.AddComponent<CharacterController>();
            var bubbleGo = new GameObject("Force Field Bubble");
            var bubble = bubbleGo.AddComponent<ForceFieldBubble>();
            try
            {
                bubble.Init(owner.transform, ownerCc, 1.5f);

                Assert.IsNotNull(bubble.Collider, "the bubble built no collider at all");
                Assert.IsTrue(bubble.Collider.enabled, "the bubble's collider starts disabled");
                Assert.IsFalse(bubble.Collider.isTrigger,
                    "a trigger would let a CharacterController pass straight through the shield (MV-378's bug, reintroduced)");
                Assert.That(bubble.Collider.radius, Is.EqualTo(1.5f).Within(1e-5f));

                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)
                Collider[] hits = Physics.OverlapSphere(owner.transform.position, 0.05f);
                Assert.Contains(bubble.Collider, hits,
                    "a physics query at the bubble's own centre does not find its collider there");
            }
            finally
            {
                Object.DestroyImmediate(bubbleGo);
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void TheBubbleIgnoresOnlyItsOwnersCharacterController()
        {
            var owner = new GameObject("Owner");
            var ownerCc = owner.AddComponent<CharacterController>();
            var otherRobot = new GameObject("Other Robot");
            var otherCc = otherRobot.AddComponent<CharacterController>();
            var bubbleGo = new GameObject("Force Field Bubble");
            var bubble = bubbleGo.AddComponent<ForceFieldBubble>();
            try
            {
                bubble.Init(owner.transform, ownerCc, 1.5f);

                Assert.IsTrue(Physics.GetIgnoreCollision(bubble.Collider, ownerCc),
                    "Max's own CharacterController must be exempted, or activating the field shoves him out of his own bubble");
                Assert.IsFalse(Physics.GetIgnoreCollision(bubble.Collider, otherCc),
                    "a robot's CharacterController must NOT be exempted, or it stops being solid to everyone but Max");
            }
            finally
            {
                Object.DestroyImmediate(bubbleGo);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(otherRobot);
            }
        }

        [Test]
        public void TheBubbleFollowsItsOwnerAsAChildTransform()
        {
            var owner = new GameObject("Owner");
            var ownerCc = owner.AddComponent<CharacterController>();
            var bubbleGo = new GameObject("Force Field Bubble");
            var bubble = bubbleGo.AddComponent<ForceFieldBubble>();
            try
            {
                bubble.Init(owner.transform, ownerCc, 1.5f);
                owner.transform.position = new Vector3(4f, 0f, -7f);

                Assert.That(bubbleGo.transform.position, Is.EqualTo(owner.transform.position),
                    "a personal bubble centred on Max must move with him, not stay where it was raised");
            }
            finally
            {
                Object.DestroyImmediate(bubbleGo);
                Object.DestroyImmediate(owner);
            }
        }

        /// <summary>
        /// MV-391 — the actual bug, not the symptom. The bubble's visual never called
        /// <c>Destroy</c>/turned orange because of a colour choice; <c>RuntimeSurfaceDirector</c>'s
        /// sweep claimed the renderer a frame after it spawned and stamped it with an opaque
        /// world-prop material, because nothing marked it as gameplay-driven. This is the regression
        /// test for the marker, mirroring <c>RuntimeSurfaceDirectorTests</c>' own checklist.
        /// </summary>
        [Test]
        public void TheVisualCarriesSelfDrivenTint_SoTheSurfaceSweepNeverClaimsIt()
        {
            var owner = new GameObject("Owner");
            var ownerCc = owner.AddComponent<CharacterController>();
            var bubbleGo = new GameObject("Force Field Bubble");
            var bubble = bubbleGo.AddComponent<ForceFieldBubble>();
            try
            {
                bubble.Init(owner.transform, ownerCc, 1.5f);

                var visual = bubbleGo.transform.Find("Visual");
                Assert.IsNotNull(visual, "Init() must build a child 'Visual' renderer");
                Assert.IsNotNull(visual.GetComponent<SelfDrivenTint>(),
                    "the shield's own MaterialPropertyBlock colour will be overwritten by " +
                    "RuntimeSurfaceDirector's sweep without this marker (MV-391)");

                var renderer = visual.GetComponent<MeshRenderer>();
                var before = renderer.sharedMaterial;
                RunSurfaceSweep();

                Assert.AreSame(before, renderer.sharedMaterial,
                    "RuntimeSurfaceDirector must never reassign the shield's material — a change " +
                    "here is the opaque-orange-sphere bug (MV-391) coming back");
            }
            finally
            {
                Object.DestroyImmediate(bubbleGo);
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void FreshAndDepletedColours_AreSubtleAndNeverOpaqueOrOrange()
        {
            var owner = new GameObject("Owner");
            var ownerCc = owner.AddComponent<CharacterController>();
            var bubbleGo = new GameObject("Force Field Bubble");
            var bubble = bubbleGo.AddComponent<ForceFieldBubble>();
            try
            {
                bubble.Init(owner.transform, ownerCc, 1.5f);
                var renderer = bubbleGo.transform.Find("Visual").GetComponent<MeshRenderer>();
                var mpb = new MaterialPropertyBlock();

                bubble.SetFraction(1f);
                renderer.GetPropertyBlock(mpb);
                Color fresh = mpb.GetColor(Shader.PropertyToID("_BaseColor"));
                Assert.Less(fresh.a, 0.2f,
                    "the DECISION (MV-391) is 'mostly-transparent' — a fresh field must stay subtle");
                Assert.That(fresh.r, Is.EqualTo(fresh.g).Within(0.02f).And.EqualTo(fresh.b).Within(0.02f),
                    "a fresh field must read as white, not orange/red (MV-391)");

                bubble.SetFraction(0f);
                renderer.GetPropertyBlock(mpb);
                Color empty = mpb.GetColor(Shader.PropertyToID("_BaseColor"));
                Assert.Less(empty.a, 0.6f,
                    "even about to pop, the field must never read as fully opaque (AC3, MV-391)");
            }
            finally
            {
                Object.DestroyImmediate(bubbleGo);
                Object.DestroyImmediate(owner);
            }
        }

        private static void RunSurfaceSweep()
        {
            var go = new GameObject("sweep-test-director");
            try
            {
                var director = go.AddComponent<RuntimeSurfaceDirector>();
                typeof(RuntimeSurfaceDirector).GetMethod("Sweep", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(director, null);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
