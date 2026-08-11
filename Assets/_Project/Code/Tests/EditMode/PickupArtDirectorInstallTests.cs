using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-313 — the self-install race that left every pickup wearing its raw greybox in the live/WebGL
    /// build. <see cref="PickupArtDirector"/> used to gate its own AfterSceneLoad install on
    /// <c>FindFirstObjectByType&lt;PickupDirector&gt;()</c> from inside its own AfterSceneLoad callback,
    /// but <c>PickupDirector</c> installs through that exact same idiom, and Unity does not guarantee
    /// which of two classes' AfterSceneLoad callbacks runs first — the IL2CPP/WebGL build was resolving
    /// that race the opposite way the Editor does, which is exactly why the isolated PlayMode suite
    /// stayed green while the live build stayed broken.
    ///
    /// EditMode, not PlayMode: MonoBehaviour lifecycle methods never fire on their own outside Play
    /// Mode, so <c>InstallGate.Start()</c> is driven directly by reflection, the same idiom
    /// <c>SpringGutsPlayTests.Install_CreatesADirector_AndRefusesToCreateASecond</c> uses for a static
    /// Install method. That lets this cover the actual regression — order independence — without ever
    /// needing the play loop.
    /// </summary>
    public sealed class PickupArtDirectorInstallTests
    {
        [TearDown]
        public void TearDown()
        {
            foreach (var d in Object.FindObjectsByType<PickupArtDirector>(FindObjectsInactive.Include,
                                                                            FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsInactive.Include,
                                                                        FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);

            var gate = GameObject.Find("PickupArtInstallGate");
            if (gate != null) Object.DestroyImmediate(gate);
        }

        private static void InvokeInstall()
        {
            var install = typeof(PickupArtDirector).GetMethod("Install",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(install, "PickupArtDirector.Install went missing — self-install is the convention");
            install.Invoke(null, null);
        }

        /// <summary>Fires the gate's Start() by hand — the same call Unity makes on the frame after
        /// AfterSceneLoad, but on demand so the test can control exactly when it lands relative to
        /// PickupDirector appearing.</summary>
        private static void FireGateStart()
        {
            var gateType = typeof(PickupArtDirector).GetNestedType("InstallGate", BindingFlags.NonPublic);
            Assert.IsNotNull(gateType, "PickupArtDirector.InstallGate went missing");

            var gateGo = GameObject.Find("PickupArtInstallGate");
            Assert.IsNotNull(gateGo, "Install() didn't create the deferred install gate");

            var gate = gateGo.GetComponent(gateType);
            Assert.IsNotNull(gate, "the gate object has no InstallGate component on it");

            var start = gateType.GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(start, "InstallGate.Start went missing");
            start.Invoke(gate, null);
        }

        [Test]
        public void PickupDirector_AlreadyPresent_GateInstallsTheRealDirector()
        {
            new GameObject("PickupDirector(Test)").AddComponent<PickupDirector>();

            InvokeInstall();
            FireGateStart();

            Assert.AreEqual(1, Object.FindObjectsByType<PickupArtDirector>(FindObjectsSortMode.None).Length,
                "the gate should have installed the real director once PickupDirector was found");
        }

        [Test]
        public void PickupDirector_AppearsAfterInstallRuns_GateStillInstallsTheRealDirector()
        {
            // The exact live-build regression: PickupArtDirector's own AfterSceneLoad callback (Install)
            // ran and lost the race — PickupDirector didn't exist yet at that instant. Before MV-313 this
            // meant PickupArtDirector never got a second chance and no pickup was ever dressed again.
            InvokeInstall();
            new GameObject("PickupDirector(Test)").AddComponent<PickupDirector>();
            FireGateStart();

            Assert.AreEqual(1, Object.FindObjectsByType<PickupArtDirector>(FindObjectsSortMode.None).Length,
                "the gate must not give up just because PickupDirector wasn't there the instant Install() ran");
        }

        [Test]
        public void PickupDirector_NeverAppears_GateRemovesItselfWithoutInstalling()
        {
            InvokeInstall();
            FireGateStart();

            Assert.AreEqual(0, Object.FindObjectsByType<PickupArtDirector>(FindObjectsSortMode.None).Length,
                "with no PickupDirector at all (e.g. a shared test scene), the gate must not install");
            Assert.IsNull(GameObject.Find("PickupArtInstallGate"),
                "a gate that gave up should remove itself, not sit around doing nothing forever");
        }

        [Test]
        public void Install_IsIdempotent_NeverCreatesASecondGateOrDirector()
        {
            new GameObject("PickupDirector(Test)").AddComponent<PickupDirector>();

            InvokeInstall();
            InvokeInstall();
            FireGateStart();
            InvokeInstall();

            Assert.AreEqual(1, Object.FindObjectsByType<PickupArtDirector>(FindObjectsSortMode.None).Length,
                "installing repeatedly must not leave two directors fighting over the same pickups");
        }
    }
}
