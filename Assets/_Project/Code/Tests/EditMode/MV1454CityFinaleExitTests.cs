using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Intro;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1454 (the one new test, Rule 1): a DEV start / RESUME into the City builds the world through the
    /// real <c>BackyardPath.Awake</c> (the AfterSceneLoad installers having run at "boot", before the City
    /// scene existed), GRIDLOCK dies through her real damage path, and the finale must still pay out: a
    /// reachable Weapon Core, taking it opens the exit at once, the doorway is walkable and walking through
    /// it starts the City-to-Reef transition. Run twice: fresh, and after a checkpoint taken in a30 before
    /// she dies. Tier 2 only: resolved pickups, gate state, physics clearance, sequence phase.
    /// Guards MV-1454.
    /// </summary>
    public sealed class MV1454CityFinaleExitTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1454-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = WorldCatalog.IndexOf(WorldIds.City) });
            Reset();
        }

        [TearDown]
        public void TearDown()
        {
            Clean();
            Reset();
            SaveSystem.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private static void Reset()
        {
            DevTuning.Reset();
            EnemyNavigation.Reset();
            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            Time.timeScale = 1f;
        }

        private static void Clean()
        {
            foreach (var t in new[] { typeof(BackyardPath), typeof(BossVictoryPayoff), typeof(WorldFinaleGate),
                                      typeof(WorldJoinSequence), typeof(PickupDirector), typeof(ResultScreen), typeof(RunTracker) })
                foreach (var o in Object.FindObjectsByType(t, FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (o != null) Object.DestroyImmediate(((Component)o).gameObject);
            foreach (var p in GameObject.FindGameObjectsWithTag("Player")) Object.DestroyImmediate(p);
            foreach (var g in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(g.gameObject);
        }

        private static object Call(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .Invoke(target, args);

        /// <summary>The AfterSceneLoad installers, run the way a boot would: before any City scene exists,
        /// so with no World 1 / World 2 boss anywhere they find nothing to install for.</summary>
        private static void RunBootInstallers()
        {
            foreach (var t in new[] { typeof(BossVictoryPayoff), typeof(WorldFinaleGate) })
                t.GetMethod("Install", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
        }

        private static void Subscribe(Component c)
        {
            // AddComponent outside Play mode does not reliably fire OnEnable -- re-subscribe cleanly.
            Call(c, "OnDisable");
            Call(c, "OnEnable");
        }

        private static Vector3 WithY(Vector3 v, float y) => new Vector3(v.x, y, v.z);

        [Test]
        public void CityFinale_GridlockDeathDropsReachableCore_OpensExit_AndCrossingStartsReefTransition()
        {
            RunBootInstallers();
            RunFinale(resumeFromCheckpoint: false);
            Clean();
            Reset();
            RunBootInstallers();
            RunFinale(resumeFromCheckpoint: true);
        }

        private void RunFinale(bool resumeFromCheckpoint)
        {
            string label = resumeFromCheckpoint ? "after a checkpoint resume" : "fresh DEV start";
            int cityIndex = WorldCatalog.IndexOf(WorldIds.City);

            var player = new GameObject("MV1454 Max") { tag = "Player" };
            player.AddComponent<CharacterController>();
            player.AddComponent<PlayerController>();

            var pathGo = new GameObject("MV1454 BackyardPath");
            var path = pathGo.AddComponent<BackyardPath>();
            Call(path, "Awake");
            Assert.IsNotNull(path.Map, $"[{label}] the City must build through BackyardPath.Awake");
            Assert.AreEqual(cityIndex, path.ResolvedWorldIndex);
            Physics.SyncTransforms();
            Call(path.ExitGate, "Awake"); // AddComponent never fires Awake in this EditMode harness

            WorldConfig cfg = path.Cfg;
            int finalArea = cfg.dials.areaCount;
            Assert.AreEqual(30, finalArea);

            var payoff = Object.FindFirstObjectByType<BossVictoryPayoff>();
            var gate = Object.FindFirstObjectByType<WorldFinaleGate>();
            Assert.IsNotNull(payoff, $"[{label}] BossVictoryPayoff must exist in a City whose a30 authors a boss");
            Assert.IsNotNull(gate, $"[{label}] WorldFinaleGate must exist in the City");
            Call(gate, "Awake"); // AddComponent does not reliably fire Awake/OnEnable outside Play mode
            Subscribe(payoff);
            Subscribe(gate);
            var tracker = new GameObject("MV1454 RunTracker").AddComponent<RunTracker>();
            Subscribe(tracker);
            var pickups = PickupDirector.EnsureInstalled();

            var boss = pathGo.GetComponentsInChildren<GridlockBoss>(true).First();
            Transform bossT = boss.transform;
            Call(boss, "Awake");
            WorldArea a30 = cfg.AreaByIndex(finalArea);

            // Max in a30, west side.
            Vector3 maxStart = new Vector3(a30.XMin + 4f, 0.1f, a30.CenterXz.y);
            player.transform.position = maxStart;
            Physics.SyncTransforms();

            if (resumeFromCheckpoint)
            {
                SaveSystem.CaptureCheckpoint(0, areaIndex: finalArea, worldIndex: cityIndex);
                Assert.IsTrue(SaveSystem.RestoreCheckpoint(0), "the a30 checkpoint must round-trip");
                SaveSlotData saved = SaveSystem.Load(0);
                gate.ResumeFromCheckpoint(saved.CheckpointFinaleWeaponGranted, saved.CheckpointFinaleExitOpen);
                Assert.IsFalse(gate.IsOpen, "resuming before GRIDLOCK died must not open the exit");
            }

            // Real wake, real pylons, real damage.
            Call(boss, "Wake");
            foreach (Pylon p in Object.FindObjectsByType<Pylon>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(p => p.Feeds == "gridlock"))
            {
                p.Build();
                p.TakeDamage(new DamageInfo(100000f, p.transform.position, Vector3.down, Team.Player));
            }
            Vector3 fell = bossT.position;
            for (int i = 0; i < 400 && BossCensus.DeadCountInArea(finalArea) == 0; i++)
                boss.TakeDamage(new DamageInfo(100000f, bossT.position, Vector3.down, Team.Player));
            Assert.AreEqual(1, BossCensus.DeadCountInArea(finalArea), $"[{label}] GRIDLOCK must die through TakeDamage");

            // Flush the deferred orb spawn.
            Call(payoff, "TickPendingWeaponCore", BossDeathSequence.FinaleTime + 0.01f);

            Pickup core = Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None).SingleOrDefault(p => p.Kind == PickupKind.WeaponCore);
            Assert.IsNotNull(core, $"[{label}] GRIDLOCK's death must drop a Weapon Core");
            Vector3 corePos = core.transform.position;
            Assert.LessOrEqual(Vector2.Distance(new Vector2(corePos.x, corePos.z), new Vector2(fell.x, fell.z)), 1.5f,
                $"[{label}] the orb must land where she fell");

            // Walkable route: step Max along the nav waypoints until in collect range.
            Vector3 pos = maxStart;
            int steps = 0;
            while (Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(corePos.x, corePos.z)) > PickupDirector.CollectRadius * 0.8f && steps < 600)
            {
                Vector3 wp = EnemyNavigation.Waypoint(pos, corePos);
                Vector3 dir = new Vector3(wp.x - pos.x, 0f, wp.z - pos.z);
                if (dir.sqrMagnitude < 1e-6f) break;
                pos += dir.normalized * Mathf.Min(0.4f, dir.magnitude);
                steps++;
            }
            Assert.LessOrEqual(Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(corePos.x, corePos.z)), PickupDirector.CollectRadius,
                $"[{label}] no walkable nav path from Max to the orb (stopped at {pos} after {steps} steps)");
            Assert.IsFalse(Physics.CheckCapsule(WithY(corePos, 0.65f), WithY(corePos, 1.45f), 0.55f, ~0, QueryTriggerInteraction.Ignore),
                $"[{label}] the orb sits inside a collider Max cannot enter");

            // Collect the real way: Max over it, director tick.
            player.transform.position = new Vector3(corePos.x, 0.1f, corePos.z);
            Physics.SyncTransforms();
            pickups.Tick(0.016f);
            Assert.IsTrue(gate.IsOpen, $"[{label}] taking the Core must open the exit at once");

            // The doorway's blocking collider is gone: capsule sweep straight through the exit.
            AreaGate exit = path.ExitGate;
            Assert.IsNotNull(exit);
            exit.Tick(10f);
            Physics.SyncTransforms();
            ExitDoorway d = path.Map.exitDoorway.Value;
            float mid = d.Hole.Mid;
            int blocked = 0;
            string what = "";
            for (float t = -6f; t <= 6f; t += 0.1f)
            {
                Vector3 c = d.AlongX ? new Vector3(mid, 0.65f, d.Coord + t) : new Vector3(d.Coord + t, 0.65f, mid);
                if (Physics.CheckCapsule(c, c + Vector3.up * 0.8f, 0.55f, ~0, QueryTriggerInteraction.Ignore))
                {
                    blocked++;
                    if (what.Length < 400)
                        what += $" t={t:F1}:" + string.Join(",", Physics.OverlapCapsule(c, c + Vector3.up * 0.8f, 0.55f, ~0, QueryTriggerInteraction.Ignore)
                            .Select(o => o.name + "@" + o.transform.position.ToString("F1") + "/" + o.bounds.size.ToString("F1")));
                }
            }
            Assert.AreEqual(0, blocked, $"[{label}] the opened City exit doorway is still blocked at {blocked} sweep steps (gate.IsOpen={exit.IsOpen}, alive={exit.IsAlive}, seq={Object.FindFirstObjectByType<WorldJoinSequence>() != null}, same={ReferenceEquals(exit, path.ExitGate)}):{what}");

            // Walk through: the City-to-Reef transition begins.
            var seq = Object.FindFirstObjectByType<WorldJoinSequence>();
            Assert.IsNotNull(seq, $"[{label}] opening the City exit must arm the City-to-Reef WorldJoinSequence");
            Vector3 beyond = d.AlongX ? new Vector3(mid, 0.1f, d.Coord + (d.Wall == Wall.N ? 3f : -3f))
                                      : new Vector3(d.Coord + (d.Wall == Wall.E ? 3f : -3f), 0.1f, mid);
            player.transform.position = beyond;
            Physics.SyncTransforms();
            seq.Tick(0.016f);
            Assert.IsTrue(seq.IsInCorridorWalk, $"[{label}] walking through the City exit must start the transition to the Reef");
        }
    }
}
