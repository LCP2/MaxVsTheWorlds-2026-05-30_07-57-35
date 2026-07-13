using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The marks the yard keeps (YT-79).
    /// </summary>
    public sealed class GroundWearPlayTests
    {
        private GameObject _ambience;
        private GameObject _director;

        [SetUp]
        public void SetUp()
        {
            _ambience = new GameObject("AmbienceVFX");
            _ambience.AddComponent<AmbienceVfx>();

            // The sweep that dresses anything undressed. It has to be here, because it is the thing
            // that was eating the scorch marks.
            _director = new GameObject("RuntimeSurfaces");
            _director.AddComponent<RuntimeSurfaceDirector>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_ambience != null) Object.Destroy(_ambience);
            if (_director != null) Object.Destroy(_director);
        }

        /// <summary>
        /// A scorch mark has to still be a scorch mark one frame later.
        ///
        /// RuntimeSurfaceDirector sweeps every frame and re-materials anything it doesn't recognise,
        /// and it did not recognise a decal: no CharacterSkin, no IDamageable, no GroundRing, no
        /// KeepsOwnMaterial. So every mark the ambience layer burned into the lawn was picked up on
        /// the very next frame, classified BY SHAPE — a flat quad, therefore ground — and repainted as
        /// a lit patch of grass. A transparent black splat became an opaque green tile.
        ///
        /// The sweep is right to be greedy; that greed is what stopped the boss's damage zones
        /// rendering magenta (YT-61). The decals just have to say they brought their own material.
        /// </summary>
        [UnityTest]
        public IEnumerator AScorchMark_SurvivesTheSurfaceSweep()
        {
            yield return null;   // let both self-installers wake

            HudSignals.EmitEnemyKilled(new Vector3(3f, 0f, 12f));
            yield return null;

            var decal = _ambience.GetComponentsInChildren<MeshRenderer>()
                                 .FirstOrDefault(r => r.name == "Decal");
            Assert.IsNotNull(decal, "no scorch mark was left where the robot died.");

            // Let the sweep run at least once more, which is where it used to eat them.
            yield return null;
            yield return null;

            Assert.IsNull(decal.GetComponent<SurfaceSkinned>(),
                "the surface sweep claimed the scorch mark — it has been re-materialed as a piece of " +
                "world surface. A burn on the lawn is not a lawn.");

            string shader = decal.sharedMaterial.shader.name;
            Assert.That(shader, Does.Not.Contain("StylizedSurface").And.Not.Contain("StylizedGround"),
                $"the scorch mark is wearing '{shader}' — a lit, opaque world material. It was a " +
                "transparent splat; it is now a tile of grass sitting on the grass.");
        }
    }
}
