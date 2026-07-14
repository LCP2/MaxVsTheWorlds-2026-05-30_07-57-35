using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The Mower Hutch's VulnerableCore is the "shoot here" tell (YT-38 QA: Lee could not tell the
    /// factory from the scenery, so he never destroyed it and never saw the gate open). It only works
    /// if it actually glows on a BUILD.
    ///
    /// It didn't. The skin director claims every renderer under a damageable body, which swept up the
    /// core, and the character path then wrote the structure's grey into the very MaterialPropertyBlock
    /// that MowerHutch.PulseCore writes cyan into — two LateUpdates, one block, no execution order
    /// between them. Script order picked the winner, so the core glowed in the editor and rendered as a
    /// dead grey panel in the deployed WebGL build.
    ///
    /// These tests run the real director against the real factory, because that fight only exists when
    /// both are present — a test of either one alone would have passed the whole time.
    /// </summary>
    public sealed class FactoryCorePlayTests
    {
        private GameObject _hutchGo, _directorGo;

        private static Renderer CoreOf(GameObject hutch)
        {
            var core = hutch.transform.Find("VulnerableCore");
            Assert.That(core, Is.Not.Null, "the factory should build a VulnerableCore child");
            return core.GetComponent<Renderer>();
        }

        /// <summary>Factory + skin director, live together, exactly as the scene has them.</summary>
        private IEnumerator BuildFactoryAndDirector()
        {
            _hutchGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _hutchGo.name = "Mower Hutch";
            _hutchGo.AddComponent<MowerHutch>();

            _directorGo = new GameObject("SkinDirector", typeof(CharacterSkinDirector));

            // Two frames: one for the director to claim renderers, one for the LateUpdates to fight.
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in new[] { _hutchGo, _directorGo })
                if (go != null) Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SkinDirector_DoesNotClaim_TheVulnerableCore()
        {
            yield return BuildFactoryAndDirector();

            var core = CoreOf(_hutchGo);
            Assert.That(core.GetComponent<CharacterSkin>(), Is.Null,
                "the core drives its own property block — skinning it puts a second writer on the " +
                "same renderer and script order decides whether the tell glows");
        }

        /// <summary>
        /// Telling the skin director to keep off the core also took away the only thing that had been
        /// handing it a material that ships: a primitive's default material is not in the build's
        /// shader set, so the first cut of the fix rendered the core MAGENTA on the deploy. Colour
        /// assertions can't see that — they passed while the build was magenta — so this asserts the
        /// core wears a MaterialLibrary shader, which is what being on the always-included list means.
        /// </summary>
        [UnityTest]
        public IEnumerator VulnerableCore_WearsAShaderThatShips_NotThePrimitiveDefault()
        {
            yield return BuildFactoryAndDirector();

            var core = CoreOf(_hutchGo);
            var library = MaterialLibrary.Character();
            if (library == null) Assert.Ignore("no character shader in this project — nothing to compare against");

            Assert.That(core.sharedMaterial, Is.Not.Null, "the core must have a material");
            Assert.That(core.sharedMaterial.shader, Is.EqualTo(library.shader),
                "the core must wear the library's shader — a primitive's default is not in the " +
                "build's shader set and renders as magenta on the deploy");
        }

        /// <summary>
        /// The assertion is a RELATIONSHIP, not a colour value: the core must read as a cool, glowing
        /// tell rather than the structure's neutral body. Pinning the literal cyan would just break the
        /// next time the art stream retunes the palette, which is not what this test is defending.
        /// </summary>
        [UnityTest]
        public IEnumerator VulnerableCore_StaysCoolAndGlowing_WithTheSkinDirectorRunning()
        {
            yield return BuildFactoryAndDirector();

            var core = CoreOf(_hutchGo);
            var mpb = new MaterialPropertyBlock();
            core.GetPropertyBlock(mpb);

            Color body = mpb.GetColor("_BaseColor");
            Color emission = mpb.GetColor("_EmissionColor");

            Assert.That(body.b, Is.GreaterThan(body.r + 0.2f),
                $"the core should read cool/cyan, not the structure's grey — got {body}");
            Assert.That(emission.maxColorComponent, Is.GreaterThan(0.5f),
                $"the core should be emissive so the eye is pulled to it — got {emission}");
        }
    }
}
