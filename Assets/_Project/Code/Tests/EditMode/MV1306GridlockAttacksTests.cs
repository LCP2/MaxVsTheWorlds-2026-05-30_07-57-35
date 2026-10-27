using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1306 (one new test, per CC_AUTONOMY's testing policy): GRIDLOCK's three attacks (SQUAD/
    /// JUMP/STRIKE) and her escalation clock. AC1's own two sub-checks, both asserted here:
    ///
    ///   1. The STRIKE circles' arm delay must always be shorter than the circle's whole lifetime
    ///      (arm + stay) — a circle that arms for longer than it lives would never actually go live.
    ///   2. Simulating 30 s from waking, with Max static, she has fired at least 3 STRIKEs, 2 SQUADs
    ///      and 1 JUMP, and never holds more than 10 of her own robots alive at once.
    ///
    /// Built through the REAL production route (<see cref="MapRuntime.Build"/> off World 3's own
    /// "a15_boss", same boss MV1299GridlockBossTests already authors kind "gridlock" onto), then
    /// driven entirely through <see cref="GridlockBoss"/>'s own PUBLIC <c>Tick(dt)</c> and the public
    /// counters MV-1306 adds (<c>SquadFiredCount</c>/<c>JumpFiredCount</c>/<c>StrikeFiredCount</c>/
    /// <c>AliveRobotCount</c>) — no reflection into attack internals, only into <c>Awake</c> (MV-1013's
    /// own "AddComponent never fires Awake in this EditMode harness" note, same as every other boss
    /// fixture here).
    ///
    /// Fails on base commit 244a934 (MV-1299's own merge, the commit immediately before this ticket's
    /// pickup): <c>GridlockBoss</c> carries no SQUAD/JUMP/STRIKE/escalation behaviour at all, so
    /// <c>SquadFiredCount</c>/<c>JumpFiredCount</c>/<c>StrikeFiredCount</c>/<c>GridlockAttackClock</c>
    /// do not exist — a compile failure, the expected fail-first proof (same shape
    /// MV1299GridlockBossTests' own header documents for its own base commit).
    /// </summary>
    public sealed class MV1306GridlockAttacksTests
    {
        private static readonly MethodInfo AwakeMethod =
            typeof(GridlockBoss).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo AddsRootField =
            typeof(GridlockBoss).GetField("_addsRoot", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void InvokeAwake(object boss) => AwakeMethod.Invoke(boss, null);

        /// <summary>GridlockBoss's own add pool root is top-level and unit-scaled ON PURPOSE (same
        /// reasoning as BigBermudaBoss.AddsRoot's own doc comment) — never a descendant of this
        /// fixture's root GameObject, so it must be torn down separately or this test's own SQUAD
        /// adds outlive it for every later EditMode test in the same process to trip over (MV-1085's
        /// own fix comment documents exactly this trap).</summary>
        private static void DestroyAddsRoot(GridlockBoss boss)
        {
            if (boss == null) return;
            var addsRoot = (Transform)AddsRootField.GetValue(boss);
            if (addsRoot != null) Object.DestroyImmediate(addsRoot.gameObject);
        }

        private static WorldBoss FindBoss(WorldConfig cfg, string bossId)
        {
            foreach (WorldArea a in cfg.areas)
                foreach (WorldBoss b in a.Bosses())
                    if (b.id == bossId) return b;

            Assert.Fail($"boss '{bossId}' not found in config");
            return null;
        }

        private const float Dt = 1f / 60f;
        private const float SimulatedSeconds = 30f;

        [Test]
        public void GridlockAttacks_FireOnCadenceAndCapRobots_OverThirtySecondsFromWaking()
        {
            // --- AC1 part 1: the STRIKE circles' arm delay must always be shorter than their own
            // --- whole lifetime (arm + stay), or a circle would never actually go live. ---
            Assert.Less(GridlockTuning.StrikeArmDelay, GridlockTuning.StrikeArmDelay + GridlockTuning.StrikeStayDuration,
                "a STRIKE circle's arm delay must be shorter than its own lifetime (arm + stay)");

            GameObject root = null, playerGo = null;
            GridlockBoss boss = null;
            try
            {
                WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
                Assert.IsNotNull(cfg, $"'{WorldLibrary.World3}' failed to load — see the error log above.");

                WorldBoss a15Boss = FindBoss(cfg, "a15_boss");
                a15Boss.kind = "gridlock";

                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
                root = new GameObject("MV1306 Root");
                MapBuild built = MapRuntime.Build(map, root.transform);

                Assert.IsTrue(built.Actors.TryGetValue("a15_boss", out GameObject bossGo) && bossGo != null,
                    "'a15_boss' was not built once it authored kind 'gridlock'");

                boss = bossGo.GetComponent<GridlockBoss>();
                Assert.IsNotNull(boss, "authoring kind 'gridlock' must build a GridlockBoss");
                InvokeAwake(boss);

                // Wake her the same way MV1299GridlockBossTests does: Max 15 m away, inside the
                // shared 16 m wake radius with clear sight.
                playerGo = new GameObject("MV1306 Max") { tag = "Player" };
                playerGo.transform.position = bossGo.transform.position + new Vector3(-15f, 0f, 0f);

                BossCensus.Reset();
                // The first tick only lazily acquires _target (same TickDormant short-circuit every
                // other boss fixture here relies on); the wake check itself runs from the next tick
                // on, so give it a few before asserting (same idiom MV1299GridlockBossTests uses,
                // just bounded instead of a single fixed tick since this fixture never first ticks
                // her Dormant with Max already acquired at a safe distance).
                bool woke = false;
                for (int i = 0; i < 5 && !woke; i++)
                {
                    boss.Tick(Dt);
                    woke = boss.Engaged;
                }
                Assert.IsTrue(woke, "must wake once Max is within 16 m with clear sight");

                // --- AC1 part 2: 30 s from waking, with Max static (never moved again), she must ---
                // --- fire >= 3 STRIKE, >= 2 SQUAD, >= 1 JUMP, and never exceed 10 live robots. ---
                int maxAliveObserved = 0;
                for (float t = 0f; t < SimulatedSeconds; t += Dt)
                {
                    boss.Tick(Dt);
                    if (boss.AliveRobotCount > maxAliveObserved) maxAliveObserved = boss.AliveRobotCount;
                }

                Assert.GreaterOrEqual(boss.StrikeFiredCount, 3,
                    $"must fire at least 3 STRIKEs in {SimulatedSeconds}s from waking, fired {boss.StrikeFiredCount}");
                Assert.GreaterOrEqual(boss.SquadFiredCount, 2,
                    $"must fire at least 2 SQUADs in {SimulatedSeconds}s from waking, fired {boss.SquadFiredCount}");
                Assert.GreaterOrEqual(boss.JumpFiredCount, 1,
                    $"must fire at least 1 JUMP in {SimulatedSeconds}s from waking, fired {boss.JumpFiredCount}");
                Assert.LessOrEqual(maxAliveObserved, 10,
                    $"must never hold more than 10 of her own robots alive at once, observed {maxAliveObserved}");
            }
            finally
            {
                DestroyAddsRoot(boss);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (root != null) Object.DestroyImmediate(root);
                BossCensus.Reset();
            }
        }
    }
}
