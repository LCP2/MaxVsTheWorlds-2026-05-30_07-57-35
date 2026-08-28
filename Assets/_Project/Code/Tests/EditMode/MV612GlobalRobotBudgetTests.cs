using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-612 — nothing capped the live robot count field-wide. <see cref="EnemySpawner"/> honoured a
    /// field-wide cap; <see cref="AreaSpawnQueue"/>'s ambient release (<c>TryExtractEligible</c>) only
    /// ever checked its OWN per-area <see cref="AreaSpawnQueue.MaxActive"/>, and
    /// <see cref="AreaSpawnQueue.TryTakeForGarrison(int, out EnemyKind)"/> checked nothing at all — so
    /// three concurrently-live areas (current, a previous area's overflow tail, MV-514's pre-placed
    /// next) could reach 3 x 18 = 54 concurrent robots, and once that population passed EnemySpawner's
    /// own 24-robot ceiling every shed stopped producing (Lee's separately-reported "sheds are not
    /// producing enemies").
    ///
    /// This introduces one authoritative field-wide budget
    /// (<see cref="RobotCompositionTuning.DefaultGlobalRobotBudget"/>, overridable via
    /// <see cref="DevTuning.GlobalRobotBudget"/>) that <see cref="AreaSpawnQueue"/>'s ambient release
    /// now also enforces, on top of the existing per-area <c>MaxActive</c>, while
    /// <c>TryTakeForGarrison</c> stays deliberately unblocked by it (a room's authored garrison must
    /// always be present the instant it's entered) but still counts toward it via the same
    /// <c>Activate</c> every release goes through.
    ///
    /// Everything here drives <see cref="AreaSpawnQueue"/> directly rather than
    /// <c>AreaAccumulationDirector</c> or <see cref="EnemySpawner"/>: it is the one plain-C#/pure class
    /// (no MonoBehaviour, no RobotEnemy/Unity-lifecycle dependency) both the ambient and every garrison
    /// path actually share, and CC_AUTONOMY.md forbids PlayMode tests outright.
    /// <see cref="RobotEnemy.ActiveCount"/> itself never moves in EditMode — its own doc comment and
    /// <c>AreaAccumulationDirector.RestoreArea</c>'s both note that <c>OnEnable</c> does not fire on a
    /// plain MonoBehaviour outside Play mode — so it cannot be what any EditMode test here asserts
    /// against. <see cref="EnemySpawner"/>'s own field-wide gate (<c>RobotEnemy.ActiveCount &lt;
    /// GlobalMaxLiveEnemies</c>) is therefore left exactly as it was — already correct, already
    /// covered by <c>EnemyPopulationPlayTests</c> — and Test 3 below instead proves, directly against
    /// <see cref="EnemySpawner.WantsToEmit"/>, that its ceiling is now the same shared,
    /// DevTuning-overridable number <see cref="AreaSpawnQueue.GlobalBudget"/> enforces too, with the
    /// field held empty (the one state EditMode can actually construct).
    /// </summary>
    public sealed class MV612GlobalRobotBudgetTests
    {
        [TearDown]
        public void TearDown() => DevTuning.Reset();

        // --- AC1: a realistic mixed sequence of area entries + garrison placements never exceeds budget ---

        [Test]
        public void AmbientAndGarrisonTogetherNeverExceedTheBudget_AcrossMultipleAreaEntries()
        {
            // Numbers are chosen so the combined system stays inside budget through area 1 and only
            // actually defers once area 2 pushes it over — a realistic mixed sequence, not the extreme
            // "garrison alone blows past budget" case, which is Test 4 below's own job.
            var queue = new AreaSpawnQueue(maxActive: 100, globalBudget: 10);

            queue.Fill(largeCount: 0, smallCount: 6, areaIndex: 1);
            Assert.IsTrue(queue.TryTakeForGarrison(1, out _));
            Assert.IsTrue(queue.TryTakeForGarrison(1, out _));
            while (queue.TryReleaseArea(1, out _))
                Assert.LessOrEqual(queue.ActiveCount, 10, "area 1's ambient release exceeded the field-wide budget");

            queue.Fill(largeCount: 0, smallCount: 6, areaIndex: 2);
            Assert.IsTrue(queue.TryTakeForGarrison(2, out _));
            Assert.IsTrue(queue.TryTakeForGarrison(2, out _));
            while (queue.TryReleaseArea(2, out _))
                Assert.LessOrEqual(queue.ActiveCount, 10, "area 2's ambient release exceeded the field-wide budget");

            Assert.AreEqual(10, queue.ActiveCount, "the sequence should have filled the budget exactly");
            Assert.AreEqual(2, queue.QueuedCount, "area 2's last two ambient robots must be deferred, not dropped");
        }

        // --- AC2: deferred ambient spawns are never lost ---------------------------------------------

        [Test]
        public void DeferredAmbientSpawnsAreNeverLost_TheyDrainOnceRoomFrees()
        {
            var queue = new AreaSpawnQueue(maxActive: 100, globalBudget: 6);
            queue.Fill(largeCount: 4, smallCount: 6, areaIndex: 1); // 10 queued total, over the budget of 6

            int totalReleased = 0;
            while (queue.TryRelease(out _)) totalReleased++;
            Assert.AreEqual(6, totalReleased, "release must stop exactly at the budget, deferring the rest");
            Assert.AreEqual(4, queue.QueuedCount, "the deferred 4 must still be queued, not dropped");

            while (queue.QueuedCount > 0)
            {
                queue.ReportDestroyed(1);
                Assert.IsTrue(queue.TryRelease(out _), "a deferred entry failed to release once room freed - it was lost");
                totalReleased++;
            }

            Assert.AreEqual(10, totalReleased, "the full authored total (10) must eventually reach the field, none dropped");
        }

        // --- AC3: EnemySpawner and the area queue share one authoritative budget ---------------------

        [Test]
        public void AShedStillProduces_WhenTheFieldIsBelowTheSharedBudget()
        {
            // Exercises EnemySpawner's REAL production gate (WantsToEmit), not just a constants
            // comparison: this is the regression guard for "sheds are not producing enemies" (Lee's
            // separate report), caused by the area director's population (once genuinely unbounded, up
            // to 54) permanently tripping EnemySpawner's own field-wide GlobalHasRoom check.
            // RobotEnemy.ActiveCount cannot be driven above 0 in EditMode (OnEnable never fires outside
            // Play mode — see this file's own header), so instead of trying to fill the field, this
            // proves the other half of the same inequality: with the field genuinely empty
            // (ActiveCount == 0, "below" any positive budget), WantsToEmit must NOT be blocked by the
            // shared global check — and IS blocked the moment the shared budget itself is dialled down
            // to 0, proving GlobalHasRoom really does read the live, shared DevTuning.GlobalRobotBudget
            // value (the SAME one AreaSpawnQueue.GlobalBudget reads) rather than an independent number.
            // The live PlayMode proof that a shed resumes once the AREA population specifically drops
            // below budget remains EnemyPopulationPlayTests' job, per CC_AUTONOMY.md's PlayMode ban.
            RobotEnemy.ResetRegistry();
            GameObject spawnerGo = null;
            try
            {
                spawnerGo = new GameObject("MV-612 test spawner");
                var spawner = spawnerGo.AddComponent<EnemySpawner>();

                FieldInfo timerField = typeof(EnemySpawner).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance);
                timerField.SetValue(spawner, 999f); // past CurrentInterval - isolates GlobalHasRoom as the only variable

                // A fresh factory's EffectiveMaxLiveEnemies ramps from startingRobots (authored 0,
                // YT-200: a run starts with no robots on the field) up as DifficultyDirector.Normalized
                // climbs - both 0 by default here, which would gate WantsToEmit on ITS OWN local cap
                // instead of the field-wide budget this test targets. Dial startingRobots up so the
                // local cap has headroom and GlobalHasRoom is the only variable left under test.
                DevTuning.StartingRobots = 4f;

                Assert.IsTrue(spawner.WantsToEmit,
                    "a shed with an empty field and its own cadence ready must want to emit - the field-wide budget must not be blocking it");

                DevTuning.GlobalRobotBudget = 0f;
                Assert.IsFalse(spawner.WantsToEmit,
                    "dialling the SHARED budget to 0 must block the shed too - proves GlobalHasRoom reads the live shared value, not an independent constant");
            }
            finally
            {
                if (spawnerGo != null) Object.DestroyImmediate(spawnerGo);
                RobotEnemy.ResetRegistry();
            }
        }

        // --- AC4: the budget holds across MULTIPLE simultaneously live areas --------------------------

        [Test]
        public void TheGlobalBudgetCatchesWhatThePerAreaCapAlonePermits_AcrossThreeSimultaneousAreas()
        {
            // A per-area cap of 18 alone lets 3 concurrently-live areas — current, a previous area's
            // still-draining overflow tail, and MV-514's pre-placed next — reach 3 x 18 = 54 concurrent
            // robots; nothing before MV-612 caught that (AreaSpawnQueueTests' own per-area-cap tests
            // only ever exercise ONE area at its cap at a time). One queue instance stands in for
            // AreaAccumulationDirector's single field-wide _queue, which really does hold all three
            // areas' accounting at once.
            var queue = new AreaSpawnQueue(maxActive: 18, globalBudget: 24);

            queue.Fill(largeCount: 0, smallCount: 18, areaIndex: 1); // previous area, still draining
            queue.Fill(largeCount: 0, smallCount: 18, areaIndex: 2); // current area
            queue.Fill(largeCount: 0, smallCount: 18, areaIndex: 3); // MV-514 pre-placed next

            int released = 0;
            while (queue.TryRelease(out _))
            {
                released++;
                Assert.LessOrEqual(queue.ActiveCount, 24,
                    "the field-wide budget must hold even though each area is individually still under its own 18-robot cap");
            }

            Assert.AreEqual(24, released, "release should stop exactly at the shared budget across all three areas combined");
            Assert.AreEqual(3 * 18 - 24, queue.QueuedCount, "the rest must stay queued, not dropped");
        }

        // --- AC5: the budget never shrinks an area's authored composition -----------------------------

        [Test]
        public void TheBudgetNeverShrinksAnAreasAuthoredComposition()
        {
            var queue = new AreaSpawnQueue(maxActive: 100, globalBudget: 3);
            var composition = new DifficultyEngine.Composition(
                rusher: 4, bruiser: 3, heavy: 1, brute: 1, gunner: 1, launcher: 1, blinker: 1);
            queue.FillExact(composition, areaIndex: 5);

            Assert.AreEqual(composition.TotalCount, queue.TotalRemaining,
                "the global budget must only ever defer release/placement timing, never drop part of an area's authored roster");
        }
    }
}
