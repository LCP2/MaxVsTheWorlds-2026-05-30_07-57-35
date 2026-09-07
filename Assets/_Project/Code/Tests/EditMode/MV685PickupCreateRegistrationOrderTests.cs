using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-685 — every ground pickup showed its raw greybox forever, not the MV-313 install-order race
    /// (that gate still fires fine) but a second, distinct ordering bug: <see cref="Pickup.Create"/> used
    /// to call <c>AddComponent&lt;Pickup&gt;()</c> on a GameObject that is active by default, and Unity
    /// fires a freshly-added component's <c>OnEnable</c> synchronously, inside that call, before
    /// <c>AddComponent</c> even returns — while <c>Registered</c> used to be raised from
    /// <c>OnEnable</c>. <c>Create</c> only assigns <see cref="Pickup.Kind"/> and calls
    /// <c>BuildVisual()</c> (which builds the "Visual" greybox child) AFTER that line, so
    /// <see cref="Pickup.Registered"/> fired with <c>Kind</c> still at its default (<see
    /// cref="PickupKind.PowerCell"/>, enum value 0) and no "Visual" child to hide yet.
    /// <c>PickupArtDirector.HideGreybox</c> looks up "Visual" by name and no-ops if it isn't there, so the
    /// greybox was never hidden — and for every kind except PowerCell, the wrong art key got built too,
    /// since <c>OnPickupRegistered</c> reads <c>Kind</c> at the moment it fires. The fix moves the
    /// <c>Registered</c> raise from <c>OnEnable</c> into <see cref="Pickup.Place"/>, which is always
    /// called after both are ready.
    ///
    /// This drives the real <c>Create</c>-then-<c>Place</c> call chain <c>PickupDirector.SpawnDrop</c>
    /// uses, not a hand-built pickup — unlike the other pickup EditMode tests (see
    /// <see cref="PickupArtDirectorRollPartArtKeyTests"/>'s doc comment), this one doesn't need to dodge
    /// <c>BuildVisual</c>'s delayed <c>Destroy()</c>, because raising <c>Registered</c> from an explicit
    /// method call — not from <c>OnEnable</c> — sidesteps the Unity-lifecycle-timing unreliability the
    /// other tests in this suite work around by invoking <c>OnEnable</c> via reflection.
    /// </summary>
    public sealed class MV685PickupCreateRegistrationOrderTests
    {
        [TearDown]
        public void TearDown() => Pickup.ResetRegistry();

        [Test]
        public void CreateThenPlace_FiresRegistered_WithKindAndVisualBothReady()
        {
            PickupKind? observedKind = null;
            bool visualExistedAtRegistration = false;
            int firedCount = 0;

            void Handler(Pickup p)
            {
                firedCount++;
                observedKind = p.Kind;
                visualExistedAtRegistration = p.transform.Find("Visual") != null;
            }

            Pickup.Registered += Handler;
            Pickup pickup = null;
            try
            {
                pickup = Pickup.Create(PickupKind.Supercell);
                pickup.Place(Vector3.zero);

                Assert.AreEqual(1, firedCount,
                    "Create()+Place() must register the pickup exactly once.");
                Assert.AreEqual(PickupKind.Supercell, observedKind,
                    "Registered fired with the wrong Kind — it must not fire until Kind is fully assigned.");
                Assert.IsTrue(visualExistedAtRegistration,
                    "Registered fired before the greybox 'Visual' child existed — PickupArtDirector.HideGreybox can't hide what doesn't exist yet, which is why the greybox never goes away.");
            }
            finally
            {
                Pickup.Registered -= Handler;
                if (pickup != null) Object.DestroyImmediate(pickup.gameObject);
            }
        }
    }
}
