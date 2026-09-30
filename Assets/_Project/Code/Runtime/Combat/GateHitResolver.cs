using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Combat
{
    /// <summary>
    /// Shared per-hit resolution for the primary weapons' hit tests (MV-1044). WaterBlaster.FireTick
    /// carried this inline since MV-302/MV-386; Undertow.FireLanceTick copied the plain
    /// transform-position/transform-sight-line test instead of this, so World 3 shipped unable to open
    /// its own gates. An <see cref="AreaGate"/> is wide and its own damageable transform (the leaf) is
    /// deliberately off the Cover layer (MV-386's <see cref="AreaGate.ThresholdObject"/> split): testing
    /// and sight-checking against the leaf misses both a shot that lands anywhere but dead-centre
    /// (MV-302) and every shot at all, since a Cover-masked sight-line to the leaf always lands on the
    /// threshold sitting across the same doorway first (MV-386). Every other <see cref="IDamageable"/>
    /// keeps testing its own transform position/sight target, exactly as before this ticket.
    /// </summary>
    internal static class GateHitResolver
    {
        /// <summary>How <paramref name="hitCollider"/> should be tested and sight-checked for
        /// <paramref name="d"/>: the point on the gate's own collider closest to the aim axis and its
        /// <see cref="AreaGate.ThresholdObject"/> for a gate, the collider's own transform for everything
        /// else.</summary>
        public static void Resolve(IDamageable d, Vector3 origin, Vector3 dir, Collider hitCollider,
            out Vector3 testPoint, out Transform sightTarget)
        {
            if (d is AreaGate gate)
            {
                testPoint = ContactPoint(origin, dir, hitCollider, hitCollider.transform.position);
                sightTarget = gate.ThresholdObject != null ? gate.ThresholdObject.transform : hitCollider.transform;
            }
            else
            {
                testPoint = hitCollider.transform.position;
                sightTarget = hitCollider.transform;
            }
        }

        /// <summary>The shared cone + line-of-sight + combat-level triple both
        /// <see cref="WaterBlaster.FireTick"/> and <see cref="Undertow.FireLanceTick"/> gate a hit on
        /// (MV-944's floor/deck split included) — a target passes only if all three agree.</summary>
        public static bool Passes(Vector3 origin, Vector3 dir, Vector3 testPoint, Transform sightTarget,
            float reach, float coneHalfAngleDeg)
        {
            return SprayHit.InCone(origin, dir, testPoint, reach, coneHalfAngleDeg)
                && LineOfSight.Clear(origin, testPoint, sightTarget)
                && CombatLevel.SameLevel(EnemyNavigation.Map, origin, testPoint);
        }

        /// <summary>Where the stream visually/physically lands on a body: the point on its collider
        /// closest to the stream's axis. Falls back to <paramref name="fallback"/> if the collider can't
        /// answer (non-convex mesh colliders reject ClosestPoint). Moved out of WaterBlaster unchanged
        /// (MV-1044) so Undertow can call the same helper instead of copying it.</summary>
        public static Vector3 ContactPoint(Vector3 origin, Vector3 dir, Collider col, Vector3 fallback)
        {
            if (col == null) return fallback;
            Vector3 onAxis = WaterVfxTuning.NearestPointOnRay(origin, dir, float.MaxValue, col.bounds.center);
            var mesh = col as MeshCollider;
            if (mesh != null && !mesh.convex) return col.ClosestPointOnBounds(onAxis);
            return col.ClosestPoint(onAxis);
        }
    }
}
