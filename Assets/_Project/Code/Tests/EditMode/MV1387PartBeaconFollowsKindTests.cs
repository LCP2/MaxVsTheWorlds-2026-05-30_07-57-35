using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1387 — the part beacon's active state must equal "this placement is a Supercell" on every
    /// registration, independent of the art-key cache. One pooled instance: Supercell, then Power Cell,
    /// then Supercell again. Asserts the resolved active-in-hierarchy state of the PartBeacon child.
    /// </summary>
    public sealed class MV1387PartBeaconFollowsKindTests
    {
        [TearDown]
        public void TearDown() => Pickup.ResetRegistry();

        private static void SetKind(Pickup p, PickupKind kind) =>
            typeof(Pickup).GetField("<Kind>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(p, kind);

        private static void Invoke(object o, string name) =>
            o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(o, null);

        private static bool BeaconLive(Pickup p)
        {
            var b = p.transform.Find(PickupArtDirector.BeaconName);
            return b != null && b.gameObject.activeInHierarchy;
        }

        [Test]
        public void BeaconIsActiveExactlyWhenKindIsSupercell_AcrossPooledReuse()
        {
            var director = new GameObject("PickupArtDirector(Test)").AddComponent<PickupArtDirector>();
            Invoke(director, "Awake");
            var pickup = new GameObject("Pickup(Test)").AddComponent<Pickup>();
            Invoke(pickup, "OnEnable");
            var register = typeof(PickupArtDirector).GetMethod("OnPickupRegistered", BindingFlags.NonPublic | BindingFlags.Instance);

            bool first, second, third;
            try
            {
                SetKind(pickup, PickupKind.Supercell);
                register.Invoke(director, new object[] { pickup });
                first = BeaconLive(pickup);

                SetKind(pickup, PickupKind.PowerCell);
                register.Invoke(director, new object[] { pickup });
                second = BeaconLive(pickup);

                SetKind(pickup, PickupKind.Supercell);
                register.Invoke(director, new object[] { pickup });
                third = BeaconLive(pickup);
            }
            finally
            {
                Object.DestroyImmediate(pickup.gameObject);
                Object.DestroyImmediate(director.gameObject);
            }

            Assert.IsTrue(first, "Supercell: beacon must be active");
            Assert.IsFalse(second, "Power Cell reuse: beacon must be inactive");
            Assert.IsTrue(third, "Supercell again: beacon must be active");
        }
    }
}
