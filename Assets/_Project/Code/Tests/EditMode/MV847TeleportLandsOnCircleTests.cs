using System.Reflection;
using NUnit.Framework;
using UnityEngine;

using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-847, Lee: "teleport should allow you to go to exactly where the circle is, but instead you
    /// move towards the circle but Max stops at objects in the path. This is a teleport — you
    /// dematerialise at one point and materialise at another, so it's irrelevant what's between the
    /// start and end point." Seen at teleport L2 in World 2, where the 12m blink crosses more
    /// Cover-layer geometry (crates, walls, the Replicator, the Mower Hutch — everything MV-670 left
    /// ON the layer) than the shorter L1 hop.
    ///
    /// <see cref="ACrateHalfwayAlongThePathDoesNotShortenTheBlink"/> exercises
    /// <see cref="PlayerAbilities.TryTeleport"/> at the L2 distance (12m) against a Cover-layer box
    /// centred 4m ahead — squarely in the path, nowhere near the destination — and asserts Max still
    /// lands exactly on the aimed circle. Fails on the pre-fix base commit (10fce02): the old path
    /// <c>CapsuleCast</c> in <c>ResolveSameRoomLanding</c> clamps Max short of the box instead.
    /// </summary>
    public sealed class MV847TeleportLandsOnCircleTests
    {
        // Awake isn't reliably invoked for AddComponent outside Play mode (MV590BossWallSteeringTests,
        // MV548MobileShedTests, MV670TeleportPassesDecorativeCollidersTests) — drive it directly so
        // PlayerAbilities' own _cc field actually exists before TryTeleport is exercised.
        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        // EditMode tests share one physics scene for the whole cc-verify run with no per-test reset —
        // a distinctive, far-off origin sidesteps colliding with another fixture's leftover geometry
        // (same reasoning as MV590BossWallSteeringTests.RigOrigin).
        private static readonly Vector3 RigOrigin = new Vector3(-71344f, 0f, 39802f);
        private static readonly Vector3 Target = RigOrigin + new Vector3(0f, 0f, 12f);

        [Test]
        public void ACrateHalfwayAlongThePathDoesNotShortenTheBlink()
        {
            WeaponSystemState.Reset();
            DevTuning.Reset();
            // MOVE (m_tp's category) starts locked after Reset() — only PRIMARY does not (RigState.Reset).
            // Unlock every category so Acquire(AbilityKind.Teleport) below succeeds, the same idiom
            // MV670TeleportPassesDecorativeCollidersTests already uses.
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
            // L2's 12m (8 + 4*(2-1)) reach, without needing to actually level the ability up — the
            // same base-distance-override idiom MV670TeleportPassesDecorativeCollidersTests uses.
            DevTuning.TeleportBaseDistance = 12f;
            DevTuning.TeleportDistancePerLevel = 0f;

            var max = new GameObject("MV-847 Max", typeof(CharacterController), typeof(PlayerController));
            var cc = max.GetComponent<CharacterController>();
            cc.center = Vector3.up * 1f;
            cc.height = 2f;
            cc.radius = 0.4f;
            max.transform.position = RigOrigin;

            var abilities = max.GetComponent<PlayerAbilities>();
            if (abilities == null) abilities = max.AddComponent<PlayerAbilities>();
            InvokeAwake(abilities);

            WeaponSystemState.Acquire(AbilityKind.Teleport);

            // Squarely in the path (4m of a 12m blink), nowhere near the 12m destination.
            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "MV-847 Crate";
            crate.transform.position = RigOrigin + new Vector3(0f, 0.5f, 4f);
            crate.transform.localScale = Vector3.one;
            CoverLayer.Assign(crate);
            // autoSyncTransforms is off project-wide (DynamicsManager.asset) — make the freshly
            // placed crate visible to the very first physics query against it.
            Physics.SyncTransforms();

            try
            {
                bool blinked = abilities.TryTeleport(Vector3.forward);

                Assert.That(blinked, Is.True, "precondition: an acquired, off-cooldown Teleport must fire");
                Assert.That(Vector3.Distance(max.transform.position, Target), Is.LessThan(0.05f),
                    "MV-847: a crate merely in the path — not at the destination — must not shorten the " +
                    "blink; a teleport dematerialises/materialises, so it's irrelevant what's between " +
                    "start and end");
            }
            finally
            {
                Object.DestroyImmediate(crate);
                Object.DestroyImmediate(max);
                WeaponSystemState.Reset();
                DevTuning.Reset();
            }
        }
    }
}
