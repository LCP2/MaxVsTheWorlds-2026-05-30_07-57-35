using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// MV-998: no robot may come to rest inside a factory's exit zone — the doorway/arc a hutch emits
    /// through, or the ramp a Replicator's twins land on. A robot that parks right in that gap blocks
    /// every robot behind it, which reads as the factory having switched itself off.
    ///
    /// Pure maths, no scene references — same idiom as <see cref="FactoryMouth"/>, so the wedge/disc
    /// geometry is unit-testable on its own.
    /// </summary>
    public static class FactoryExitZone
    {
        /// <summary>How far past a hutch's own mouth arc still counts as "the exit zone" (the ticket's
        /// own number) — the doorway must stay clear this far out, not just exactly at its edge.</summary>
        public const float HutchMargin = 2.0f;

        /// <summary>Radius of a Replicator's own two exit discs — the out-ramp foot and the in-ramp
        /// mouth (the ticket's own number).</summary>
        public const float ReplicatorRadius = 3.0f;

        /// <summary>
        /// True when <paramref name="point"/> sits within <paramref name="margin"/> of ANY point a
        /// hutch could emit a robot along — every DoorPoint..ExitPoint segment across the mouth's fan,
        /// for every direction <paramref name="mouthDir"/> swung +/-<paramref name="halfAngleDeg"/>.
        /// That union is a wedge, radius 0..<paramref name="outerRadius"/>, centred on the factory.
        /// </summary>
        public static bool InsideHutchZone(Vector3 point, Vector3 factory, Vector3 mouthDir,
            float halfAngleDeg, float outerRadius, float margin = HutchMargin) =>
            DistanceToHutchWedge(point, factory, mouthDir, halfAngleDeg, outerRadius) <= margin;

        /// <summary>Flat distance from <paramref name="point"/> to the nearest point of that wedge.</summary>
        public static float DistanceToHutchWedge(Vector3 point, Vector3 factory, Vector3 mouthDir,
            float halfAngleDeg, float outerRadius)
        {
            Vector3 v = FlattenRaw(point - factory);
            float d = v.magnitude;
            if (d < 1e-5f) return 0f; // standing on the factory's own centre — inside by construction

            Vector3 dir = FlattenDir(mouthDir);
            if (dir == Vector3.zero) dir = Vector3.back;

            float angle = Vector3.SignedAngle(dir, v, Vector3.up);
            float clampedAngle = Mathf.Clamp(angle, -halfAngleDeg, halfAngleDeg);
            float clampedRadius = Mathf.Clamp(d, 0f, outerRadius);

            if (Mathf.Approximately(angle, clampedAngle))
                return Mathf.Abs(d - clampedRadius); // same ray as the wedge — purely radial distance

            Vector3 nearest = (Quaternion.AngleAxis(clampedAngle, Vector3.up) * dir) * clampedRadius;
            return Vector3.Distance(v, nearest);
        }

        /// <summary>True when <paramref name="point"/> sits within <paramref name="radius"/> of either
        /// of a Replicator's own two exit discs — the out-ramp foot twins land on, or the in-ramp mouth
        /// a lured robot is drawn into.</summary>
        public static bool InsideReplicatorZone(Vector3 point, Vector3 outRampFoot, Vector3 inRampMouth,
            float radius = ReplicatorRadius) =>
            FlatDistance(point, outRampFoot) <= radius || FlatDistance(point, inRampMouth) <= radius;

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static Vector3 FlattenRaw(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        private static Vector3 FlattenDir(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-6f ? Vector3.zero : v.normalized;
        }
    }

    /// <summary>
    /// MV-998: something a robot must never rest inside — a hutch's mouth (<see cref="EnemySpawner"/>)
    /// or a Replicator's ramp (<see cref="MaxWorlds.Factories.Replicator"/>). Both implement this and
    /// register/unregister themselves on OnEnable/OnDisable, so <see cref="RobotEnemy"/>'s rest gate and
    /// <see cref="DormantWakeScheduler"/>'s safety sweep can ask "would resting here be inside somebody's
    /// exit zone" without either side needing to know the other's concrete type.
    /// </summary>
    public interface IExitZoneSource
    {
        bool ExitZoneContains(Vector3 point);

        /// <summary>Where a robot resting at <paramref name="restingPoint"/> should walk to instead —
        /// clear of this source's own exit zone. <paramref name="slotIndex"/> spreads consecutive
        /// callers across lateral slots so they don't all converge on the same spot (see
        /// <see cref="FactoryMouth.MusterPoint"/>).</summary>
        Vector3 MusterPointFor(Vector3 restingPoint, int slotIndex);
    }

    /// <summary>
    /// MV-998: the field-wide registry of active exit-zone sources — same "OnEnable/OnDisable registry,
    /// no scene scan" idiom as <see cref="RobotEnemy.Active"/>. Empty on any level with no factory or
    /// Replicator built yet (or in a test that never creates one), so a query costs nothing until a
    /// source actually exists.
    /// </summary>
    public static class FactoryExitZones
    {
        private static readonly List<IExitZoneSource> _sources = new List<IExitZoneSource>(4);

        public static void Register(IExitZoneSource source)
        {
            if (!_sources.Contains(source)) _sources.Add(source);
        }

        public static void Unregister(IExitZoneSource source) => _sources.Remove(source);

        /// <summary>The first registered source that claims <paramref name="restingPoint"/> as inside
        /// its own exit zone, if any — false (the common, cheap case) when nothing does.</summary>
        public static bool TryMusterOut(Vector3 restingPoint, int slotIndex, out Vector3 musterPoint)
        {
            for (int i = 0; i < _sources.Count; i++)
            {
                IExitZoneSource source = _sources[i];
                if (source == null) continue;
                if (!source.ExitZoneContains(restingPoint)) continue;

                musterPoint = source.MusterPointFor(restingPoint, slotIndex);
                return true;
            }

            musterPoint = restingPoint;
            return false;
        }

        /// <summary>Test/level-reset hygiene — same idiom as <see cref="RobotEnemy.ResetRegistry"/>.</summary>
        public static void ResetForTests() => _sources.Clear();
    }
}
