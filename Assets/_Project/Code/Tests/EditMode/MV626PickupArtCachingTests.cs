using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-626 change 4 — <c>PickupArtDirector.Update</c> used to re-resolve every live pickup's art
    /// child, glint dots and Core band by name (<c>Transform.Find</c> plus a
    /// <c>TryGetComponent&lt;MeshRenderer&gt;</c>) every single frame, for every live pickup, on top of
    /// the accumulation this ticket's first three changes fix — see
    /// <see cref="MV626PickupCapAndLifetimeTests"/>. <c>BuildArtState</c> now resolves those once, on the
    /// <c>Pickup.Registered</c> event (a fresh drop or a pooled reuse), and <c>Update</c> only ever reads
    /// the cached references afterwards.
    ///
    /// Same "prove nothing gets re-resolved" idiom as <c>MV527AllocationGuardTests</c>' frustum-planes
    /// buffer-identity check and <c>MV611DissolveMeshFilterCacheTests</c>' cache-miss counter: rather than
    /// intercepting every <c>Transform.Find</c> call, this reads the director's own resolve counter
    /// (<c>_artResolveCount</c>, mirroring <c>DissolveVfx._meshFilterCacheMisses</c>) and asserts the
    /// cached art <c>Transform</c>'s reference identity holds across repeated <c>Update()</c> calls.
    ///
    /// <c>Pickup.OnEnable</c> — which populates <see cref="Pickup.Active"/>, the list <c>Update</c> walks
    /// — is invoked by hand for the same reason <c>MV611DissolveMeshFilterCacheTests</c> invokes
    /// <c>RobotEnemy.OnEnable</c> directly: Unity does not reliably call a MonoBehaviour's OnEnable for
    /// <c>AddComponent</c> outside Play mode, and without it the pickup would never be in
    /// <c>Pickup.Active</c> for <c>Update</c> to actually walk — making a "did Update re-resolve"
    /// assertion vacuously true regardless of whether the cache works.
    /// </summary>
    public sealed class MV626PickupArtCachingTests
    {
        [TearDown]
        public void TearDown() => Pickup.ResetRegistry();

        private static void InvokePickupOnEnable(Pickup pickup) =>
            typeof(Pickup).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(pickup, null);

        /// <summary>Unity does not reliably call Awake for AddComponent outside Play mode either — this
        /// director's Awake is what builds <c>_mpb</c>, which PulseGlisten/PulseCellCore need. Without
        /// this, calling Update() directly (as every test below does) throws inside GetPropertyBlock.</summary>
        private static void InvokeDirectorAwake(PickupArtDirector director) =>
            typeof(PickupArtDirector).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, null);

        private static void InvokeRegistered(PickupArtDirector director, Pickup pickup) =>
            typeof(PickupArtDirector).GetMethod("OnPickupRegistered", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { pickup });

        private static void InvokeUpdate(PickupArtDirector director) =>
            typeof(PickupArtDirector).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, null);

        private static int ResolveCount(PickupArtDirector director) =>
            (int)typeof(PickupArtDirector).GetField("_artResolveCount", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(director);

        private static object ArtStateFor(PickupArtDirector director, Pickup pickup)
        {
            var dict = (IDictionary)typeof(PickupArtDirector)
                .GetField("_artState", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(director);
            Assert.IsTrue(dict.Contains(pickup),
                "the pickup was never given an ArtState — BuildArtState didn't run on registration");
            return dict[pickup];
        }

        private static Transform ArtOf(object artState) =>
            (Transform)artState.GetType().GetField("Art").GetValue(artState);

        /// <summary>Skips <c>Pickup.Create</c>'s greybox build — its delayed <c>Destroy()</c> on the
        /// collider is illegal outside Play mode, same reason <c>PickupArtDirectorScaleTests</c> and
        /// <c>PickupArtDirectorRollPartArtKeyTests</c> build a bare pickup by hand. <c>Kind</c> defaults
        /// to <see cref="PickupKind.PowerCell"/> (enum value 0). Registers it into
        /// <see cref="Pickup.Active"/> by hand — see the class doc comment.</summary>
        private static Pickup BarePickup()
        {
            var pickup = new GameObject("Pickup(Test)").AddComponent<Pickup>();
            pickup.gameObject.SetActive(true);
            InvokePickupOnEnable(pickup);
            return pickup;
        }

        [Test]
        public void ArtIsResolvedOnceOnRegistration_NotAgainOnRepeatedUpdates()
        {
            var director = new GameObject("PickupArtDirector(Test)").AddComponent<PickupArtDirector>();
            InvokeDirectorAwake(director);
            var pickup = BarePickup();

            try
            {
                InvokeRegistered(director, pickup);
                int afterRegistration = ResolveCount(director);
                Assert.That(afterRegistration, Is.EqualTo(1), "one placement must resolve the art exactly once");

                Transform artAfterRegistration = ArtOf(ArtStateFor(director, pickup));
                Assert.IsNotNull(artAfterRegistration, "registration must have built the power cell's art");

                InvokeUpdate(director);
                InvokeUpdate(director);
                InvokeUpdate(director);

                Assert.That(ResolveCount(director), Is.EqualTo(afterRegistration),
                    "Update() must never re-resolve the art — that's the per-frame Find/GetComponent MV-626 removed");
                Assert.AreSame(artAfterRegistration, ArtOf(ArtStateFor(director, pickup)),
                    "the cached art Transform reference must stay the exact same instance across frames");
            }
            finally
            {
                Object.DestroyImmediate(pickup.gameObject);
                Object.DestroyImmediate(director.gameObject);
            }
        }

        [Test]
        public void APooledReuse_ReResolvesOnceOnItsOwnRegistration_NotPerFrame()
        {
            // A pooled pickup is redropped without ever being destroyed — PickupDirector just calls
            // Place() again, which fires Pickup.Registered a second time. That second registration is
            // allowed to resolve again (it's still "once per placement", not "once ever"); what must NOT
            // happen is a third, fourth, fifth resolve from sitting through more Update() frames.
            var director = new GameObject("PickupArtDirector(Test)").AddComponent<PickupArtDirector>();
            InvokeDirectorAwake(director);
            var pickup = BarePickup();

            try
            {
                InvokeRegistered(director, pickup);
                InvokeUpdate(director);
                InvokeRegistered(director, pickup);   // pooled reuse: a second, legitimate placement
                int afterTwoPlacements = ResolveCount(director);
                Assert.That(afterTwoPlacements, Is.EqualTo(2), "two placements must resolve twice, once each");

                InvokeUpdate(director);
                InvokeUpdate(director);

                Assert.That(ResolveCount(director), Is.EqualTo(afterTwoPlacements),
                    "frames between placements must never trigger another resolve");
            }
            finally
            {
                Object.DestroyImmediate(pickup.gameObject);
                Object.DestroyImmediate(director.gameObject);
            }
        }
    }
}
