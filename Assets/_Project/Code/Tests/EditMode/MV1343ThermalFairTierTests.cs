using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1343 — Lee's TestFlight iPhone report (0.25.0, World 1, 3 Nov): gpu 9.2ms/frame, iOS already
    /// reporting thermal "fair", but nothing throttled because <see cref="ThermalQualityGovernor"/> only
    /// acted at "serious", and World 1 paid HDR's doubled colour-buffer bandwidth for a look that never
    /// needed it (World 5's neon, MV-1273). Pins both resolved fixes:
    ///
    /// AC1 — the play camera's <see cref="Camera.allowHDR"/> is read off the world table
    /// (<see cref="WorldDefinition.UsesHdr"/>) through <see cref="BackyardLighting.ActiveUsesHdr"/>, off
    /// for World 1, on for the Mothership.
    ///
    /// AC2 — the governor now has a Fair tier that acts the moment iOS reports "fair": soft shadows off,
    /// render scale 0.75, shadow distance 18 — asserted as the RESOLVED URP values (Tier 2, MV-465),
    /// never a literal read back off the governor's own consts.
    /// </summary>
    public sealed class MV1343ThermalFairTierTests
    {
        private GameObject _lightingGo;
        private GameObject _camGo;
        private Camera[] _suppressedAmbientCameras;
        private float _origRenderScale;
        private float _origShadowDistance;
        private bool _origSoftShadows;
        private int _origTargetFrameRate;
        private bool _origActiveUsesHdr;

        [SetUp]
        public void SetUp()
        {
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
            DevTuning.Reset();
            ModalFrameRateGate.ResetForTests();
            _origActiveUsesHdr = BackyardLighting.ActiveUsesHdr;

            var urp = UniversalRenderPipeline.asset;
            Assert.IsNotNull(urp, "no active UniversalRenderPipelineAsset — MobileRenderTuning applies to this, not the authored asset file directly");
            _origRenderScale = urp.renderScale;
            _origShadowDistance = urp.shadowDistance;
            _origSoftShadows = urp.supportsSoftShadows;
            _origTargetFrameRate = Application.targetFrameRate;
            MobileRenderTuning.ApplyRenderScale(MobileRenderTuning.DefaultRenderScale);
            MobileRenderTuning.ApplyShadowDistance(MobileRenderTuning.DefaultShadowDistance);
            MobileRenderTuning.ApplySoftShadows(true);
            Application.targetFrameRate = ModalFrameRateGate.ActiveFrameRate;
        }

        [TearDown]
        public void TearDown()
        {
            if (_lightingGo != null) Object.DestroyImmediate(_lightingGo);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);

            MobileRenderTuning.ApplyRenderScale(_origRenderScale);
            MobileRenderTuning.ApplyShadowDistance(_origShadowDistance);
            MobileRenderTuning.ApplySoftShadows(_origSoftShadows);
            Application.targetFrameRate = _origTargetFrameRate;
            BackyardLighting.ActiveUsesHdr = _origActiveUsesHdr;
            DevTuning.Reset();
            ModalFrameRateGate.ResetForTests();
        }

        [Test]
        public void CameraHdrIsPerWorld_AndGovernorActsAtFair()
        {
            // --- AC1: HDR resolved per world, off for World 1, on for the Mothership. ---
            _camGo = new GameObject("MV1343 Main Camera", typeof(Camera)) { tag = "MainCamera" };
            var cam = _camGo.GetComponent<Camera>();

            _lightingGo = new GameObject("MV1343 lighting");
            var lighting = _lightingGo.AddComponent<BackyardLighting>();

            WorldDefinition backyard = WorldCatalog.Get(WorldCatalog.IndexOf(WorldIds.Backyard));
            WorldDefinition mothership = WorldCatalog.Get(WorldCatalog.IndexOf(WorldIds.Mothership));

            Assert.IsFalse(backyard.UsesHdr, "World 1 gains nothing from HDR (MV-1343) — the table must say so.");
            Assert.IsTrue(mothership.UsesHdr, "World 5's neon look (MV-1273) is the one world that still needs HDR.");

            BackyardLighting.ActiveUsesHdr = backyard.UsesHdr;
            lighting.Apply(backyard.Look);
            Assert.IsFalse(cam.allowHDR, "World 1's play camera must render LDR — HDR doubles colour-buffer bandwidth for no visual gain here.");

            BackyardLighting.ActiveUsesHdr = mothership.UsesHdr;
            lighting.Apply(mothership.Look);
            Assert.IsTrue(cam.allowHDR, "the Mothership's neon look still needs the camera's HDR headroom.");

            // --- AC2: the governor acts the moment iOS reports "fair", not only at "serious". ---
            var urp = UniversalRenderPipeline.asset;
            var governor = new ThermalQualityGovernor();

            governor.Tick(0f, "fair");
            Assert.AreEqual(ThermalTier.Fair, governor.AppliedTier,
                "\"fair\" must resolve to its own Fair tier — iOS already reports fair while nothing throttles (MV-1343).");
            Assert.AreEqual(ThermalQualityGovernor.FairRenderScale, urp.renderScale,
                "Fair must resolve renderScale to 0.75.");
            Assert.AreEqual(ThermalQualityGovernor.FairShadowDistance, urp.shadowDistance,
                "Fair must resolve shadowDistance to 18.");
            Assert.IsFalse(urp.supportsSoftShadows,
                "Fair must turn soft shadows off.");
        }
    }
}
