using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The scene-adopted gate's open/closed contract that MV-364 depends on: <see cref="SubZoneGate"/>
    /// has to say, the INSTANT it opens, both "I am passable" (<see cref="SubZoneGate.Unlocked"/>) and
    /// "tell whoever is routing around me" (<see cref="SubZoneGate.Opened"/>) — not after the cosmetic
    /// sink finishes. <see cref="MaxWorlds.Arena.AreaGate"/> already made this promise; this pins
    /// <see cref="SubZoneGate"/> to the same one, since <see cref="MaxWorlds.Enemies.EnemyNavigation"/>
    /// now trusts both equally.
    /// </summary>
    public sealed class SubZoneGateTests
    {
        [Test]
        public void BeforeOpening_TheGateIsNotUnlocked()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var door = go.AddComponent<SubZoneGate>();
                Assert.IsFalse(door.Unlocked, "a freshly built gate should be shut");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Open_FiresOpenedTheInstantTheGateBeginsOpening_NotAfterTheSink()
        {
            // Awake — which caches the Collider reference Open() disables — only runs on scene load /
            // in Play mode, not for a component added via AddComponent in an EditMode test (Unity
            // refuses to run behaviour messages like Awake outside a running player loop). What this
            // CAN pin without that: the state machine Open() itself owns — Unlocked and Opened firing
            // immediately — which is exactly the contract EnemyNavigation depends on. The collider drop
            // itself is covered by AreaGatePlayTests' equivalent for the sibling gate kind.
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var door = go.AddComponent<SubZoneGate>();
                bool fired = false;
                door.Opened += () => fired = true;

                door.Open();

                Assert.IsTrue(fired,
                    "Opened did not fire the instant the gate began opening — a router listening for " +
                    "it (EnemyNavigation) would keep routing robots at a doorway that is now clear");
                Assert.IsTrue(door.Unlocked, "the gate opened but Unlocked still reads false");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
