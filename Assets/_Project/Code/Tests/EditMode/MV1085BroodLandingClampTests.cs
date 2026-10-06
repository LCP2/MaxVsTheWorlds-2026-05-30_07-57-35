using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1085 (Lee, device, TestFlight v0.11.7, 2026-10-06): "Big Bermuda gets to full yellow bar and
    /// stops producing robots." His own on-device log repeated <c>[MV-955] FALLS: robot fell in unknown
    /// at (340.0, 0.8, 97.2)...</c> in World 1 area 30 — x 294..338, so x=340 is 2 m OUTSIDE its east
    /// wall. <see cref="BigBermudaBoss.LaunchVolley"/> used to land every add at a raw
    /// <see cref="BroodArc.Landing"/> point — a fixed distance off the boss's own flank, with no
    /// awareness of the arena at all — so a boss standing near a wall threw adds clean over it. Each one
    /// then sat outside the map forever (never dying, never reaching Max) while still counting against
    /// the old decrement-only <c>_liveAdds</c> field, so six of those jammed the volley shut for good.
    ///
    /// Fails on base commit d30d293 (confirmed via <c>git log --oneline d30d293..HEAD -- Assets/_Project/Code/Runtime/Bosses</c>:
    /// no commit between d30d293 and this ticket's pickup touched <c>BigBermudaBoss</c>/<c>BossTuning</c>/
    /// <c>BroodArc</c>, so pre-fix HEAD is behaviourally identical for this code path). Quoted failure
    /// output from a base-commit run (see this ticket's fix comment for the full log):
    ///   a landed add sits at x=341.00, outside area 30's own [294,338] bound by its own 0.40m radius
    ///   Expected: in range (294.39,337.61)
    ///   But was:  341.0f
    ///
    /// ONE new EditMode test (MV-465 Rule 1), carrying every assertion the ticket's own AC1 asks for as
    /// sub-checks: every landed add lands inside area 30's own bound (by its own real, built collider
    /// radius) and resolves to area 30 via <see cref="MapData.ZoneAt(float, float)"/>; killing every live
    /// add through the real <see cref="RobotEnemy.TakeDamage"/> path frees a full volley's worth of
    /// slots; and an add teleported outside the area (alive, never killed) holds no slot at all, so the
    /// boss still fires its next volley instead of jamming.
    ///
    /// Built through the REAL production route (<see cref="MapRuntime.Build"/> off world1_config's own
    /// "a30_boss1", World 1 area 30's real boss, the exact area Lee's device log named) — same build
    /// idiom <c>MV720BossContactDamageTests</c>/<c>MV1083BossClosesToContactTests</c> already use. Driven
    /// through <see cref="BigBermudaBoss"/>'s own private per-tick seams directly (reflection) —
    /// <c>TickVolley</c>/<c>AdvanceAdds</c> — "the real volley tick" the ticket's own AC1 asks for,
    /// rather than the whole <c>Update</c> loop (which would also drive <c>Approach</c>/<c>FaceTarget</c>,
    /// walking the boss away from the wall position this fixture deliberately places it against).
    ///
    /// The add pool is cleared via reflection right after each kill step, below. Not a production
    /// concern: <see cref="BigBermudaBoss.AdvanceAdds"/> revives a landed add by toggling its own
    /// <c>enabled</c> flag, which real Unity Play Mode dispatches <c>OnEnable</c> for exactly like any
    /// other activation — the one path this EditMode test CANNOT exercise is a pooled reuse's SECOND
    /// activation, because EditMode's batch run never pumps the engine's own enable/disable dispatch for
    /// a bare flag toggle the way a live Update loop does. Clearing the pool keeps every add this test
    /// drives on its FIRST activation (<c>CreateAdd</c>'s own <c>Awake</c>, which calls
    /// <c>ResetState</c> directly and needs no later toggle at all) — proving the boss's own counting
    /// and landing logic, the actual subject of this ticket, without tripping over an EditMode-only
    /// lifecycle gap in a system (pooled reactivation) this ticket does not touch.
    ///
    /// EditMode only, reflection-driven (repo convention — this worker never authors PlayMode tests).
    /// </summary>
    public sealed class MV1085BroodLandingClampTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly MethodInfo AwakeMethod =
            typeof(BigBermudaBoss).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo TickVolleyMethod =
            typeof(BigBermudaBoss).GetMethod("TickVolley", NonPublicInstance);
        private static readonly MethodInfo AdvanceAddsMethod =
            typeof(BigBermudaBoss).GetMethod("AdvanceAdds", NonPublicInstance);
        private static readonly MethodInfo AddsRootMethod =
            typeof(BigBermudaBoss).GetMethod("AddsRoot", NonPublicInstance);
        private static readonly FieldInfo BrainField =
            typeof(BigBermudaBoss).GetField("_brain", NonPublicInstance);
        private static readonly FieldInfo VolleyField =
            typeof(BigBermudaBoss).GetField("_volley", NonPublicInstance);
        private static readonly FieldInfo AddPoolsField =
            typeof(BigBermudaBoss).GetField("_addPools", NonPublicInstance);

        private const float Dt = 1f / 60f;

        // World 1 area 30 ("The Great Lawn"), world1_config.json: origin (294, 78), size 44x56 ->
        // x [294, 338], z [78, 134] -- exactly the bound Lee's own device log named.
        private const float AreaXMin = 294f, AreaXMax = 338f, AreaZMin = 78f, AreaZMax = 134f;

        // 3 m inside the east wall, clear of every authored a30_h* hedge (all sit at z >= 91) and of
        // both authored boss spots (a30_boss1 @ (316,127), a30_boss2 @ (320,86)).
        private static readonly Vector3 BossPosition = new Vector3(AreaXMax - 3f, 0f, 84f);

        private static void InvokeAwake(object boss) => AwakeMethod.Invoke(boss, null);
        private static void InvokeTickVolley(object boss, float dt) => TickVolleyMethod.Invoke(boss, new object[] { dt });
        private static void InvokeAdvanceAdds(object boss, float dt) => AdvanceAddsMethod.Invoke(boss, new object[] { dt });
        private static Transform InvokeAddsRoot(object boss) => (Transform)AddsRootMethod.Invoke(boss, null);

        private static void ClearAddPools(object boss) => ((IDictionary)AddPoolsField.GetValue(boss)).Clear();

        private static float WorldRadius(CharacterController cc, Transform t)
        {
            Vector3 scale = t.lossyScale;
            return cc.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        /// <summary>Every add under <paramref name="addsRoot"/> that currently holds a cap slot: active,
        /// enabled (landed, not mid-flight) and alive — the same shape
        /// <see cref="BigBermudaBoss.CountLandedAddsInArea"/> itself counts by, read independently here
        /// rather than trusting that method's own arithmetic.</summary>
        private static List<RobotEnemy> LandedLiveAdds(Transform addsRoot)
        {
            var result = new List<RobotEnemy>();
            for (int i = 0; i < addsRoot.childCount; i++)
            {
                Transform child = addsRoot.GetChild(i);
                if (!child.gameObject.activeInHierarchy) continue;
                if (!child.TryGetComponent(out RobotEnemy robot) || !robot.enabled || !robot.IsAlive) continue;
                result.Add(robot);
            }
            return result;
        }

        [SetUp]
        public void SetUp() => DevTuning.Reset();

        [TearDown]
        public void TearDown() => DevTuning.Reset();

        private static void RunTicks(object boss, float seconds)
        {
            for (float t = 0f; t < seconds; t += Dt)
            {
                InvokeTickVolley(boss, Dt);
                InvokeAdvanceAdds(boss, Dt);
            }
        }

        /// <summary>Ticks <paramref name="boss"/> until its own <see cref="BroodVolley.JustFired"/> edge
        /// fires exactly once (bounded by <paramref name="maxWaitSeconds"/>, generous over a fresh
        /// <see cref="BossTuning.VolleyInterval"/>+<see cref="BossTuning.VolleyWindup"/> wait), then runs
        /// <see cref="BossTuning.VolleyArcTime"/>'s worth more so that volley's own adds finish landing —
        /// and stops there, before a second volley could possibly fire (the shortest any next one could
        /// come is <see cref="BossTuning.VolleyOpenHold"/> + a fresh interval + windup later, far past the
        /// small arc-time buffer this waits). Returns how many MORE adds are landed-and-alive afterward
        /// than before.</summary>
        private static int CountNextVolleyLaunch(object boss, Transform addsRoot, float maxWaitSeconds)
        {
            var volley = (BroodVolley)VolleyField.GetValue(boss);
            int before = LandedLiveAdds(addsRoot).Count;

            bool fired = false;
            for (float waited = 0f; waited < maxWaitSeconds; waited += Dt)
            {
                InvokeTickVolley(boss, Dt);
                InvokeAdvanceAdds(boss, Dt);
                if (volley.JustFired) { fired = true; break; }
            }
            Assert.IsTrue(fired, $"no volley fired within {maxWaitSeconds:F1}s");

            for (float a = 0f; a < BossTuning.VolleyArcTime + 0.2f; a += Dt)
            {
                InvokeTickVolley(boss, Dt);
                InvokeAdvanceAdds(boss, Dt);
            }

            return LandedLiveAdds(addsRoot).Count - before;
        }

        [Test]
        public void BroodVolley_NeverLandsAnAddOutsideTheBossOwnArea_AndNeverJamsOnAStrandedAdd()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "world1_config.json failed to load — see the error log above");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            MapZone area30 = map.Zone("area30");
            Assert.IsNotNull(area30, "world1_config.json has no area30 zone");

            var root = new GameObject("MV1085 Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                Assert.IsTrue(built.Actors.TryGetValue("a30_boss1", out GameObject bossGo) && bossGo != null,
                    "world1_config.json's 'a30_boss1' was not built");
                var boss = bossGo.GetComponent<BigBermudaBoss>();
                Assert.IsNotNull(boss, "a30_boss1 did not build as a BigBermudaBoss");

                // Place the real boss 3 m inside area 30's own east wall, facing north (+Z, so its
                // right-flank hatch throws due EAST — straight at the wall) -- Lee's own defect.
                bossGo.transform.position = BossPosition;
                bossGo.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide

                InvokeAwake(boss); // re-run post-placement, same convention as MV720/MV1083's own fixtures

                // Spawn level 4 (Lee's own "full yellow bar") -- jump the brain's clock straight there
                // (BigBermudaBrain.Tick is public and pure) rather than ticking 90 simulated seconds one
                // frame at a time. hpNormalized=1 -> not enraged, so RobotsPerVolley (not the enraged
                // count) is what AC1's "assert the next volley launches 3 robots" means.
                var brain = (BigBermudaBrain)BrainField.GetValue(boss);
                brain.Tick(1000f, 1f);
                Assert.AreEqual(BossTuning.MaxSpawnLevel, brain.SpawnLevel,
                    "the brain never reached spawn level 4 (the full yellow bar)");

                // --- AC1 part 1: drive five volleys through the real volley tick. ---
                float fiveVolleysBudget = 5f * BossTuning.VolleyInterval + BossTuning.VolleyWindup
                    + BossTuning.VolleyArcTime + 1f;
                RunTicks(boss, fiveVolleysBudget);

                Transform addsRoot = InvokeAddsRoot(boss);
                List<RobotEnemy> firstWave = LandedLiveAdds(addsRoot);
                Assert.Greater(firstWave.Count, 0, "no add ever landed across 5 volleys — the fixture itself is broken");

                foreach (RobotEnemy robot in firstWave)
                {
                    var cc = robot.GetComponent<CharacterController>();
                    float worldRadius = WorldRadius(cc, robot.transform);
                    Vector3 p = robot.transform.position;

                    Assert.That(p.x, Is.InRange(AreaXMin + worldRadius - 0.01f, AreaXMax - worldRadius + 0.01f),
                        $"a landed add sits at x={p.x:F2}, outside area 30's own [{AreaXMin},{AreaXMax}] " +
                        $"bound by its own {worldRadius:F2}m radius");
                    Assert.That(p.z, Is.InRange(AreaZMin + worldRadius - 0.01f, AreaZMax - worldRadius + 0.01f),
                        $"a landed add sits at z={p.z:F2}, outside area 30's own [{AreaZMin},{AreaZMax}] " +
                        $"bound by its own {worldRadius:F2}m radius");

                    MapZone zoneAt = map.ZoneAt(p.x, p.z);
                    Assert.AreSame(area30, zoneAt,
                        $"a landed add at ({p.x:F2},{p.z:F2}) does not resolve to area 30 via MapZone.ZoneAt");
                }

                // --- AC1 part 2: kill every live add (landed OR still mid-arc) through the real ---
                // --- TakeDamage path; the next volley must launch a full wave again (no regression on ---
                // --- the ordinary death-frees-a-slot bookkeeping the old _liveAdds counter already got ---
                // --- right). The pool is cleared right after (test-only; see class doc) so the NEXT ---
                // --- volley's adds are fresh instances, each provably alive off its own first Awake. ---
                foreach (Transform child in addsRoot)
                {
                    if (!child.gameObject.activeInHierarchy) continue;
                    if (!child.TryGetComponent(out RobotEnemy robot) || !robot.IsAlive) continue;
                    robot.TakeDamage(new DamageInfo(1e6f, robot.transform.position, Vector3.forward, Team.Player));
                    Assert.IsFalse(robot.IsAlive, "a 1,000,000-damage hit must kill a brood add outright");
                }
                Assert.AreEqual(0, LandedLiveAdds(addsRoot).Count, "every add must read as dead immediately after TakeDamage");
                ClearAddPools(boss);

                float maxWait = BossTuning.VolleyInterval + BossTuning.VolleyWindup + 2f;
                int launchedAfterKill = CountNextVolleyLaunch(boss, addsRoot, maxWait);
                Assert.AreEqual(BossTuning.RobotsPerVolley, launchedAfterKill,
                    "killing every live add must free a full volley's worth of slots — the next volley " +
                    $"launched {launchedAfterKill}, not the full {BossTuning.RobotsPerVolley}");

                // --- AC1 part 3: teleport every live add outside the area rect (still alive, never ---
                // --- killed) and assert the boss still launches its NEXT volley — a stranded robot ---
                // --- (MV-955's own repeating FALLS log) must hold no slot at all. ---
                List<RobotEnemy> strandedButAlive = LandedLiveAdds(addsRoot);
                Assert.AreEqual(BossTuning.RobotsPerVolley, strandedButAlive.Count,
                    "the fixture itself is broken — the just-launched volley is no longer the only live wave");
                foreach (RobotEnemy robot in strandedButAlive)
                    robot.transform.position = new Vector3(AreaXMax + 500f, 0f, AreaZMax + 500f);
                Physics.SyncTransforms();

                int launchedAfterStranding = CountNextVolleyLaunch(boss, addsRoot, maxWait);
                Assert.Greater(launchedAfterStranding, 0,
                    "the boss never fired its next volley while every prior add sat alive outside its " +
                    "own area — a stranded (but alive) add is still holding a cap slot");
            }
            finally
            {
                Object.DestroyImmediate(root);
                BossCensus.Reset();
            }
        }
    }
}
