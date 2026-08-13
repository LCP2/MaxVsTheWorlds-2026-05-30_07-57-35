using NUnit.Framework;
using UnityEngine;
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
    }
}
