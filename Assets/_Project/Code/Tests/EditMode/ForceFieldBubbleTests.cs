using NUnit.Framework;
using UnityEngine;
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
    }
}
