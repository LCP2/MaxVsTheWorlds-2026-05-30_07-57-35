using System.Reflection;
using NUnit.Framework;
using UnityEngine;

using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-493: melee robots stalled and oscillated in an open gate because
    /// <see cref="ZoneRouteGrid.NextStep"/> clamped <see cref="MapRoutes"/>'s deliberately-through-the-
    /// wall waypoint back onto the doorway's own wall coordinate — precisely the aim point
    /// <see cref="MapRoutes.ThroughDoorway"/> exists to avoid. This is the single EditMode test the
    /// project's testing policy (MV-465 Rule 1) allows for this ticket: one fixture, folding the
    /// regression proof, the crossing simulation, the open-ground invariant and the <c>lastLeg</c>
    /// room-membership fix into one run, the same shape as <see cref="ZoneRouteGridTests"/>' own MV-476
    /// test.
    ///
    /// This test fails against the pre-fix (<c>0b30559</c>) behaviour: <c>NextStep</c> clamped its
    /// answer onto the shared wall coordinate, so the first assertion below (the waypoint must sit
    /// <see cref="MapRoutes.ThroughDoorway"/> metres past the wall's far face) reads a negative distance
    /// instead — the robot aimed AT the wall, not through it.
    /// </summary>
    public sealed class MV493DoorwayWaypointTests
    {
        private GameObject _pathGo;

        [SetUp]
        public void SetUp() => EnemyNavigation.Reset();

        [TearDown]
        public void TearDown()
        {
            EnemyNavigation.Reset();
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
        }

        /// <summary>Two 10x10 m rooms sharing a 4 m gated-free doorway at z=5, both empty of cover — the
        /// plain "open gate" the playtest report described.</summary>
        private static MapData TwoRoomsWithAnOpenDoorway(out MapZone a, out MapZone b)
        {
            a = new MapZone { id = "a", x = 0f, z = 0f, width = 10f, depth = 10f };
            b = new MapZone { id = "b", x = 0f, z = 10f, width = 10f, depth = 10f };

            return new MapData
            {
                zones = new[] { a, b },
                links = new[] { new MapLink { from = "a", to = "b", doorway = 4f } },
            };
        }

        /// <summary>Points <see cref="EnemyNavigation.Map"/> at <paramref name="map"/> without running
        /// <see cref="BackyardPath.Awake"/> (which loads a real world from Resources) — the same
        /// AddComponent-without-Awake seam <c>EnemyNavigationGateTests</c> relies on, one field set
        /// further in.</summary>
        private void InstallMap(MapData map)
        {
            _pathGo = new GameObject("MV493-test-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            FieldInfo mapField = typeof(BackyardPath).GetField("_map",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            mapField.SetValue(path, map);
        }

        [Test]
        public void MeleeRobotCrossingAnOpenGate_IsAimedThroughTheDoorway_NotAtTheWallLine()
        {
            MapData map = TwoRoomsWithAnOpenDoorway(out MapZone a, out MapZone b);
            InstallMap(map);

            var from = new Vector3(0f, 0f, -3f);
            var goal = new Vector3(0f, 0f, b.z);   // deep in zone b, well clear of the doorway

            // ---- AC1: the waypoint must be aimed THROUGH the doorway — ThroughDoorway metres past
            // the wall's far face — not clamped back onto the shared wall coordinate itself.
            Vector3 waypoint = EnemyNavigation.Waypoint(from, goal, useZoneRoute: true);
            float pastWallFace = waypoint.z - (a.ZMax + map.wallThickness * 0.5f);

            Assert.AreEqual(MapRoutes.ThroughDoorway, pastWallFace, 0.01f,
                "the waypoint should sit ThroughDoorway metres past the wall's far face — instead it " +
                "was clamped back onto (or short of) the wall line, the exact stall the playtest found");

            // ---- AC2/AC3: walk the crossing. It must reach the goal, it must not linger at the wall
            // line, and it must not reverse heading by more than 90 degrees more than once.
            const float dt = 1f / 60f;
            const float speed = 3.6f;             // a Rusher's own pace
            const float durationSeconds = 10f;
            int ticks = Mathf.RoundToInt(durationSeconds / dt);
            const float wallLineZ = 5f;           // a.ZMax == b.ZMin, the shared boundary
            const float band = 0.25f;
            const float arriveRadius = 1.2f;

            Vector3 at = from;
            float timeNearWallLine = 0f;
            Vector3? previousDir = null;
            int bigReversals = 0;
            bool arrived = false;

            for (int i = 0; i < ticks; i++)
            {
                if ((at - goal).sqrMagnitude <= arriveRadius * arriveRadius) { arrived = true; break; }

                Vector3 way = EnemyNavigation.Waypoint(at, goal, useZoneRoute: true);
                Vector3 dir = way - at;
                dir.y = 0f;
                if (dir.magnitude < 1e-4f) break;   // nowhere to go — exactly the stall this ticket fixes
                dir = dir.normalized;

                if (previousDir.HasValue && Vector3.Angle(previousDir.Value, dir) > 90f) bigReversals++;
                previousDir = dir;

                at += dir * speed * dt;
                if (Mathf.Abs(at.z - wallLineZ) <= band) timeNearWallLine += dt;
            }

            Assert.IsTrue(arrived,
                "the robot never reached the goal in zone b — it stalled on the way, the exact playtest " +
                "bug (a Rusher stationary in an open gateway)");
            Assert.LessOrEqual(timeNearWallLine, 1.0f,
                "the robot spent more than a second within 0.25 m of the doorway wall line — that is " +
                "the stand-and-jitter this ticket fixes, not a clean crossing");
            Assert.LessOrEqual(bigReversals, 1,
                "the crossing reversed heading by more than 90 degrees more than once — that is " +
                "oscillation, not a walk through a doorway");

            // ---- AC4: open ground, unchanged. No cover anywhere in this fixture, so a target in clear
            // line of sight must come back byte-identical — never clamped, never a cell centre.
            var openTarget = new Vector2(2f, 3f);
            Vector2? openStep = ZoneRouteGrid.NextStep(map, a, new Vector2(-2f, -3f), openTarget);
            Assert.AreEqual(openTarget, openStep,
                "a clear line inside the room came back as something other than the target itself");

            // ---- AC5: lastLeg must survive a grid-substituted step. Build a single room with a hedge
            // between the robot and the goal so ZoneRouteGrid.NextStep hands back a cell-centre point
            // instead of the goal itself — precisely the case that broke the OLD
            // `(waypoint - goal).sqrMagnitude < 0.01f` check at RobotEnemy.cs:719 pre-fix. The FIXED
            // check (room membership, mirrored here directly rather than inferred from fan width) must
            // still read true.
            var roomC = new MapZone { id = "c", x = 0f, z = 0f, width = 20f, depth = 10f };
            var mapC = new MapData
            {
                zones = new[] { roomC },
                entities = new[]
                {
                    new MapEntity
                    {
                        id = "hedge", kind = "cover", dressing = "hedge",
                        x = 0f, z = 0f, width = 13.5f, depth = 1f, height = 1.8f,
                    },
                },
            };
            var robotAt = new Vector2(0f, -4f);
            var maxAt = new Vector2(0f, 4f);

            Vector2? substitutedStep = ZoneRouteGrid.NextStep(mapC, roomC, robotAt, maxAt);
            Assert.IsTrue(substitutedStep.HasValue && (substitutedStep.Value - maxAt).sqrMagnitude >= 0.01f,
                "fixture didn't actually force a substituted step — nothing to prove AC5 against");

            bool oldLastLeg = (new Vector3(substitutedStep.Value.x, 0f, substitutedStep.Value.y)
                - new Vector3(maxAt.x, 0f, maxAt.y)).sqrMagnitude < 0.01f;
            Assert.IsFalse(oldLastLeg,
                "sanity: the old waypoint-vs-goal check should read false here — that is the AC5 bug " +
                "this ticket fixes");

            bool fixedLastLeg = roomC.Contains(maxAt.x, maxAt.y);
            Assert.IsTrue(fixedLastLeg,
                "the fixed lastLeg check (room membership) must read true on the final approach even " +
                "when the grid solver substituted a step point");
        }
    }
}
