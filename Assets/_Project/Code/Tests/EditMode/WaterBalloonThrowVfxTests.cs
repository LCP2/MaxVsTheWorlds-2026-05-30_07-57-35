using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The Water Balloon's thrown body (MV-334): "throwing the Water Balloon shows a visible throw
    /// VFX/animation" — before this, a throw was invisible for the whole flight time and only the
    /// splash on landing gave any feedback.
    /// </summary>
    public sealed class WaterBalloonThrowVfxTests
    {
        [Test]
        public void StartsAtTheOrigin()
        {
            var origin = new Vector3(1f, 0f, 2f);
            var landing = origin + new Vector3(0f, 0f, 6f);
            var vfx = WaterBalloonThrowVfx.Fire(origin, landing, 1f);
            try
            {
                Assert.That(Vector3.Distance(vfx.transform.position, origin), Is.LessThan(0.01f),
                    "the thrown balloon doesn't start at the throw origin");
            }
            finally { Object.DestroyImmediate(vfx.gameObject); }
        }

        [Test]
        public void EndsExactlyAtTheLandingPoint()
        {
            var origin = Vector3.zero;
            var landing = new Vector3(3f, 0f, 4f);
            var vfx = WaterBalloonThrowVfx.Fire(origin, landing, 1f);
            try
            {
                vfx.ApplyProgress(1f);
                Assert.That(Vector3.Distance(vfx.transform.position, landing), Is.LessThan(0.01f),
                    "the thrown balloon doesn't land where the ability said it would");
            }
            finally { Object.DestroyImmediate(vfx.gameObject); }
        }

        [Test]
        public void RisesInTheAirPartway_ItIsALobNotASlide()
        {
            var vfx = WaterBalloonThrowVfx.Fire(Vector3.zero, new Vector3(0f, 0f, 8f), 1f);
            try
            {
                vfx.ApplyProgress(0.5f);
                Assert.Greater(vfx.transform.position.y, 0.5f,
                    "the balloon never leaves the ground mid-flight — it reads as a slide, not a throw");
            }
            finally { Object.DestroyImmediate(vfx.gameObject); }
        }

        [Test]
        public void FollowsTheExactSameArcAsTheAimPreview()
        {
            // The preview arc (WV-241) is what the player aimed with. If the thrown body traced a
            // different curve, the balloon would visibly land somewhere other than where it was aimed.
            const float distance = 6f;
            var vfx = WaterBalloonThrowVfx.Fire(Vector3.zero, new Vector3(0f, 0f, distance), 1f);
            try
            {
                foreach (float t in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                {
                    Vector3 expected = WaterBalloonAimMesh.LocalPositionOnArc(distance, t);
                    Vector3 actual = vfx.PositionAt(t);
                    Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.01f),
                        $"at t={t} the thrown body diverges from the previewed arc");
                }
            }
            finally { Object.DestroyImmediate(vfx.gameObject); }
        }

        [Test]
        public void TheBodyHasAMaterialAssigned()
        {
            var vfx = WaterBalloonThrowVfx.Fire(Vector3.zero, new Vector3(0f, 0f, 5f), 1f);
            try
            {
                var renderer = vfx.GetComponentInChildren<MeshRenderer>();
                Assert.IsNotNull(renderer, "the thrown balloon has no visible body");
                Assert.IsNotNull(renderer.sharedMaterial,
                    "the thrown balloon's body has no material — it would draw nothing in a build");
            }
            finally { Object.DestroyImmediate(vfx.gameObject); }
        }

        [Test]
        public void ADegenerateThrowDoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                var vfx = WaterBalloonThrowVfx.Fire(Vector3.zero, Vector3.zero, 0f);
                Object.DestroyImmediate(vfx.gameObject);
            });
        }
    }
}
