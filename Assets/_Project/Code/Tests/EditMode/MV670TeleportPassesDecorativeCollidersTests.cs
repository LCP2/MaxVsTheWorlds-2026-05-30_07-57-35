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
    /// MV-670, Lee: "Max is not arriving at the destination some of the time — it seems like he
    /// can't teleport past things like hedges and pots sometimes, and instead of ending up where he
    /// should, he goes in a straight line to that destination but appears to bang into the
    /// hedge/pot and not go further." A same-room blink ran through <see cref="CharacterController.Move"/>
    /// — a physics-swept move that stops dead at the first solid collider in its path, including
    /// hedges (MV-400) and pots (MV-613), both deliberately non-blocking dressing everywhere else
    /// but both still carrying real colliders. This supersedes MV-393's "same-room blinks keep
    /// collision-respecting movement" decision, per Lee's direct instruction — do not re-raise it.
    ///
    /// <see cref="LandsPastAHedgeOrPot_ButStillClampsAtAGenuineWall"/> exercises
    /// <see cref="PlayerAbilities.TryTeleport"/> directly — not <c>CanWarpAcrossAreas</c>, which
    /// <see cref="TeleportAreaWarpTests"/> already covers and this ticket leaves untouched — against
    /// a hedge/pot-shaped collider left OFF <see cref="CoverLayer"/> (MV-400/MV-613's exact
    /// convention) and, separately, against a wall-shaped collider left ON it (<c>MapRuntime</c>'s
    /// convention for every real solid: walls, gates, the Mower Hutch, non-hedge cover). Fails on
    /// the pre-fix base commit — the hedge/pot scenario's assertion that Max actually reaches the
    /// aimed target.
    /// </summary>
    public sealed class MV670TeleportPassesDecorativeCollidersTests
    {
        // Awake isn't reliably invoked for AddComponent outside Play mode (MV590BossWallSteeringTests,
        // MV548MobileShedTests) — drive it directly so PlayerAbilities' own _cc field actually exists
        // before TryTeleport is exercised.
        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        // EditMode tests share one physics scene for the whole cc-verify run with no per-test reset — a
        // distinctive, far-off origin sidesteps colliding with another fixture's leftover geometry (same
        // reasoning as MV590BossWallSteeringTests.RigOrigin).
        private static readonly Vector3 RigOrigin = new Vector3(48213f, 0f, -61177f);
        private static readonly Vector3 Target = RigOrigin + new Vector3(0f, 0f, 4f);

        private static (GameObject max, PlayerAbilities abilities) BuildMax()
        {
            WeaponSystemState.Reset();
            DevTuning.Reset();
            // MOVE (m_tp's category) starts locked after Reset() — only PRIMARY does not (RigState.Reset).
            // Unlock every category so Acquire(AbilityKind.Teleport) below succeeds, the same idiom this
            // project's other RIG-touching EditMode tests already use (e.g. MV523ForceFieldFreeActivationTests).
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
            DevTuning.TeleportBaseDistance = 4f;
            DevTuning.TeleportDistancePerLevel = 0f;

            var max = new GameObject("MV-670 Max", typeof(CharacterController), typeof(PlayerController));
            var cc = max.GetComponent<CharacterController>();
            cc.center = Vector3.up * 1f;
            cc.height = 2f;
            cc.radius = 0.4f;
            max.transform.position = RigOrigin;

            var abilities = max.GetComponent<PlayerAbilities>();
            if (abilities == null) abilities = max.AddComponent<PlayerAbilities>();
            InvokeAwake(abilities);

            WeaponSystemState.Acquire(AbilityKind.Teleport);
            return (max, abilities);
        }

        // Squarely between from (RigOrigin) and target (RigOrigin + (0,0,4)) — anything blocking must
        // be hit head-on.
        private static GameObject SpawnObstacle(bool onCoverLayer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = onCoverLayer ? "MV-670 Wall" : "MV-670 Hedge";
            go.transform.position = RigOrigin + new Vector3(0f, 1f, 2f);
            go.transform.localScale = new Vector3(4f, 2f, 0.5f);
            if (onCoverLayer) CoverLayer.Assign(go);
            // autoSyncTransforms is off project-wide (DynamicsManager.asset) — make the freshly
            // placed obstacle visible to the very first physics query against it.
            Physics.SyncTransforms();
            return go;
        }

        [Test]
        public void LandsPastAHedgeOrPot_ButStillClampsAtAGenuineWall()
        {
            (GameObject max, PlayerAbilities abilities) = BuildMax();
            GameObject hedge = SpawnObstacle(onCoverLayer: false);
            try
            {
                bool blinked = abilities.TryTeleport(Vector3.forward);

                Assert.That(blinked, Is.True, "precondition: an acquired, off-cooldown Teleport must fire");
                Assert.That(Vector3.Distance(max.transform.position, Target), Is.LessThan(0.5f),
                    "MV-670: a hedge/pot directly in the way must not stop Max short of the aimed destination");
            }
            finally
            {
                Object.DestroyImmediate(hedge);
                Object.DestroyImmediate(max);
                WeaponSystemState.Reset();
                DevTuning.Reset();
            }

            (max, abilities) = BuildMax();
            GameObject wall = SpawnObstacle(onCoverLayer: true);
            try
            {
                bool blinked = abilities.TryTeleport(Vector3.forward);

                Assert.That(blinked, Is.True, "precondition: an acquired, off-cooldown Teleport must fire");
                Assert.That(max.transform.position.z, Is.LessThan(RigOrigin.z + 1.5f),
                    "MV-670 AC2: a genuine wall/building collider must still stop a same-room blink short " +
                    "of it, not let Max pass through the way a hedge/pot now does");
            }
            finally
            {
                Object.DestroyImmediate(wall);
                Object.DestroyImmediate(max);
                WeaponSystemState.Reset();
                DevTuning.Reset();
            }
        }
    }
}
