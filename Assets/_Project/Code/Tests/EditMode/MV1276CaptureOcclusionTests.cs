using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Dev;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1276 (the one new test, per CC_AUTONOMY's testing policy): <c>CaptureDirector.BuildMv1274HangarCheck</c>'s
    /// own <c>Measure()</c> used <c>cam.WorldToViewportPoint</c> plus a raw framebuffer pixel index with
    /// no occlusion test at all -- zero <c>Physics.Raycast</c> calls existed anywhere in
    /// <c>CaptureDirector.cs</c>. Every sample reported whatever was DRAWN at that screen pixel, never
    /// confirmed against what was actually AT that world point, which is why two of MV-1274's AC4
    /// falloff samples sat pinned within 5 luma of the frame's own brightest pixel across three
    /// relighting rounds -- the probe was reading the Hangar's wall-top cap neon drawn over the floor,
    /// never the floor itself (triage, MV-1274 comment, 22 Oct).
    ///
    /// Fails on base commit 741b517 (<c>feat/MV-1274-hangar-relight-slice</c>, the tip MV-1276 was filed
    /// against): <c>CaptureDirector.IsUnoccludedSample</c> doesn't exist yet -- a compile failure, the
    /// same "doesn't exist yet" shape <c>MV1032CaptureNeverResumesLiveRunTests</c> already used for this
    /// same class.
    /// </summary>
    public sealed class MV1276CaptureOcclusionTests
    {
        private GameObject _cam, _floor, _occluder;

        [TearDown]
        public void TearDown()
        {
            if (_cam != null) Object.DestroyImmediate(_cam);
            if (_floor != null) Object.DestroyImmediate(_floor);
            if (_occluder != null) Object.DestroyImmediate(_occluder);
        }

        [Test]
        public void SamplePointBehindAnOccluder_IsRejected_NotSilentlyMeasured()
        {
            _cam = new GameObject("MV1276 Test Cam");
            Camera cam = _cam.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 5f, -10f);

            // A floor collider marked the same way MapRuntime.Build tags the real map floor slab.
            _floor = new GameObject("MV1276 Test Floor");
            _floor.transform.position = new Vector3(0f, -1f, 0f);   // top surface at y = 0
            _floor.AddComponent<BoxCollider>().size = new Vector3(10f, 2f, 10f);
            _floor.AddComponent<StructuralFloor>();

            Vector3 target = Vector3.zero;   // sits exactly on the floor's own top surface

            // An occluder planted on the camera-to-target ray's own midpoint, sized well past the
            // ray's width there -- a sample point known, by construction, to be occluded, not a
            // glancing edge case.
            _occluder = new GameObject("MV1276 Test Occluder");
            _occluder.transform.position = new Vector3(0f, 2.5f, -5f);
            _occluder.AddComponent<BoxCollider>().size = new Vector3(2f, 2f, 2f);

            Physics.SyncTransforms();

            bool unoccluded = CaptureDirector.IsUnoccludedSample(cam, target,
                c => c.GetComponentInParent<StructuralFloor>() != null, out string reason);

            Assert.That(unoccluded, Is.False,
                $"a sample point known to be occluded at the camera must be rejected by the validation, " +
                $"not silently measured as if it were the floor -- got reason='{reason}'");
            Assert.That(reason, Does.StartWith("occluded-by:"),
                $"the rejection reason should name what occluded the sample, not report 'no-collider-hit' " +
                $"or 'unexpected-hit' -- got '{reason}'");
        }
    }
}
