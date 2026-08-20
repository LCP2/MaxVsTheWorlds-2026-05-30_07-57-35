using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-454 AC4 — <see cref="PickupArtDirector.RollPartArtKey"/> has to keep handing back the same key
    /// for a pickup that's just sitting on the ground, and only reroll on a fresh drop (an
    /// inactive→active transition). MV-498 collapsed <see cref="WeaponPartArt.MachineInternalsKeys"/>
    /// back to one design, so the companion "50 fresh drops produce more than one distinct key" coverage
    /// this class used to carry is gone — with one key in the pool that property is no longer true by
    /// design, not a regression. Driven by reflection since <c>RollPartArtKey</c> is a private instance
    /// method with no other surface to exercise it through outside Play Mode (same idiom as
    /// <see cref="PickupArtDirectorScaleTests"/>).
    /// </summary>
    public sealed class PickupArtDirectorRollPartArtKeyTests
    {
        private static string InvokeRoll(PickupArtDirector director, Pickup pickup)
        {
            var method = typeof(PickupArtDirector).GetMethod("RollPartArtKey",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "PickupArtDirector.RollPartArtKey went missing");
            return (string)method.Invoke(director, new object[] { pickup });
        }

        private static Pickup BarePickup() => new GameObject("Pickup(Test)").AddComponent<Pickup>();

        [Test]
        public void RollPartArtKey_ReturnsTheSameKey_AcrossRepeatedCallsWithNoFreshDrop()
        {
            var director = new GameObject("PickupArtDirector(Test)").AddComponent<PickupArtDirector>();
            var pickup = BarePickup();
            pickup.gameObject.SetActive(true);

            string first = InvokeRoll(director, pickup);
            string second = InvokeRoll(director, pickup);
            string third = InvokeRoll(director, pickup);

            Assert.AreEqual(first, second, "the key should stay stable while the pickup sits on the ground.");
            Assert.AreEqual(first, third, "the key should stay stable while the pickup sits on the ground.");

            Object.DestroyImmediate(pickup.gameObject);
            Object.DestroyImmediate(director.gameObject);
        }
    }
}
