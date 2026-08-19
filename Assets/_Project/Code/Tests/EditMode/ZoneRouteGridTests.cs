using NUnit.Framework;
using UnityEngine;

using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-476: a melee robot separated from its goal by a hedge row must walk to the gap and through
    /// it, not press into the hedge and slide — <see cref="MapRoutes"/>'s own doc comment says out loud
    /// that it deliberately does not avoid cover inside a room, and areas now hold enough authored
    /// hedge to form a maze, so that assumption stopped holding. This is the single EditMode test the
    /// project's testing policy (MV-465 Rule 1) allows for this ticket: one fixture, one proof that the
    /// core regression is fixed, plus the cheap-to-check invariants (no detour on open ground, the grid
    /// is rasterised once and not per query) folded into the same run rather than spent as separate
    /// tests.
    ///
    /// Before <see cref="ZoneRouteGrid"/> existed this test does not compile at all — <c>ZoneRouteGrid</c>
    /// is new — which is the "fails on the base commit" the policy asks for; there is no prior behaviour
    /// to run this against, only the absence of the class.
    /// </summary>
    public sealed class ZoneRouteGridTests
    {
        /// <summary>A single 20x10 m room with a hedge row splitting it at z=0, open only through a
        /// 3 m gap around x≈5 — a maze in miniature, the exact shape the ticket describes: "hedges in
        /// between... representing something of a maze".</summary>
        private static MapData RoomWithAHedgeGap(out MapZone room, out Rect hedgeA, out Rect hedgeB)
        {
            room = new MapZone { id = "room", x = 0f, z = 0f, width = 20f, depth = 10f };

            var map = new MapData
            {
                zones = new[] { room },
                entities = new[]
                {
                    new MapEntity
                    {
                        id = "hedgeA", kind = "cover", dressing = "hedge",
                        x = -3.25f, z = 0f, width = 13.5f, depth = 1f, height = 1.8f,
                    },
                    new MapEntity
                    {
                        id = "hedgeB", kind = "cover", dressing = "hedge",
                        x = 8.25f, z = 0f, width = 3.5f, depth = 1f, height = 1.8f,
                    },
                },
            };

            hedgeA = new Rect(-10f, -0.5f, 13.5f, 1f);
            hedgeB = new Rect(6.5f, -0.5f, 3.5f, 1f);
            return map;
        }

        /// <summary>Walk a robot from <paramref name="from"/> to <paramref name="goal"/>, asking
        /// <see cref="ZoneRouteGrid.NextStep"/> the way at every step — the same shape as
        /// <c>MapRoutesTests.Walks</c>, one level down (inside a room instead of across rooms).
        /// Returns false, with <paramref name="pressedIntoHedge"/> true, the instant the walk enters
        /// either hedge's REAL (uninflated) footprint — pressing into the hedge is exactly the bug.</summary>
        private static bool Walks(MapData map, MapZone room, Vector2 from, Vector2 goal,
                                  Rect hedgeA, Rect hedgeB, out bool pressedIntoHedge, out int steps)
        {
            const float Step = 0.3f;
            const float Arrived = 0.3f;
            const int Limit = 500;

            Vector2 at = from;
            pressedIntoHedge = false;

            for (steps = 0; steps < Limit; steps++)
            {
                if ((at - goal).magnitude <= Arrived) return true;

                Vector2? next = ZoneRouteGrid.NextStep(map, room, at, goal);
                if (!next.HasValue) return false;   // decision #10: caller falls back, never freezes

                Vector2 dir = next.Value - at;
                if (dir.magnitude < 1e-4f) return false;

                at += dir.normalized * Step;

                if (hedgeA.Contains(at) || hedgeB.Contains(at)) { pressedIntoHedge = true; return false; }
            }

            return false;
        }

        [Test]
        public void MeleeRobotRoutesThroughTheHedgeGap_NeverPressingIntoTheHedge()
        {
            MapData map = RoomWithAHedgeGap(out MapZone room, out Rect hedgeA, out Rect hedgeB);

            // ---- AC1/AC2: blocked by the hedge, the robot detours to the gap and through it.
            var from = new Vector2(0f, -4f);
            var goal = new Vector2(0f, 4f);

            bool arrived = Walks(map, room, from, goal, hedgeA, hedgeB,
                out bool pressedIntoHedge, out int steps);

            Assert.IsFalse(pressedIntoHedge,
                "the robot walked into the hedge's own footprint instead of routing around it to the gap");
            Assert.IsTrue(arrived,
                $"the robot never reached the goal — it gave up after {steps} steps instead of finding " +
                "the gap in the hedge row");

            // ---- AC3: on open ground (same room, well clear of the hedge band), the solver must not
            // invent a detour — it hands back the goal itself, exactly like MapRoutes does with nothing
            // in the way.
            var openFrom = new Vector2(-8f, -4f);
            var openGoal = new Vector2(-2f, -4f);
            Vector2? openStep = ZoneRouteGrid.NextStep(map, room, openFrom, openGoal);

            Assert.AreEqual(openGoal, openStep,
                "a clear line inside the room came back as something other than the goal itself — that " +
                "is a detour on open ground, which the ticket explicitly forbids");

            // ---- AC5: the zone's grid is rasterised once, not once per question — every call above
            // (the whole walk, plus the open-ground check) must have built it exactly once.
            int buildsAfterEverythingAbove = ZoneRouteGrid.GridBuilds;

            for (int i = 0; i < 20; i++) ZoneRouteGrid.NextStep(map, room, from, goal);

            Assert.AreEqual(buildsAfterEverythingAbove, ZoneRouteGrid.GridBuilds,
                "the room's cover was rasterised again on a later question — that has to happen once " +
                "per zone per invalidation, not once per robot per frame");
        }
    }
}
