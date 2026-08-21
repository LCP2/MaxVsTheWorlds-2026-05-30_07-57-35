using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-454 AC4 — the Supercell art key has to keep handing back the same design for a pickup that's
    /// just sitting on the ground, and only reroll on a fresh drop — now meaningfully testable with ten
    /// keys in the pool instead of one.
    ///
    /// MV-527: the reroll used to be driven by polling an active/inactive transition inside
    /// <c>RollPartArtKey</c>, invoked every frame from <c>Update</c>. It's now driven once per placement
    /// by <c>Pickup.Registered</c> (<c>PickupArtDirector.OnPickupRegistered</c>), which stores the result
    /// in the private <c>_partArtKey</c> dictionary read by <c>Update</c>. These tests now drive that
    /// same event instead of the old method, and read the key back off the dictionary — same invariant,
    /// exercised through the new mechanism. Driven by reflection since both are private with no other
    /// surface to exercise them through outside Play Mode (same idiom as <see cref="PickupArtDirectorScaleTests"/>).
    /// </summary>
    public sealed class PickupArtDirectorRollPartArtKeyTests
    {
        private static void InvokeRegistered(PickupArtDirector director, Pickup pickup)
        {
            var method = typeof(PickupArtDirector).GetMethod("OnPickupRegistered",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "PickupArtDirector.OnPickupRegistered went missing");
            method.Invoke(director, new object[] { pickup });
        }

        private static string ReadKey(PickupArtDirector director, Pickup pickup)
        {
            var field = typeof(PickupArtDirector).GetField("_partArtKey",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "PickupArtDirector._partArtKey went missing");
            var dict = (Dictionary<Pickup, string>)field.GetValue(director);
            Assert.IsTrue(dict.TryGetValue(pickup, out string key), "the pickup was never assigned a key");
            return key;
        }

        /// <summary>A bare Supercell pickup — <c>OnPickupRegistered</c> only rerolls a Part design for
        /// <see cref="PickupKind.Supercell"/> (PowerCell/Device always wear a fixed key), and
        /// <see cref="Pickup.Kind"/> has a private setter only <see cref="Pickup.Create"/> normally
        /// reaches, which this skips (its greybox build calls a delayed <c>Destroy</c> illegal outside
        /// Play mode — same reason <see cref="PickupArtDirectorScaleTests"/> does the same thing).</summary>
        private static Pickup BarePickup()
        {
            var pickup = new GameObject("Pickup(Test)").AddComponent<Pickup>();
            typeof(Pickup).GetProperty("Kind", BindingFlags.Public | BindingFlags.Instance)
                .SetValue(pickup, PickupKind.Supercell);
            return pickup;
        }

        [Test]
        public void PartArtKey_StaysTheSame_AcrossRepeatedUpdatesWithNoFreshDrop()
        {
            var director = new GameObject("PickupArtDirector(Test)").AddComponent<PickupArtDirector>();
            var pickup = BarePickup();
            pickup.gameObject.SetActive(true);

            InvokeRegistered(director, pickup);   // one placement — the only reroll
            string first = ReadKey(director, pickup);
            string second = ReadKey(director, pickup);
            string third = ReadKey(director, pickup);

            Assert.AreEqual(first, second, "the key should stay stable while the pickup sits on the ground.");
            Assert.AreEqual(first, third, "the key should stay stable while the pickup sits on the ground.");

            Object.DestroyImmediate(pickup.gameObject);
            Object.DestroyImmediate(director.gameObject);
        }

        [Test]
        public void PartArtKey_ReturnsMoreThanOneDistinctKey_Over50FreshDrops()
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
                InvokeRegistered(director, pickup);   // one call == one fresh drop, unambiguously
                seen.Add(ReadKey(director, pickup));
            }

            Assert.Greater(seen.Count, 1,
                "50 fresh drops from a 10-key pool should produce more than one distinct design.");

            Object.DestroyImmediate(pickup.gameObject);
            Object.DestroyImmediate(director.gameObject);
        }
    }
}
