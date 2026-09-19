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
    /// convention), midway along the path, and separately against a wall-shaped collider left ON it
    /// (<c>MapRuntime</c>'s convention for every real solid: walls, gates, the Mower Hutch, non-hedge
    /// cover). Fails on the pre-fix base commit — the hedge/pot scenario's assertion that Max
    /// actually reaches the aimed target.
    ///
    /// MV-847 (Lee): "a teleport is a dematerialise/materialise — it's irrelevant what's between the
    /// start and end point", so a genuine wall sitting only MIDWAY along the path (as this test's wall
    /// scenario originally placed it) no longer clamps Max short of it either — same as the hedge/pot.
    /// The wall obstacle now sits directly ON the aimed destination instead, so the scenario still
    /// exercises a real, still-true invariant: solid geometry that the circle itself lands on redirects
    /// Max to the nearest point that fits, searched back toward him. Do not re-raise "a wall midway in
    /// the path should still stop the blink" — MV-847 explicitly overturned it.
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

        // Positioned along Z between from (RigOrigin) and target (RigOrigin + (0,0,4)).
        private static GameObject SpawnObstacle(bool onCoverLayer, float z)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = onCoverLayer ? "MV-670 Wall" : "MV-670 Hedge";
            go.transform.position = RigOrigin + new Vector3(0f, 1f, z);
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
            GameObject hedge = SpawnObstacle(onCoverLayer: false, z: 2f);
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
            // MV-847: the wall now sits ON the aimed destination (z=4), not midway (z=2) — a wall
            // merely in the path no longer matters (see the hedge scenario above, which already
            // proves that); only a wall the circle itself lands on should redirect Max.
            GameObject wall = SpawnObstacle(onCoverLayer: true, z: 4f);
            try
            {
                bool blinked = abilities.TryTeleport(Vector3.forward);

                // MV-847: nearest-fit search steps back from the destination (z=4) in 0.25m increments
                // against a wall spanning z=3.75..4.25 (radius 0.4 capsule clears once its centre drops
                // below z=3.35) — the third probe, z=3.25, is the first that fits.
                Assert.That(blinked, Is.True, "precondition: an acquired, off-cooldown Teleport must fire");
                Assert.That(Vector3.Distance(max.transform.position, RigOrigin + new Vector3(0f, 0f, 3.25f)),
                    Is.LessThan(0.05f),
                    "MV-847: a genuine wall/building collider the circle lands ON must redirect Max to " +
                    "the nearest point that actually fits, not let him clip through it");
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
