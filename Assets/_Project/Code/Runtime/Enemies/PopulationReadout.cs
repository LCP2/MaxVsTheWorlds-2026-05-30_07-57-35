using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.Player;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// MV-869: World 2 runs at 15-21 fps against a 60 target and nothing in the build says how many
    /// actors are alive — <see cref="RobotEnemy.ActiveCount"/> already exists and nothing surfaces it.
    /// This is the one readout line that answers both that question and the separate "why has a
    /// Replicator never fired" question, off the same single walk of <see cref="RobotEnemy.Active"/> —
    /// not two instruments.
    ///
    /// Wired into <see cref="Bootstrap.PopulationLineProvider"/> once, from the one assembly that can
    /// see the Enemies/Arena/Factories/Player types (this Core/Gameplay split is the same reasoning
    /// <see cref="BackyardPath.InstallWorldProbe"/> already documents for
    /// <see cref="Bootstrap.WorldProbeLineProvider"/>).
    /// </summary>
    public static class PopulationReadout
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallPopulationProbe() => Bootstrap.PopulationLineProvider = BuildLine;

        /// <summary>Resolves every live source (the field-wide robot/sentinel registries, every
        /// registered Replicator, and the live player position) and formats the line off them. Static
        /// and self-contained so an EditMode test can drive <see cref="BuildLine(System.Collections.Generic.IReadOnlyList{RobotEnemy}, System.Collections.Generic.IReadOnlyList{Sentinel}, System.Collections.Generic.IReadOnlyList{Replicator}, Vector3?)"/>
        /// directly with a synthetic population instead of a live scene.</summary>
        public static string BuildLine()
        {
            var player = Object.FindFirstObjectByType<PlayerController>();
            Vector3? playerPosition = player != null ? player.transform.position : (Vector3?)null;
            return BuildLine(RobotEnemy.Active, Sentinel.Active, FactoryCensus.RegisteredReplicators, playerPosition);
        }

        /// <summary>
        /// <c>robots &lt;total&gt; (awake &lt;a&gt; dorm &lt;d&gt; behind &lt;b&gt;)  sent &lt;s&gt;  repl &lt;r&gt;</c>,
        /// with a trailing <c>  rep a&lt;N&gt; sub&lt;0|1&gt; in&lt;0|1&gt; cap&lt;n&gt; q&lt;n&gt;/&lt;max&gt; elig&lt;n&gt;</c>
        /// appended only when a live Replicator sits in the player's own current area (nearest one if
        /// several) — omitted entirely when none does, the same as when there are no live Replicators at
        /// all. <paramref name="robots"/> is walked exactly ONCE: total/awake/dorm/behind and (when a
        /// target Replicator was found) elig all come out of that single pass, never a second walk.
        /// </summary>
        public static string BuildLine(
            System.Collections.Generic.IReadOnlyList<RobotEnemy> robots,
            System.Collections.Generic.IReadOnlyList<Sentinel> sentinels,
            System.Collections.Generic.IReadOnlyList<Replicator> replicators,
            Vector3? playerPosition)
        {
            int playerArea = ResolvePlayerArea(playerPosition);
            Replicator target = SelectTargetReplicator(replicators, playerArea, playerPosition);

            int total = robots?.Count ?? 0;
            int awake = 0, dorm = 0, behind = 0, elig = 0;

            if (robots != null)
            {
                for (int i = 0; i < robots.Count; i++)
                {
                    RobotEnemy r = robots[i];
                    if (r == null) continue;

                    if (r.Current == RobotEnemy.State.Dormant)
                    {
                        if (r.IsWellBehindPlayer) behind++;
                        else dorm++;
                    }
                    else if (r.IsAlive)
                    {
                        awake++;
                    }

                    if (target != null && Replicator.IsEligibleFor(r, target.AreaIndex)) elig++;
                }
            }

            int sent = sentinels?.Count ?? 0;
            int repl = 0;
            if (replicators != null)
            {
                for (int i = 0; i < replicators.Count; i++)
                    if (replicators[i] != null && replicators[i].IsAlive) repl++;
            }

            string line = $"robots {total} (awake {awake} dorm {dorm} behind {behind})  sent {sent}  repl {repl}";

            if (target != null)
            {
                line += $"  rep a{target.AreaIndex} sub{(target.IsSubscribedToAreaEvents ? 1 : 0)}" +
                        $" in{(target.PlayerInArea ? 1 : 0)} cap{target.Capacity}" +
                        $" q{target.QueueCount}/{Replicator.MaxQueueSlots} elig{elig}";
            }

            return line;
        }

        /// <summary>The player's own 1-based area right now, read from live position — same
        /// "ZoneAt then AreaIndexOf" idiom <see cref="RobotEnemy.IsWellBehindPlayer"/> and
        /// <see cref="AreaAccumulationDirector"/>'s own physical-area tracker both use, never
        /// <see cref="AreaAccumulationDirector.CurrentArea"/> (that tracker advances ahead of the player
        /// for population purposes). 0 (no area) whenever it can't be resolved — no map, no player, an
        /// unrecognised zone.</summary>
        private static int ResolvePlayerArea(Vector3? playerPosition)
        {
            if (playerPosition == null) return 0;

            MapData map = EnemyNavigation.Map;
            if (map == null) return 0;

            Vector3 pos = playerPosition.Value;
            MapZone zone = map.ZoneAt(pos.x, pos.y, pos.z);
            return zone == null ? 0 : AreaAccumulationDirector.AreaIndexOf(zone.id);
        }

        /// <summary>The live Replicator to report on: nearest-by-distance among the ones standing in
        /// <paramref name="playerArea"/>. Null when the player's area can't be resolved, or nothing
        /// live sits in it — the caller omits the "rep" suffix entirely in that case rather than
        /// reporting on a box the player isn't even near.</summary>
        private static Replicator SelectTargetReplicator(
            System.Collections.Generic.IReadOnlyList<Replicator> replicators, int playerArea, Vector3? playerPosition)
        {
            if (replicators == null || playerArea <= 0) return null;

            Replicator nearest = null;
            float nearestDist = float.MaxValue;
            Vector3 pos = playerPosition ?? Vector3.zero;

            for (int i = 0; i < replicators.Count; i++)
            {
                Replicator r = replicators[i];
                if (r == null || !r.IsAlive || r.AreaIndex != playerArea) continue;

                float dist = Vector3.Distance(r.transform.position, pos);
                if (dist < nearestDist) { nearestDist = dist; nearest = r; }
            }

            return nearest;
        }
    }
}
