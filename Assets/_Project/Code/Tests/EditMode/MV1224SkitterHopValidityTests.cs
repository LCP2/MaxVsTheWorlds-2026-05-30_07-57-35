using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1224 AC1: a Skitter's hop must never land inside cover, inside or beyond a wall, or outside
    /// its own area, and every hop distance must stay within the ticket's own 2.5-3.5 m band. Ticks
    /// <see cref="SkitterHop.TryResolveLanding"/> 50 times with an explicit, alternating sign — the same
    /// zig-zag <c>RobotEnemy.TickChase</c>'s own Skitter gate drives, here driven directly rather than
    /// through the whole MonoBehaviour/CharacterController machinery so the fixture stays deterministic
    /// (no incidental drift from ordinary between-hop walking) — across a hand-built <see cref="MapData"/>
    /// room with a wall (the zone's own boundary; <see cref="MapZone"/>'s own doc comment: walls are
    /// DERIVED from a zone's edges, never authored separately) and a cover piece sitting on the direct
    /// line between the Skitter's start and Max. Every resolved landing is checked against the fixture's
    /// own authored zone/cover geometry — independent ground truth, not a second call into the function
    /// under test.
    ///
    /// Fails on e0c0fc7 (MV-1223's merge, the ticket's own named base): neither
    /// <see cref="EnemyKind.Skitter"/> nor <see cref="SkitterHop"/> exist there, so this test does not
    /// compile on that commit.
    /// </summary>
    public sealed class MV1224SkitterHopValidityTests
    {
        private static readonly Vector3 CoverCenter = new Vector3(0f, 0f, 0f);
        private static readonly Vector3 CoverSize = new Vector3(2f, 1.5f, 2f);

        /// <summary>EnemyArchetype.Skitter.ColliderRadius (0.2) plus a small margin — the clearance a
        /// real spawn's CharacterController would pass in.</summary>
        private const float ClearRadius = 0.3f;

        private static MapData BuildRoom()
        {
            return new MapData
            {
                zones = new[]
                {
                    new MapZone { id = "room", x = 0f, z = 0f, width = 16f, depth = 10f, level = 0 },
                },
                entities = new[]
                {
                    new MapEntity
                    {
                        id = "cover1", kind = "cover",
                        x = CoverCenter.x, z = CoverCenter.z,
                        width = CoverSize.x, height = CoverSize.y, depth = CoverSize.z,
                        shape = "box",
                    },
                },
            };
        }

        [Test]
        public void FiftyHops_NeverLandInCoverOrOutsideTheRoom_AndEveryHopStaysUnder3Point5Metres()
        {
            MapData map = BuildRoom();
            MapZone room = map.Zone("room");
            ArenaCover cover = map.entities[0].ToCover();
            Vector3 max = new Vector3(6f, 0f, 3f);
            Vector3 from = new Vector3(-6f, 0f, -3f);
            float sign = 1f;
            int hops = 0;

            for (int i = 0; i < 50; i++)
            {
                sign = -sign;
                if (!SkitterHop.TryResolveLanding(map, from, max, sign, ClearRadius, out Vector3 landing))
                    continue; // the ticket's own "skips the hop" — nothing landed, nothing to assert

                hops++;

                float distance = Vector3.Distance(from, landing);
                Assert.LessOrEqual(distance, SkitterHop.MaxDistance + 0.01f,
                    $"hop {i}: travelled {distance:0.00} m, over the ticket's own 3.5 m cap");

                Assert.GreaterOrEqual(landing.x, room.XMin + ClearRadius - 0.001f,
                    $"hop {i}: landed at {landing} — inside or beyond the room's west wall");
                Assert.LessOrEqual(landing.x, room.XMax - ClearRadius + 0.001f,
                    $"hop {i}: landed at {landing} — inside or beyond the room's east wall");
                Assert.GreaterOrEqual(landing.z, room.ZMin + ClearRadius - 0.001f,
                    $"hop {i}: landed at {landing} — inside or beyond the room's south wall");
                Assert.LessOrEqual(landing.z, room.ZMax - ClearRadius + 0.001f,
                    $"hop {i}: landed at {landing} — inside or beyond the room's north wall");

                float coverDistance = cover.DistanceTo(new Vector2(landing.x, landing.z));
                Assert.GreaterOrEqual(coverDistance, ClearRadius - 0.001f,
                    $"hop {i}: landed at {landing}, {coverDistance:0.00} m from the cover piece — inside or touching it");

                from = landing;
            }

            Assert.Greater(hops, 0, "the fixture must actually exercise at least one successful hop");
        }
    }
}
