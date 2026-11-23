using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// MV-1427: the City's squad/post rules. Pure goal-choosing helpers plus the squad queries over the
    /// live <see cref="RobotEnemy.Active"/> registry; <see cref="RobotEnemy"/> only calls these when its own
    /// <c>SquadOn</c> flag (the world's <c>squadTactics</c> dial) is set, so every other world is untouched.
    /// Pathfinding is unchanged: these rules only choose goals.
    /// </summary>
    public static class SquadTactics
    {
        public const float HoldRingMeters = 9f;           // "circle Max at 8-10 m"
        public const float HoldReleaseSeconds = 6f;
        public const float BodyguardEngageMeters = 10f;
        public const float BodyguardDisengageMeters = 12f;
        public const float BodyguardOffsetMeters = 2f;
        public const float PostRadiusMeters = 1.5f;
        public const float PostSettleMeters = 1.0f;
        public const float BolterBackOffMeters = 9f;
        public const float BlinkBehindMeters = 2.5f;

        public static bool IsChaser(EnemyKind k) =>
            k == EnemyKind.Rusher || k == EnemyKind.Bruiser || k == EnemyKind.Heavy || k == EnemyKind.Brute
            || k == EnemyKind.Blinker || k == EnemyKind.Charger;

        public static bool IsRanged(EnemyKind k) =>
            k == EnemyKind.Bolter || k == EnemyKind.Gunner || k == EnemyKind.Launcher || k == EnemyKind.Turret
            || k == EnemyKind.ArcStriker || k == EnemyKind.Orbital;

        public static bool IsBodyguard(EnemyKind k) => k == EnemyKind.Bruiser || k == EnemyKind.Heavy;

        public static bool SameSquad(RobotEnemy a, RobotEnemy b) =>
            a != null && b != null && a.SquadOn && b.SquadOn && !string.IsNullOrEmpty(a.SquadId)
            && a.SquadId == b.SquadId && a.AreaIndex == b.AreaIndex;

        /// <summary>Wake together: every squad-mate of <paramref name="r"/> wakes in the same call.</summary>
        public static void WakeSquad(RobotEnemy r)
        {
            if (r == null || !r.SquadOn || string.IsNullOrEmpty(r.SquadId)) return;
            var all = RobotEnemy.Active;
            for (int i = 0; i < all.Count; i++)
            {
                RobotEnemy o = all[i];
                if (o != null && o != r && o.IsAlive && SameSquad(r, o)) o.Activate();
            }
        }

        public static bool HasLiveRangedMate(RobotEnemy r)
        {
            var all = RobotEnemy.Active;
            for (int i = 0; i < all.Count; i++)
            {
                RobotEnemy o = all[i];
                if (o != null && o != r && o.IsAlive && IsRanged(o.Kind) && SameSquad(r, o)) return true;
            }
            return false;
        }

        /// <summary>A ranged robot fired at Max: every chaser in its squad commits from this tick on.</summary>
        public static void NotifyFired(RobotEnemy shooter)
        {
            if (shooter == null || !shooter.SquadOn || !IsRanged(shooter.Kind)) return;
            var all = RobotEnemy.Active;
            for (int i = 0; i < all.Count; i++)
            {
                RobotEnemy o = all[i];
                if (o != null && SameSquad(shooter, o)) o.SquadVolleyFired = true;
            }
            shooter.SquadVolleyFired = true;
        }

        /// <summary>The 8-10 m ring goal a held chaser aims at: a point on a 9 m ring around Max, nudged
        /// round it so the robot circles instead of parking.</summary>
        public static Vector3 HoldGoal(Vector3 robotPos, Vector3 maxPos, int id)
        {
            Vector3 radial = robotPos - maxPos; radial.y = 0f;
            if (radial.sqrMagnitude < 0.01f) radial = Vector3.forward;
            radial.Normalize();
            Vector3 tangent = new Vector3(-radial.z, 0f, radial.x) * ((id & 1) == 0 ? 1f : -1f);
            Vector3 g = maxPos + (radial + tangent * 0.5f).normalized * HoldRingMeters;
            g.y = robotPos.y;
            return g;
        }

        /// <summary>The point <see cref="BodyguardOffsetMeters"/> from <paramref name="ranged"/> on the line toward Max.</summary>
        public static Vector3 BodyguardPoint(Vector3 rangedPos, Vector3 maxPos)
        {
            Vector3 d = maxPos - rangedPos; d.y = 0f;
            d = d.sqrMagnitude < 1e-4f ? Vector3.forward : d.normalized;
            Vector3 p = rangedPos + d * BodyguardOffsetMeters;
            p.y = rangedPos.y;
            return p;
        }

        /// <summary>The live ranged squad-mate nearest Max that a bodyguard should cover: any within
        /// <see cref="BodyguardEngageMeters"/> of Max, or — when <paramref name="current"/> is already being
        /// covered — it stays covered out to <see cref="BodyguardDisengageMeters"/>.</summary>
        public static RobotEnemy PickCharge(RobotEnemy guard, RobotEnemy current, Vector3 maxPos)
        {
            if (current != null && current.IsAlive && SameSquad(guard, current) && FlatDist(current.transform.position, maxPos) <= BodyguardDisengageMeters)
                return current;
            RobotEnemy best = null; float bestD = BodyguardEngageMeters;
            var all = RobotEnemy.Active;
            for (int i = 0; i < all.Count; i++)
            {
                RobotEnemy o = all[i];
                if (o == null || o == guard || !o.IsAlive || !IsRanged(o.Kind) || !SameSquad(guard, o)) continue;
                float d = FlatDist(o.transform.position, maxPos);
                if (d < bestD) { bestD = d; best = o; }
            }
            return best;
        }

        /// <summary>Where a Blinker lands: <see cref="BlinkBehindMeters"/> behind Max (opposite his facing),
        /// or the nearest clear spot round that circle if the ideal one is blocked.</summary>
        public static Vector3 BlinkBehindPoint(MapData map, Vector3 maxPos, Vector3 maxFacing, float clearRadius)
        {
            Vector3 back = -maxFacing; back.y = 0f;
            back = back.sqrMagnitude < 1e-4f ? Vector3.back : back.normalized;
            Vector3 ideal = maxPos + back * BlinkBehindMeters;
            if (map == null) return ideal;

            for (float ang = 0f; ang <= 90f; ang += 15f)
                for (int s = 0; s < 2; s++)
                {
                    if (ang == 0f && s == 1) continue;
                    Vector3 c = maxPos + Quaternion.AngleAxis(s == 0 ? ang : -ang, Vector3.up) * back * BlinkBehindMeters;
                    c.y = maxPos.y;
                    if (SkitterHop.IsClearLanding(map, maxPos, c, clearRadius)) return c;
                }
            return ideal;
        }

        private static float FlatDist(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
