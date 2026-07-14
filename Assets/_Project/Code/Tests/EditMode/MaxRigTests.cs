using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Max carries the gadget, and he raises it to aim (YT-95).
    ///
    /// EditMode, because every claim here is about the POSE FUNCTION and not about a scene: where the
    /// gadget sits at a given presentation amount is pure arithmetic, and pure arithmetic should not
    /// need a running game — or a synthesised gamepad — to be held to account.
    /// </summary>
    public sealed class MaxRigTests
    {
        /// <summary>
        /// THE ONE THAT MATTERS. The water has to leave the gun.
        ///
        /// <c>WaterBlaster</c> casts its damage from <c>transform.position</c> — Max's capsule centre,
        /// which is <see cref="EnemyArchetype.PlayerHeight"/> / 2 off the lawn — and <c>WaterVfx</c>
        /// emits the jet from that same origin. Neither of them knows or cares where Max's arms are.
        /// So the gadget has to be brought to the water, because the water is never coming to it: if
        /// the aim pose drifts away from this height, the jet erupts from Max's chest while the thing
        /// he is holding points somewhere else entirely.
        ///
        /// This is the same contract the boss's rig signs from the other side (YT-90): its blade tip
        /// sits at 2.35 m because its charge hurts you at 2.4 m. The reach you can see is the reach
        /// that is real.
        /// </summary>
        [Test]
        public void TheGadgetIsHeldWhereTheWaterActuallyComesFrom()
        {
            const float firingOrigin = EnemyArchetype.PlayerHeight * 0.5f;   // 1.0 m — the capsule's centre

            Assert.That(MaxRig.BarrelHeight(1f), Is.EqualTo(firingOrigin).Within(0.05f),
                "Max presents the gadget at a height the Water Blaster does not fire from. The jet " +
                "will come out of his chest.");
        }

        /// <summary>At the hip when he runs, up when he aims — the GDD's line about Max, and the only
        /// thing on screen that says the gadget is live before a drop of water does.</summary>
        [Test]
        public void HeRaisesTheGadgetToAim()
        {
            float hip = MaxRig.BarrelHeight(0f);
            float aimed = MaxRig.BarrelHeight(1f);

            Assert.That(aimed, Is.GreaterThan(hip),
                "The gadget does not come UP when Max aims. It is supposed to go from a two-handed " +
                "hip carry to a presented weapon.");

            // And it is a real move, not a twitch — it has to be visible on a 30-pixel character.
            Assert.That(aimed - hip, Is.GreaterThan(0.10f),
                "The gadget rises by less than 10 cm. Nobody will see that.");
        }

        /// <summary>The gadget also comes FORWARD, not just up — he is presenting it at something, not
        /// doing a bicep curl.</summary>
        [Test]
        public void HePushesTheGadgetOutInFrontOfHim()
        {
            MaxRig.GadgetPose(0f, out Vector3 hip, out _);
            MaxRig.GadgetPose(1f, out Vector3 aimed, out _);

            Assert.That(aimed.z, Is.GreaterThan(hip.z),
                "Aiming does not push the gadget forward, so Max presents it by lifting it straight up.");
        }

        /// <summary>Aiming is a continuous move, not a snap: half-presented is half-way there. This is
        /// what lets <c>presentSpeed</c> ease the gun up instead of teleporting it.</summary>
        [Test]
        public void ThePresentationIsContinuous()
        {
            float half = MaxRig.BarrelHeight(0.5f);

            Assert.That(half, Is.GreaterThan(MaxRig.BarrelHeight(0f)));
            Assert.That(half, Is.LessThan(MaxRig.BarrelHeight(1f)));
        }

        /// <summary>Out-of-range presentation amounts clamp rather than extrapolate. Nothing should be
        /// able to shove the gadget through his own head.</summary>
        [Test]
        public void ThePoseClampsRatherThanExtrapolating()
        {
            Assert.That(MaxRig.BarrelHeight(4f), Is.EqualTo(MaxRig.BarrelHeight(1f)).Within(0.001f));
            Assert.That(MaxRig.BarrelHeight(-3f), Is.EqualTo(MaxRig.BarrelHeight(0f)).Within(0.001f));
        }

        /// <summary>
        /// Max is the biggest thing in the yard that is not the boss (YT-74's rule: nothing in the
        /// swarm may out-size Max), and his gadget is held below his own head — the two together are
        /// what keep the character reading as a kid holding a thing rather than a thing holding a kid.
        /// </summary>
        [Test]
        public void TheGadgetIsHeldBelowHisHead()
        {
            Assert.That(MaxRig.BarrelHeight(1f), Is.LessThan(EnemyArchetype.PlayerHeight * 0.75f),
                "The gadget is presented up around Max's face. He is holding it, not looking down it.");
        }
    }
}
