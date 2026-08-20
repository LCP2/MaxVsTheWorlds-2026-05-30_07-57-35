using System.Collections.Generic;
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
    /// inactive→active transition) — now meaningfully testable with ten keys in the pool instead of one.
    /// Driven by reflection since <c>RollPartArtKey</c> is a private instance method with no other
    /// surface to exercise it through outside Play Mode (same idiom as <see cref="PickupArtDirectorScaleTests"/>).
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

        [Test]
        public void RollPartArtKey_ReturnsMoreThanOneDistinctKey_Over50FreshDrops()
        {
            var director = new GameObject("PickupArtDirector(Test)").AddComponent<PickupArtDirector>();
            var pickup = BarePickup();
            // No fixed seed: UnityEngine.Random is process-global and other EditMode tests (e.g. combat
            // AI) also draw from it, so re-seeding here would leak into whatever runs next in the same
            // batch. With 10 keys in the pool, 50 draws landing on the same one is ~1e-49 regardless of
            // seed, so leaving the stream alone costs nothing.

            var seen = new HashSet<string>();
            for (int i = 0; i < 50; i++)
            {
                pickup.gameObject.SetActive(false);
                InvokeRoll(director, pickup);               // records the inactive edge, no reroll
                pickup.gameObject.SetActive(true);
                seen.Add(InvokeRoll(director, pickup));      // inactive -> active: a fresh drop, rerolls
            }

            Assert.Greater(seen.Count, 1,
                "50 fresh drops from a 10-key pool should produce more than one distinct design.");

            Object.DestroyImmediate(pickup.gameObject);
            Object.DestroyImmediate(director.gameObject);
        }
    }
}
