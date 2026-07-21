using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using MaxWorlds.Combat;
using MaxWorlds.Hose;
using MaxWorlds.Player;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Upgrades;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The upgrades actually work (YT-141). Each of the five is installed the way the game installs it
    /// — pick it up, open the screen, and TAP TO CONTINUE — and then the live weapon/player must
    /// measurably change. Tapping the screen (not just the bare margin outside the panel) has to
    /// dismiss it: the regression that shipped was the panel eating the tap so nothing installed.
    /// One test per part so an effect can't silently stop applying.
    /// </summary>
    public sealed class UpgradeEffectsPlayTests
    {
        private GameObject _max;
        private GameObject _screenGo;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            UpgradeState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
            foreach (var s in Object.FindObjectsByType<UpgradeScreen>(FindObjectsSortMode.None))
                Object.Destroy(s.gameObject);
            yield return null;

            _max = new GameObject("Max");
            _max.tag = "Player";
            _max.AddComponent<CharacterController>();
            _max.AddComponent<WaterBlaster>();
            _max.AddComponent<PlayerController>();
            _max.AddComponent<HoseTether>();

            _screenGo = new GameObject("UpgradeScreen");
            _screenGo.AddComponent<UpgradeScreen>();
            yield return null;   // Awake/Start build the weapon sub-objects and the screen
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (_max != null) Object.Destroy(_max);
            if (_screenGo != null) Object.Destroy(_screenGo);
            yield return null;
            UpgradeState.Reset();
            PickupWallet.Reset();
        }

        private UpgradeScreen Screen => _screenGo.GetComponent<UpgradeScreen>();
        private WaterBlaster Blaster => _max.GetComponent<WaterBlaster>();

        /// <summary>Pick up <paramref name="kind"/>, open the screen, and dismiss it the way a finger
        /// does — a tap ANYWHERE, via the screen's tap-catcher — which must install the effect.</summary>
        private IEnumerator PickUpAndConfirm(PartKind kind)
        {
            PickupWallet.AddPart(kind);
            Screen.Open(UpgradeCatalog.For(kind));
            yield return null;

            Button catcher = FindCatcher();
            Assert.That(catcher, Is.Not.Null,
                "no tap-catcher — a tap on the screen can't register, so nothing installs (YT-141)");

            catcher.onClick.Invoke();   // TAP TO CONTINUE
            yield return null;

            Assert.That(Screen.IsOpen, Is.False, "the tap didn't dismiss the screen");
            Assert.That(UpgradeState.IsInstalled(kind), Is.True, "the confirmed part wasn't installed");
        }

        [UnityTest]
        public IEnumerator BeamNozzle_NarrowsTheBeam_SameReach()
        {
            float baseCone = Blaster.ConeHalfAngle, baseRange = Blaster.Range;
            yield return PickUpAndConfirm(PartKind.BeamNozzle);
            Assert.That(Blaster.ConeHalfAngle, Is.LessThan(baseCone - 1f), "the beam didn't narrow");
            Assert.That(Blaster.Range, Is.EqualTo(baseRange).Within(0.01f), "the beam nozzle shouldn't change reach");
        }

        [UnityTest]
        public IEnumerator PowerNozzle_NarrowsAndLengthens()
        {
            float baseCone = Blaster.ConeHalfAngle, baseRange = Blaster.Range;
            yield return PickUpAndConfirm(PartKind.PowerNozzle);
            Assert.That(Blaster.ConeHalfAngle, Is.LessThan(baseCone - 1f), "the power nozzle didn't narrow");
            Assert.That(Blaster.Range, Is.GreaterThan(baseRange + 0.5f), "the power nozzle didn't lengthen the reach");
        }

        [UnityTest]
        public IEnumerator AugmentationHarness_GrowsTheTank()
        {
            float baseMax = Blaster.Energy.Max;
            yield return PickUpAndConfirm(PartKind.AugmentationHarness);
            Assert.That(Blaster.Energy.Max, Is.GreaterThan(baseMax + 1f), "the harness didn't enlarge the tank");
        }

        [UnityTest]
        public IEnumerator AccelerationEngine_SpeedsMaxUp()
        {
            float baseSpeed = _max.GetComponent<PlayerController>().WalkSpeed;
            yield return PickUpAndConfirm(PartKind.AccelerationEngine);
            Assert.That(_max.GetComponent<PlayerController>().WalkSpeed, Is.GreaterThan(baseSpeed + 0.1f),
                "the acceleration engine didn't speed Max up");
        }

        [UnityTest]
        public IEnumerator Hydro_Untethers()
        {
            Assert.That(UpgradeState.Untethered, Is.False, "precondition");
            yield return PickUpAndConfirm(PartKind.Hydro);
            Assert.That(UpgradeState.Untethered, Is.True, "the Hydro device didn't untether Max");
        }

        [UnityTest]
        public IEnumerator EffectsStack_BeamThenPowerCompoundTheNarrowing()
        {
            float baseCone = Blaster.ConeHalfAngle;
            yield return PickUpAndConfirm(PartKind.BeamNozzle);
            float afterBeam = Blaster.ConeHalfAngle;
            yield return PickUpAndConfirm(PartKind.PowerNozzle);
            float afterBoth = Blaster.ConeHalfAngle;

            Assert.That(afterBeam, Is.LessThan(baseCone), "beam narrowed");
            Assert.That(afterBoth, Is.LessThan(afterBeam - 0.5f), "the second nozzle must compound — upgrades stack");
        }

        [UnityTest]
        public IEnumerator TheReticleReFitsWhenTheBeamNarrows()
        {
            var reticle = _max.GetComponent<MaxWorlds.VFX.AimReticle>();
            Assert.That(reticle, Is.Not.Null, "the blaster should carry its reticle");
            var mesh = ReticleMesh();
            float baseWidth = mesh != null ? mesh.bounds.size.x : -1f;

            yield return PickUpAndConfirm(PartKind.BeamNozzle);

            var mesh2 = ReticleMesh();
            Assert.That(mesh2, Is.Not.Null, "no reticle mesh after install");
            Assert.That(mesh2.bounds.size.x, Is.LessThan(baseWidth - 0.01f),
                "the aim reticle didn't re-fit to the narrower beam — it now lies about the reach");
        }

        [UnityTest]
        public IEnumerator TheTapCatcherCoversTheScreenOnTop_SoATapCantBeEaten()
        {
            PickupWallet.AddPart(PartKind.BeamNozzle);
            Screen.Open(UpgradeCatalog.For(PartKind.BeamNozzle));
            yield return null;

            var catcher = FindCatcher();
            Assert.That(catcher, Is.Not.Null, "no tap-catcher");
            var rt = (RectTransform)catcher.transform;
            Assert.That(rt.anchorMin, Is.EqualTo(Vector2.zero), "the catcher must fill the screen");
            Assert.That(rt.anchorMax, Is.EqualTo(Vector2.one), "the catcher must fill the screen");
            Assert.That(catcher.GetComponent<Image>().raycastTarget, Is.True, "the catcher must take raycasts");
            Assert.That(rt.GetSiblingIndex(), Is.EqualTo(rt.parent.childCount - 1),
                "the catcher must sit on top of the panel, or the panel eats the tap again (the YT-141 bug)");
        }

        private Button FindCatcher()
        {
            foreach (var b in _screenGo.GetComponentsInChildren<Button>(true))
                if (b.gameObject.name == "Tap Catcher") return b;
            return null;
        }

        private static Mesh ReticleMesh()
        {
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                if (mf.gameObject.name == "AimReticle") return mf.sharedMesh;
            return null;
        }
    }
}
