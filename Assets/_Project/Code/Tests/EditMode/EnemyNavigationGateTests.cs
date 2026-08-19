using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The routing bug MV-364 fixes: before <see cref="EnemyNavigation.RegisterGate(string, SubZoneGate)"/>
    /// existed, only an <see cref="MaxWorlds.Arena.AreaGate"/> could tell the router it was shut — a
    /// scene-adopted <see cref="SubZoneGate"/> was invisible to it, so a link naming one always read as
    /// open (<see cref="MapRoutesTests"/> proves the room-graph maths itself was already correct; this
    /// is the bridge that used to be missing on one of the two gate kinds it can be asked about).
    /// </summary>
    public sealed class EnemyNavigationGateTests
    {
        // Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode (confirmed
        // empirically for AreaGate, see AreaGateTests's MV-386 note) — drive Awake directly instead,
        // the same workaround WaterBlasterGateDamageTests/RobotSkinDiagnosticsTests already rely on.
        private static void InvokeAwake(Object component)
        {
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
        }

        [TearDown]
        public void TearDown() => EnemyNavigation.Reset();

        [Test]
        public void ARegisteredSubZoneGate_ReadsClosedUntilItOpens_ThenReadsOpen()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var door = go.AddComponent<SubZoneGate>();
                EnemyNavigation.RegisterGate("gate1", door);

                Assert.IsFalse(EnemyNavigation.IsGateOpen("gate1"),
                    "a freshly built, still-shut SubZoneGate read as passable to the router — a robot " +
                    "would be routed straight at geometry it cannot physically cross");

                door.Open();

                Assert.IsTrue(EnemyNavigation.IsGateOpen("gate1"),
                    "the gate opened but the router still thinks the link is blocked — robots would " +
                    "stall on the near side of a doorway that is now clear");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AnUnregisteredGateId_StillReadsAsOpen_TheOldDefault()
        {
            Assert.IsTrue(EnemyNavigation.IsGateOpen("nothing-built-this"),
                "a link naming a gate nothing registered should read as open, same as before this " +
                "ticket — this class only distrusts a gate it was actually told about");
        }

        /// <summary>
        /// MV-448 cause 2, pinned: <c>Reclose()</c> used to fire nothing, so <c>MapRoutes</c> kept a
        /// route solved through this gate cached forever after an arena reset (MV-427) shut it again —
        /// every robot kept routing at a doorway that was no longer there. This wires the exact same
        /// path a real level does (<c>RegisterGate</c>'s <c>Opened</c>/<c>Closed</c> subscriptions) and
        /// drives <see cref="MapRoutes.Waypoint"/> directly, so it proves the cache actually gets
        /// dropped rather than just that <see cref="EnemyNavigation.IsGateOpen"/> reads correctly (the
        /// two are different bugs — see the class doc comment).
        /// </summary>
        [Test]
        public void ReclosingARegisteredGate_InvalidatesTheCachedRoute_SoTheNextWaypointHolds()
        {
            var map = new MapData
            {
                zones = new[]
                {
                    new MapZone { id = "a", x = 0f, z = 0f, width = 10f, depth = 10f },
                    new MapZone { id = "b", x = 0f, z = 10f, width = 10f, depth = 10f },
                },
                links = new[] { new MapLink { from = "a", to = "b", doorway = 4f, gate = "gate1" } },
            };

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var gate = go.AddComponent<AreaGate>();
                InvokeAwake(gate);
                EnemyNavigation.RegisterGate("gate1", gate, map);

                var from = new Vector2(0f, 0f);
                var goal = new Vector2(0f, 10f);

                Vector2 shut = MapRoutes.Waypoint(map, from, goal, EnemyNavigation.IsGateOpen);
                Assert.AreEqual(from, shut,
                    "a shut gate should hold the robot, not route it at the doorway");

                gate.ForceOpen();
                Vector2 open = MapRoutes.Waypoint(map, from, goal, EnemyNavigation.IsGateOpen);
                Assert.AreNotEqual(from, open, "opening the gate should give a real route through it");

                gate.Reclose();
                Vector2 afterReclose = MapRoutes.Waypoint(map, from, goal, EnemyNavigation.IsGateOpen);
                Assert.AreEqual(from, afterReclose,
                    "Reclose() must drop the cached route the same way Open() does — otherwise every " +
                    "robot keeps routing through a gate an arena reset (MV-427) just shut again");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
