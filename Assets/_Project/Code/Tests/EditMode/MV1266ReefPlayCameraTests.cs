using NUnit.Framework;
using UnityEngine;
using MaxWorlds.CameraRig;
using MaxWorlds.Dev;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1266 (the one new test): the "mv1266reefplaycam" capture preset leaves the live
    /// CinemachineBrain in control of the camera — no FrameZone-style manual pose, no
    /// <c>DisableBrain</c> override — so the pose it actually renders at has to come from the
    /// production <see cref="FixedAngleCameraRig"/>, resolved live via
    /// <see cref="CaptureDirector.ResolveProductionCameraPose"/>, not a copied literal that could
    /// silently drift from the rig the way the scene-vs-code desktop distance already has once
    /// (MV-315; see <c>CameraFramingTests.TheSceneAndTheCodeAgreeOnTheZoom</c>).
    ///
    /// Asserts the resolved pose against a SECOND, independently-built rig (CLAUDE.md's testing
    /// policy, Tier 2 — a resolved value, not an authored constant) — not the preset's own helper
    /// re-read back at itself — so it would still fail if <c>ResolveProductionCameraPose</c> silently
    /// returned a stale or zeroed pose.
    ///
    /// Fails on base commit 8832b57: <c>CaptureDirector.ResolveProductionCameraPose</c> doesn't
    /// exist and <c>CapturePresets.All</c> carries no "mv1266reefplaycam" key — a compile failure,
    /// the same "doesn't exist yet" shape <c>MV1263ReefCaptureUsesMobileQualityTierTests</c> already
    /// used for this same class.
    /// </summary>
    public sealed class MV1266ReefPlayCameraTests
    {
        [Test]
        public void Mv1266ReefPlayCameraCheck_LeavesTheBrainInControlAndReportsTheLiveRigPose()
        {
            CapturePreset preset = CapturePresets.All["mv1266reefplaycam"];
            Assert.IsFalse(preset.DisableBrain,
                "the new preset must leave CinemachineBrain enabled -- that is the whole point: the " +
                "live production camera, never a manual zone-framing pose");

            (float distance, float pitch) = CaptureDirector.ResolveProductionCameraPose();

            var go = new GameObject("Mv1266ExpectedRig");
            try
            {
                var rig = go.AddComponent<FixedAngleCameraRig>();
                rig.ApplyDeviceDefault();
                Assert.AreEqual(rig.Distance, distance, 1e-3,
                    "the preset's reported camera distance drifted from a freshly-built production rig");
                Assert.AreEqual(rig.Pitch, pitch, 1e-3,
                    "the preset's reported camera pitch drifted from a freshly-built production rig");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
