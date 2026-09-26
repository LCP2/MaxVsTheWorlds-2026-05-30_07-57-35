using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-966 — World 2's ambient/garrison population kept ticking at full cost long after Max left the
    /// area: garrison placement bypasses the field-wide budget by design (pre-placed a whole area ahead,
    /// MV-514), and the old index-based <c>RobotEnemy.IsWellBehindPlayer</c> throttle (MV-870) assumed
    /// area numbers rise along the route — wrong for World 2's downward deck gantry (a15→a12→a11→a10),
    /// so those robots never even counted as "behind" and paid a full dormant tick every frame. Every
    /// robot also carried a stray <c>BoxCollider</c>/<c>CapsuleCollider</c>
    /// <c>GameObject.CreatePrimitive</c> auto-attaches and nothing removed (re-synced in PhysX every
    /// tick alongside the real <see cref="CharacterController"/>), and a Sludger's on-death split built
    /// four brand-new robots every time, never pooled, never destroyed.
    ///
    /// This one consolidated test (testing policy MV-465, Rule 1) drives the REAL, shipped World 2
    /// config through <see cref="AreaAccumulationDirector"/> exactly as a live run would, and asserts
    /// four RESOLVED values (Tier 2): (1) every resting robot whose area isn't reachable from Max's
    /// physical position is parked (<c>gameObject.activeInHierarchy == false</c>) — checked against an
    /// independently-recomputed reachable set built from the map's own public <c>AreLinked</c>/
    /// <c>ShareFootprint</c> primitives, not by calling the production reachability method itself; (2)
    /// moving Max's physical area into a16 unparks its pre-placed garrison the same call, with health
    /// and position unchanged; (3) no robot carries any <see cref="Collider"/> besides its own
    /// <see cref="CharacterController"/>; (4) a Sludger death with its split pool already warm creates no
    /// new <see cref="RobotEnemy"/> GameObjects.
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
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            Assert.GreaterOrEqual(cfg.dials.areaCount, 16, "setup failure: World 2 must author at least 16 areas for this scenario");

            var root = new GameObject("MV966 World2 Root").transform;
            GameObject directorGo = null;
            try
            {
                MapBuild built = MapRuntime.Build(map, root);

                directorGo = new GameObject("MV966 Area Director");
                var director = directorGo.AddComponent<AreaAccumulationDirector>();
                director.ConfigureWorld(cfg);
                director.Configure(map, built.Cover);   // seeds area 1, physicalArea starts at 1

                // Walk every gate up to a15 in order, exactly as a live run would — FillArea(15)'s own
                // trailing PlacePendingGarrison(16) pre-places a16's garrison along the way.
                for (int i = 2; i <= 15; i++) director.EnterArea(i);

                // Max's own PHYSICAL position now reaches a15's deck (EnterArea alone never moves it —
                // it only advances the population-facing, gate-ahead CurrentArea).
                director.SetCurrentArea(15);

                // ---- AC1, part 1: every resting robot outside reach is parked -----------------------
                HashSet<int> reachableFromA15 = IndependentlyComputedReachableAreas(map, 15);

                RobotEnemy[] allRobots = Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                Assert.IsNotEmpty(allRobots, "setup failure: World 2 must have placed robots by a15 for this test to mean anything");

                // Only a RESTING robot (Dormant, or Submerged for a Grate Lurker) is ever parked by
                // reach — an ordinary ambient robot spawns straight into Chase (awake, per Spawn's own
                // "BeginDormant only for the small concealed knot") and Change 1's own "awake robots
                // chasing Max are never parked" leaves it alone regardless of area. Only a garrison
                // seed (BeginDormant) or a concealed knot member is eligible here, and nothing in this
                // test ever wakes one (no Player/camera exists), so IsResting stays a stable read.
                int outOfReachChecked = 0;
                foreach (RobotEnemy r in allRobots)
                {
                    if (r.AreaIndex <= 0 || reachableFromA15.Contains(r.AreaIndex) || !r.IsResting) continue;
                    outOfReachChecked++;
                    Assert.IsFalse(r.gameObject.activeInHierarchy,
                        $"{r.name} sits in area{r.AreaIndex}, which is neither a15 nor gate-linked/footprint-sharing " +
                        "to it, but is still active");
                }
                Assert.Greater(outOfReachChecked, 0,
                    "setup failure: no out-of-reach resting robot existed to prove parking actually happened");

                // a16's own pre-placed garrison, specifically (the ticket's own named scenario).
                var a16Before = new List<RobotEnemy>();
                foreach (RobotEnemy r in allRobots) if (r.AreaIndex == 16) a16Before.Add(r);
                Assert.IsNotEmpty(a16Before, "setup failure: a16's garrison must have been pre-placed while filling a15");

                var a16Positions = new Vector3[a16Before.Count];
                var a16Healths = new float[a16Before.Count];
                for (int i = 0; i < a16Before.Count; i++)
                {
                    Assert.IsFalse(a16Before[i].gameObject.activeInHierarchy,
                        "a16's pre-placed garrison must start parked while Max only stands on a15");
                    a16Positions[i] = a16Before[i].transform.position;
                    a16Healths[i] = a16Before[i].HealthCurrent;
                }

                // ---- AC1, part 2: moving Max into a16 unparks it the same call, state unchanged -----
                director.SetCurrentArea(16);

                for (int i = 0; i < a16Before.Count; i++)
                {
                    Assert.IsTrue(a16Before[i].gameObject.activeInHierarchy,
                        "a16's own robots must be active the instant Max's physical area becomes a16");
                    Assert.AreEqual(a16Positions[i], a16Before[i].transform.position,
                        "unparking must never move a robot");
                    Assert.AreEqual(a16Healths[i], a16Before[i].HealthCurrent, 0.0001f,
                        "unparking must never change a robot's health");
                }

                // ---- AC1, part 3: no robot carries a stray collider ----------------------------------
                foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    Collider[] colliders = r.GetComponents<Collider>();
                    Assert.AreEqual(1, colliders.Length,
                        $"{r.name} carries {colliders.Length} Colliders — expected exactly its own CharacterController");
                    Assert.IsInstanceOf<CharacterController>(colliders[0],
                        $"{r.name}'s one Collider must be its CharacterController, not a stray primitive collider");
                }

                // ---- AC1, part 4: a Sludger death with the pool warm creates no new GameObjects ------
                EnemyArchetype rusherArchetype = EnemyArchetype.For(EnemyKind.Rusher, cfg);
                const int sludgerSplitCount = 4; // RobotEnemy.SludgerSplitCount — this ticket's own "stays 4"

                // Create all four warm Rushers FIRST, then kill all four — killing one at a time would
                // let each Take() immediately pop the one just pushed back by the death before it,
                // warming the pool with one recycled instance instead of four distinct ones.
                var warm = new RobotEnemy[sludgerSplitCount];
                for (int i = 0; i < sludgerSplitCount; i++)
                {
                    warm[i] = director.TakeForSplit(EnemyKind.Rusher, rusherArchetype);
                    warm[i].transform.position = new Vector3(i, 0f, 0f);
                    warm[i].gameObject.SetActive(true);
                }
                for (int i = 0; i < sludgerSplitCount; i++)
                    warm[i].TakeDamage(new DamageInfo(999999f, warm[i].transform.position, Vector3.up, Team.Player));

                EnemyArchetype sludgerArchetype = EnemyArchetype.For(EnemyKind.Sludger, cfg);
                RobotEnemy sludger = director.TakeForSplit(EnemyKind.Sludger, sludgerArchetype);
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
                Object.DestroyImmediate(root.gameObject);
                if (directorGo != null) Object.DestroyImmediate(directorGo);
            }
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
