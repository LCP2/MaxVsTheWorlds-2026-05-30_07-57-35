using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Dev;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1277 (the one new test, per CC_AUTONOMY's testing policy): <c>ShipKit</c> creates zero
    /// colliders (its own class doc: "nothing here carries a collider ... colliders come from the
    /// map's own cover entries"), so <c>CaptureDirector.IsUnoccludedSample</c>'s <c>Physics.Raycast</c>
    /// is structurally blind to a ShipKit dressing/VFX piece sitting between the camera and a sample
    /// point -- confirmed on main @ 871e3b7b: zero <c>AddComponent&lt;.*Collider&gt;</c>/<c>new BoxCollider</c>
    /// hits across ShipKit.cs's 1676 lines. <c>CaptureDirector.FindColliderFreeOccluder</c> is the
    /// renderer-side counterpart that closes that gap, reading <see cref="Renderer.bounds"/> (MV-465
    /// Tier 2 -- engine-resolved per the scene's current transforms, not an authored constant) instead
    /// of a physics collider.
    ///
    /// Fails on base commit (this branch, <c>FindColliderFreeOccluder</c> present but stubbed to always
    /// return null -- a real base where the test compiles and the assertion itself fails, not a bare
    /// CS0117, per the ticket's own AC): a sample point known, by construction, to sit behind a
    /// collider-free box must still report no occluder, since the stub never tests any candidate's own
    /// bounds.
    /// </summary>
    public sealed class MV1277ColliderFreeOccluderTests
    {
        private GameObject _cam, _occluder;

        [TearDown]
        public void TearDown()
        {
            if (_cam != null) Object.DestroyImmediate(_cam);
            if (_occluder != null) Object.DestroyImmediate(_occluder);
        }

        [Test]
        public void ColliderFreeBox_OccludesASamplePointBehindIt_ButNotAClearBearing()
        {
            _cam = new GameObject("MV1277 Test Cam");
            Camera cam = _cam.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 5f, -10f);

            // A collider-free box -- the same shape ShipKit.EmissiveBox/MeshBox build (a MeshFilter +
            // MeshRenderer, deliberately no Collider at all), planted on the camera-to-target ray's own
            // midpoint, sized well past the ray's width there -- a sample point known, by construction,
            // to be occluded, not a glancing edge case.
            _occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(_occluder.GetComponent<Collider>());
            _occluder.name = "MV1277 Test Emissive Box";
            _occluder.transform.position = new Vector3(0f, 2.5f, -5f);
            _occluder.transform.localScale = new Vector3(2f, 2f, 2f);

            var candidates = new List<Renderer> { _occluder.GetComponent<Renderer>() };

            // Direction 1: a sample point behind the box, from the camera's own point of view, must
            // report the box by name -- not silently measured as if it were whatever is really there.
            Vector3 behindOccluder = new Vector3(0f, 2.5f, 0f);
            Renderer hit = CaptureDirector.FindColliderFreeOccluder(cam, behindOccluder, candidates);
            Assert.That(hit, Is.Not.Null,
                "a sample point behind a known collider-free box must report that box as the occluder, " +
                "not silently report a clear bearing");
            Assert.That(hit.name, Is.EqualTo("MV1277 Test Emissive Box"),
                $"expected the occluding renderer to be the planted box, got '{hit?.name}'");

            // Direction 2 (the one-sided-assertion guard): a sample point with a clear bearing from the
            // same camera, same candidate list, must report no occluder -- a classifier that flags
            // every candidate regardless of worldPos would pass direction 1 for the wrong reason.
            Vector3 clearBearing = new Vector3(5f, 2.5f, 0f);
            Renderer clear = CaptureDirector.FindColliderFreeOccluder(cam, clearBearing, candidates);
            Assert.That(clear, Is.Null,
                "a sample point with a clear bearing from the camera must report no occluder, not flag " +
                "the box anyway");
        }
    }
}
