using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The aim reticle (YT-84) shipped, passed its tests, and never drew a single pixel.
    ///
    /// It is unparented on purpose, to keep CharacterSkinDirector from repainting it — and that is
    /// exactly what hands it to <see cref="RuntimeSurfaceDirector"/>, which sweeps every LOOSE
    /// renderer each frame and dresses it as scenery. It claimed the reticle and replaced its
    /// transparent particle material with the OPAQUE ground material, so the reach cone was drawn as
    /// a wedge of grass lying 6 mm above the grass. Invisible, alpha meaningless, no error anywhere.
    ///
    /// Every existing reticle test passed throughout, because they all check the mesh and the alpha
    /// — neither of which the bug touches. So this one runs the REAL director against the REAL
    /// reticle and asserts the only thing that actually mattered: that it can still be seen through.
    /// </summary>
    public sealed class AimReticleSurvivesDirectorsPlayTests
    {
        private GameObject _ownerGo, _directorGo;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in new[] { _ownerGo, _directorGo })
                if (go != null) Object.Destroy(go);
            yield return null;
        }

        private IEnumerator BuildReticleAndDirector()
        {
            _ownerGo = new GameObject("Max");
            var reticle = _ownerGo.AddComponent<AimReticle>();
            reticle.Init(_ownerGo.transform, range: 6f, coneHalfAngle: 35f);

            _directorGo = new GameObject("RuntimeSurfaces", typeof(RuntimeSurfaceDirector));

            // Let the director sweep, and let the reticle's own LateUpdate run after it.
            yield return null;
            yield return null;
        }

        private static Renderer ReticleRenderer()
        {
            var go = GameObject.Find("AimReticle");
            Assert.That(go, Is.Not.Null, "the reticle should build its quad");
            return go.GetComponent<Renderer>();
        }

        [UnityTest]
        public IEnumerator Reticle_KeepsATransparentMaterial_WhenTheSurfaceDirectorSweeps()
        {
            yield return BuildReticleAndDirector();

            var mat = ReticleRenderer().sharedMaterial;
            Assert.That(mat, Is.Not.Null, "the reticle must keep a material");

            // The reach cone is a translucent mark on the lawn. Anything drawn in the opaque geometry
            // queue is, by definition, not that — and an opaque wedge of ground over ground is what
            // "invisible" looked like.
            Assert.That(mat.renderQueue, Is.GreaterThanOrEqualTo(3000),
                $"the reticle must stay in the transparent queue — got '{mat.name}' " +
                $"({mat.shader.name}) at queue {mat.renderQueue}. The surface director has " +
                $"re-dressed it as scenery.");
        }

        [UnityTest]
        public IEnumerator Reticle_IsNotWearingTheGroundMaterial()
        {
            yield return BuildReticleAndDirector();

            var mat = ReticleRenderer().sharedMaterial;
            Assert.That(mat.shader.name, Does.Not.Contain("Ground"),
                $"the reticle is wearing the world's ground shader ('{mat.shader.name}') — it is " +
                "being drawn as a patch of lawn on top of the lawn, which is why nobody ever saw it");
        }
    }
}
