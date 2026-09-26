using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-966 — World-wide (originally scoped to World 2, widened by the ticket's own 2026-09-26 update
    /// once Lee reported the same overheating on World 1) ambient/garrison population kept ticking at
    /// full cost long after Max left the area: garrison placement bypasses the field-wide budget by
    /// design (pre-placed a whole area ahead, MV-514), and the old index-based
    /// <c>RobotEnemy.IsWellBehindPlayer</c> throttle (MV-870) assumed area numbers rise along the route
    /// — wrong for World 2's downward deck gantry (a15→a12→a11→a10), so those robots never even counted
    /// as "behind" and paid a full dormant tick every frame. Every robot also carried a stray
    /// <c>BoxCollider</c>/<c>CapsuleCollider</c> <c>GameObject.CreatePrimitive</c> auto-attaches and
    /// nothing removed (re-synced in PhysX every tick alongside the real
    /// <see cref="CharacterController"/>), and a Sludger's on-death split built four brand-new robots
    /// every time, never pooled, never destroyed.
    ///
    /// Two consolidated tests (testing policy MV-465, Rule 1 — one NEW test; the ticket's own "repeat
    /// the parking assertion on the real World 1 config" is the same assertion re-run against a second
    /// world's real data, not a second independent regression) drive the REAL, shipped World 2 and
    /// World 1 configs through <see cref="AreaAccumulationDirector"/> exactly as a live run would, and
    /// assert RESOLVED values (Tier 2): every resting robot whose area isn't reachable from Max's
    /// physical position is parked (<c>gameObject.activeInHierarchy == false</c>) — checked against an
    /// independently-recomputed reachable set built from the map's own public <c>AreLinked</c>/
    /// <c>ShareFootprint</c> primitives, not by calling the production reachability method itself — and
    /// moving Max's physical area into the next one unparks its pre-placed garrison the same call, with
    /// health and position unchanged. The World 2 test additionally asserts (3) no robot carries any
    /// <see cref="Collider"/> besides its own <see cref="CharacterController"/>, and (4) a Sludger death
    /// with its split pool already warm creates no new <see cref="RobotEnemy"/> GameObjects — mechanisms
    /// that are world-independent, so proven once rather than duplicated per world.
    ///
    /// Must fail to COMPILE on the pre-fix commit: <c>RobotEnemy.SetParked</c>/<c>IsResting</c>,
    /// <c>AreaAccumulationDirector.TakeForSplit</c> and the stray-collider strip did not exist before
    /// this ticket — the same "fails on the base commit" the project's testing policy accepts, per
    /// <c>MV611DormantAreaGateTests</c>' own precedent (culled by this same ticket, MV-870's throttle it
    /// guarded having been replaced outright by parking).
    /// </summary>
    public sealed class MV966ParkByReachTests
    {
        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
        }

        [Test]
        public void RealWorld2_ParksOutOfReachRobots_UnparksOnCrossing_NoStrayColliders_AndPoolsSludgerSplits()
        {
            ParkingScenario s = BuildWorldAndAssertParkByReach(WorldLibrary.World2, minAreaCount: 16,
                enterUpTo: 15, currentArea: 15, nextArea: 16);
            try
            {
                WorldConfig cfg = s.Cfg;

                // ---- part 3: no robot carries a stray collider --------------------------------------
                foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    Collider[] colliders = r.GetComponents<Collider>();
                    Assert.AreEqual(1, colliders.Length,
                        $"{r.name} carries {colliders.Length} Colliders — expected exactly its own CharacterController");
                    Assert.IsInstanceOf<CharacterController>(colliders[0],
                        $"{r.name}'s one Collider must be its CharacterController, not a stray primitive collider");
                }

                // ---- part 4: a Sludger death with the pool warm creates no new GameObjects ----------
                EnemyArchetype rusherArchetype = EnemyArchetype.For(EnemyKind.Rusher, cfg);
                const int sludgerSplitCount = 4; // RobotEnemy.SludgerSplitCount — this ticket's own "stays 4"

                // Create all four warm Rushers FIRST, then kill all four — killing one at a time would
                // let each Take() immediately pop the one just pushed back by the death before it,
                // warming the pool with one recycled instance instead of four distinct ones.
                var warm = new RobotEnemy[sludgerSplitCount];
                for (int i = 0; i < sludgerSplitCount; i++)
                {
                    warm[i] = s.Director.TakeForSplit(EnemyKind.Rusher, rusherArchetype);
                    warm[i].transform.position = new Vector3(i, 0f, 0f);
                    warm[i].gameObject.SetActive(true);
                }
                for (int i = 0; i < sludgerSplitCount; i++)
                    warm[i].TakeDamage(new DamageInfo(999999f, warm[i].transform.position, Vector3.up, Team.Player));

                EnemyArchetype sludgerArchetype = EnemyArchetype.For(EnemyKind.Sludger, cfg);
                RobotEnemy sludger = s.Director.TakeForSplit(EnemyKind.Sludger, sludgerArchetype);
                sludger.transform.position = new Vector3(0f, 0f, 5f);
                sludger.gameObject.SetActive(true);

                int beforeCount = Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

                sludger.TakeDamage(new DamageInfo(999999f, sludger.transform.position, Vector3.up, Team.Player));

                int afterCount = Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

                Assert.AreEqual(beforeCount, afterCount,
                    "a Sludger death with its split pool already warm must instantiate zero new RobotEnemy GameObjects");
            }
            finally
            {
                Object.DestroyImmediate(s.Root);
                Object.DestroyImmediate(s.DirectorGo);
            }
        }

        /// <summary>The ticket's own 2026-09-26 update: "the phone overheats in World 1 too... repeat
        /// the parking assertion on the real World 1 config with Max in a5 and robots alive in a1-a3
        /// and pre-placed in a6" — the same park/unpark-by-reach assertion
        /// <see cref="RealWorld2_ParksOutOfReachRobots_UnparksOnCrossing_NoStrayColliders_AndPoolsSludgerSplits"/>
        /// already proves, re-run against World 1's own real config/gate graph/garrison sizes (644
        /// authored garrison robots, per the ticket, more than World 2's 572) to prove <see cref="AreaAccumulationDirector.ParkByReach"/>
        /// is genuinely world-generic — it reads only the live <c>MapData</c>/physical-area state
        /// <see cref="AreaAccumulationDirector.Configure"/> was handed, never a World-2-specific
        /// assumption. Not a second independent regression (collider-stripping and Sludger-split pooling
        /// are mechanisms proven once, above, since neither varies per world), so this is the SAME AC1
        /// re-verified on a second world's data, not a second new test per MV-465 Rule 1.</summary>
        [Test]
        public void RealWorld1_ParksOutOfReachRobots_UnparksOnCrossing()
        {
            ParkingScenario s = BuildWorldAndAssertParkByReach(WorldLibrary.World1, minAreaCount: 6,
                enterUpTo: 5, currentArea: 5, nextArea: 6);
            Object.DestroyImmediate(s.Root);
            Object.DestroyImmediate(s.DirectorGo);
        }

        private readonly struct ParkingScenario
        {
            public readonly AreaAccumulationDirector Director;
            public readonly WorldConfig Cfg;
            public readonly GameObject Root;
            public readonly GameObject DirectorGo;

            public ParkingScenario(AreaAccumulationDirector director, WorldConfig cfg, GameObject root, GameObject directorGo)
            {
                Director = director;
                Cfg = cfg;
                Root = root;
                DirectorGo = directorGo;
            }
        }

        /// <summary>Builds <paramref name="worldKey"/>'s real, shipped config/map through
        /// <see cref="AreaAccumulationDirector"/> exactly as a live run would (walking every gate up to
        /// <paramref name="enterUpTo"/> in order — <c>FillArea</c>'s own trailing
        /// <c>PlacePendingGarrison(areaIndex + 1)</c> pre-places <paramref name="nextArea"/>'s garrison
        /// along the way), then asserts the two world-independent halves of Change 1: every out-of-reach
        /// RESTING robot is parked while Max's physical area is <paramref name="currentArea"/>, and
        /// moving it to <paramref name="nextArea"/> unparks that area's own pre-placed garrison the same
        /// call with health/position unchanged. Leaves the director/map/root alive and returns them so a
        /// caller can run further, mechanism-specific assertions before disposing.</summary>
        private static ParkingScenario BuildWorldAndAssertParkByReach(
            string worldKey, int minAreaCount, int enterUpTo, int currentArea, int nextArea)
        {
            WorldConfig cfg = WorldLibrary.Load(worldKey);
            Assert.IsNotNull(cfg, $"{worldKey}'s own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            Assert.GreaterOrEqual(cfg.dials.areaCount, minAreaCount,
                $"setup failure: {worldKey} must author at least {minAreaCount} areas for this scenario");

            var root = new GameObject($"MV966 {worldKey} Root");
            var directorGo = new GameObject($"MV966 {worldKey} Area Director");
            MapBuild built = MapRuntime.Build(map, root.transform);

            var director = directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, built.Cover);   // seeds area 1, physicalArea starts at 1

            for (int i = 2; i <= enterUpTo; i++) director.EnterArea(i);

            // Max's own PHYSICAL position now reaches currentArea (EnterArea alone never moves it — it
            // only advances the population-facing, gate-ahead CurrentArea).
            director.SetCurrentArea(currentArea);

            // ---- every resting robot outside reach is parked -------------------------------------
            HashSet<int> reachable = IndependentlyComputedReachableAreas(map, currentArea);

            RobotEnemy[] allRobots = Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.IsNotEmpty(allRobots,
                $"setup failure: {worldKey} must have placed robots by area{currentArea} for this test to mean anything");

            // Only a RESTING robot (Dormant, or Submerged for a Grate Lurker) is ever parked by reach —
            // an ordinary ambient robot spawns straight into Chase (awake, per Spawn's own "BeginDormant
            // only for the small concealed knot") and Change 1's own "awake robots chasing Max are never
            // parked" leaves it alone regardless of area. Only a garrison seed (BeginDormant) or a
            // concealed knot member is eligible here, and nothing in this test ever wakes one (no
            // Player/camera exists), so IsResting stays a stable read.
            int outOfReachChecked = 0;
            foreach (RobotEnemy r in allRobots)
            {
                if (r.AreaIndex <= 0 || reachable.Contains(r.AreaIndex) || !r.IsResting) continue;
                outOfReachChecked++;
                Assert.IsFalse(r.gameObject.activeInHierarchy,
                    $"{r.name} sits in area{r.AreaIndex}, which is neither area{currentArea} nor gate-linked/" +
                    $"footprint-sharing to it, but is still active");
            }
            Assert.Greater(outOfReachChecked, 0,
                "setup failure: no out-of-reach resting robot existed to prove parking actually happened");

            // nextArea's own pre-placed garrison, specifically (the ticket's own named scenario).
            var nextBefore = new List<RobotEnemy>();
            foreach (RobotEnemy r in allRobots) if (r.AreaIndex == nextArea) nextBefore.Add(r);
            Assert.IsNotEmpty(nextBefore,
                $"setup failure: area{nextArea}'s garrison must have been pre-placed while filling area{currentArea}");

            // World 2's a15/a16 are NOT gate-linked (Max walks a separate gantry to reach a16, per the
            // ticket's own observation) so a16 starts parked; World 1's a5/a6 turned out to be plain
            // adjacent rooms (gate-linked directly), so a6 is already reachable and active the moment
            // Max stands on a5. Assert "starts parked" only when the independently-computed reachable
            // set actually agrees nextArea is out of reach — the general out-of-reach loop above already
            // covers nextArea correctly either way; this only pins the ticket's own named before/after
            // snapshot without assuming a topology fact that isn't universal across worlds.
            bool nextAreaReachableAtStart = reachable.Contains(nextArea);

            var positions = new Vector3[nextBefore.Count];
            var healths = new float[nextBefore.Count];
            for (int i = 0; i < nextBefore.Count; i++)
            {
                if (!nextAreaReachableAtStart)
                {
                    Assert.IsFalse(nextBefore[i].gameObject.activeInHierarchy,
                        $"area{nextArea}'s pre-placed garrison must start parked while Max only stands on area{currentArea}");
                }
                positions[i] = nextBefore[i].transform.position;
                healths[i] = nextBefore[i].HealthCurrent;
            }

            // ---- moving Max into nextArea unparks it the same call, state unchanged -------------
            director.SetCurrentArea(nextArea);

            for (int i = 0; i < nextBefore.Count; i++)
            {
                Assert.IsTrue(nextBefore[i].gameObject.activeInHierarchy,
                    $"area{nextArea}'s own robots must be active the instant Max's physical area becomes area{nextArea}");
                Assert.AreEqual(positions[i], nextBefore[i].transform.position,
                    "unparking must never move a robot");
                Assert.AreEqual(healths[i], nextBefore[i].HealthCurrent, 0.0001f,
                    "unparking must never change a robot's health");
            }

            return new ParkingScenario(director, cfg, root, directorGo);
        }

        /// <summary>Independently recomputes MV-966's "current + gate-linked + footprint-sharing" active
        /// area set straight from <see cref="MapData"/>'s own public primitives
        /// (<see cref="MapData.AreLinked"/>, <see cref="MapZone.ShareFootprint"/>,
        /// <see cref="MapZone.AreaIndex"/>) — deliberately NOT a call into
        /// <c>AreaAccumulationDirector</c>'s own (private) reachability method, so this test is checking
        /// the production code's OUTPUT (which robots ended up parked) against a from-scratch reference
        /// built from the same raw map data, not merely restating the implementation.</summary>
        private static HashSet<int> IndependentlyComputedReachableAreas(MapData map, int physicalArea)
        {
            var reachable = new HashSet<int> { physicalArea };
            var activeZones = new List<MapZone>();
            MapZone current = map.Zone($"area{physicalArea}");
            if (current != null) activeZones.Add(current);

            foreach (MapZone z in map.zones)
            {
                if (z == null || z.AreaIndex <= 0 || z.AreaIndex == physicalArea) continue;
                if (!map.AreLinked($"area{physicalArea}", $"area{z.AreaIndex}")) continue;
                reachable.Add(z.AreaIndex);
                activeZones.Add(z);
            }

            foreach (MapZone z in map.zones)
            {
                if (z == null || z.AreaIndex <= 0 || reachable.Contains(z.AreaIndex)) continue;
                foreach (MapZone match in activeZones)
                {
                    if (!MapZone.ShareFootprint(z, match)) continue;
                    reachable.Add(z.AreaIndex);
                    break;
                }
            }

            return reachable;
        }
    }
}
