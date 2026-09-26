using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-954 (triage, read at c04a337a): <see cref="MapGeometry.Floor"/>'s single slab was only 0.1 m
    /// thick -- half the minimum wall thickness -- while <see cref="CharacterControllerMotion.MaxSafeStep"/>/
    /// <see cref="CharacterControllerMotion.MaxSubSteps"/> could still send a single sub-step a full metre
    /// downward during a stall-inflated frame (<c>Time.maximumDeltaTime</c> 0.1 s, MV-883) at the game's
    /// own documented worst-case fall speed (<see cref="CharacterControllerMotion.TerminalFallSpeed"/>,
    /// 40 m/s) -- ten times the floor's old thickness. Lee fell through the world at World 1 gate g20
    /// (a20-&gt;a21) on TestFlight v0.9.9; this ships as defence in depth, not a proven cause (the
    /// ticket's own EditMode repro never reproduced the live bug -- see its "Do not re-raise").
    ///
    /// One EditMode test (testing policy MV-465 Rule 1): World 1's own shipped config, the exact box
    /// <see cref="MapGeometry.Floor"/> produces (the same one <c>MapRuntime.Build</c> spawns as "Map
    /// Floor"), and a Max-sized <see cref="CharacterController"/> dropped onto it at the game's own
    /// worst-case fall speed and frame time. Tier 2 (resolved values, MV-465 Rule 2): the floor collider's
    /// own resolved <see cref="Collider.bounds"/>, and the character's own resolved grounded state and
    /// position after ten such frames -- never an authored constant.
    ///
    /// Fails on base commit 96bbf00 (<see cref="MapGeometry.FloorThickness"/> still 0.1 m):
    /// <c>MV-954: the floor collider must be at least 1.0 m thick -- got 0.1
    /// Expected: greater than or equal to 1.0f
    /// But was:  0.1f</c>
    /// </summary>
    public sealed class MV954FloorThicknessTests
    {
        [Test]
        public void FloorIsThickEnoughThatTerminalFallSpeedNeverEndsUpBelowGround()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "World 1's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            // WorldMapLoader renames a combat area's authored id ("a1") to the old engine's "area<N>"
            // convention -- "area1" is the zone id at runtime, not "a1" (same idiom as MV953).
            MapZone a1 = map.Zone("area1");
            Assert.IsNotNull(a1, "MV-954: world1_config.json must still author area index 1 (a plain floor area, no deck)");

            FloorSlab floorSlab = MapGeometry.Floor(map);
            GameObject floorGo = null, characterGo = null;
            float originalMaxDeltaTime = Time.maximumDeltaTime;
            try
            {
                // The exact box MapRuntime.Build spawns for "Map Floor" (MapRuntime.Box/Spawn), built
                // directly from MapGeometry.Floor rather than the whole MapRuntime.Build pipeline so this
                // test doesn't also spin up every robot/factory/boss World 1 authors.
                floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floorGo.name = "Map Floor";
                floorGo.transform.position = floorSlab.Center;
                floorGo.transform.localScale = floorSlab.Size;
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (see GateSolidityTests)

                Bounds floorBounds = floorGo.GetComponent<Collider>().bounds;
                Assert.AreEqual(0f, floorBounds.max.y, 0.01f,
                    $"MV-954: the floor's top surface must stay exactly at y=0 -- got {floorBounds.max.y:0.###}");
                Assert.GreaterOrEqual(floorBounds.size.y, 1.0f,
                    $"MV-954: the floor collider must be at least 1.0 m thick -- got {floorBounds.size.y:0.###}");

                // Max-sized: radius 0.5 (DECISIONS.md -- Max's CharacterController), height/center match
                // the convention MV952/MV953 already use so a grounded transform.position.y reads near 0.
                characterGo = new GameObject("MV954-falling-character");
                characterGo.transform.position = new Vector3(a1.x, 2f, a1.z);
                var cc = characterGo.AddComponent<CharacterController>();
                cc.radius = 0.5f;
                cc.height = 1.8f;
                cc.center = Vector3.up * (cc.height * 0.5f);
                Physics.SyncTransforms();

                // MV-883's own clamp -- the worst single-frame dt this game allows -- at the game's own
                // documented worst-case fall speed, ten frames of it (the AC's own repro shape).
                Time.maximumDeltaTime = 0.1f;
                Vector3 fallStep = Vector3.down * CharacterControllerMotion.TerminalFallSpeed * Time.maximumDeltaTime;
                for (int i = 0; i < 10; i++)
                    CharacterControllerMotion.SafeMove(cc, fallStep);

                Assert.IsTrue(cc.isGrounded,
                    "MV-954: a character falling at the game's own terminal fall speed must end up grounded " +
                    "on the floor, never through it");
                Assert.That(characterGo.transform.position.y, Is.EqualTo(0f).Within(0.1f),
                    $"MV-954: the character must settle within 0.1 m of the floor -- got y={characterGo.transform.position.y:0.###}");
            }
            finally
            {
                Time.maximumDeltaTime = originalMaxDeltaTime;
                if (characterGo != null) Object.DestroyImmediate(characterGo);
                if (floorGo != null) Object.DestroyImmediate(floorGo);
            }
        }
    }
}
