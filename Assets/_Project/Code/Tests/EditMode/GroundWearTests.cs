using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Factories;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The wear the yard carries (YT-79): the ruts the mowers have worn driving out of the Hutch, the
    /// dead apron they turn on, and the oil under the machine.
    ///
    /// Painted into the ground shader rather than projected as URP decals. That is the whole reason it
    /// is affordable: a decal is a render pass and a projector, and this is a handful of ALU on a
    /// surface that was already being shaded, on the mobile tier WebGL actually ships. It also cannot
    /// pop, cannot sort wrong, and cannot z-fight with the grass it is supposed to be worn INTO.
    /// </summary>
    public sealed class GroundWearTests
    {
        private GameObject _hutch;
        private GameObject _wear;

        [TearDown]
        public void TearDown()
        {
            if (_wear != null) Object.DestroyImmediate(_wear);
            if (_hutch != null) Object.DestroyImmediate(_hutch);
            BackyardWear.Clear();
            MaterialLibrary.Palette = BiomePalette.Backyard;
            MaterialLibrary.Clear();
        }

        private BackyardWear NewWear()
        {
            _wear = new GameObject("BackyardWear");
            return _wear.AddComponent<BackyardWear>();
        }

        [Test]
        public void TheGroundShader_KnowsHowToBeWorn()
        {
            var ground = MaterialLibrary.Surface(SurfaceKind.Ground);

            Assert.IsTrue(ground.HasProperty("_WearColor"), "no worn-earth colour");
            Assert.IsTrue(ground.HasProperty("_TrackGauge"), "no rut spacing — the mowers have no wheels");
            Assert.IsTrue(ground.HasProperty("_ApronRadius"), "no turning apron");
            Assert.IsTrue(ground.HasProperty("_OilColor"), "no oil");
        }

        /// <summary>
        /// The tracks are the tracks OF the factory — so they follow it.
        ///
        /// The Hutch's position is gameplay's: YT-38 put it where the fight wanted it, YT-70 moved Max
        /// away from it. Baking the ruts in as a decal somebody once placed on a lawn would mean the
        /// day gameplay moves the factory, the yard keeps the tyre marks of a machine that is no longer
        /// there.
        /// </summary>
        [Test]
        public void TheWear_FollowsTheFactory()
        {
            _hutch = new GameObject("Mower Hutch");
            _hutch.transform.position = new Vector3(4f, 1f, 21f);
            _hutch.AddComponent<MowerHutch>();

            Assert.IsTrue(NewWear().Apply(), "the lawn was never told where the machine is.");

            Vector4 wear = Shader.GetGlobalVector(BackyardWear.GlobalName);
            Assert.That(wear.x, Is.EqualTo(4f).Within(1e-3f), "the ruts are in the wrong place.");
            Assert.That(wear.y, Is.EqualTo(21f).Within(1e-3f), "the ruts are in the wrong place.");
            Assert.Greater(wear.z, 0.5f, "the wear is switched off even though there IS a factory.");
        }

        /// <summary>
        /// No factory, no tracks — and this one has teeth.
        ///
        /// A shader global outlives the scene that set it. Without the reset, any fixture that builds a
        /// bare arena AFTER the Backyard has run inherits the Backyard's tyre marks, running out of a
        /// machine that isn't there, through the origin of a yard that never had one.
        /// </summary>
        [Test]
        public void APristineYard_GrowsNoTyreTracks()
        {
            Shader.SetGlobalVector(BackyardWear.GlobalName, new Vector4(0f, 15f, 1f, 0f));   // a stale scene

            Assert.IsFalse(NewWear().Apply(), "it found a factory in a yard that hasn't got one.");

            Assert.LessOrEqual(Shader.GetGlobalVector(BackyardWear.GlobalName).z, 0.5f,
                "the lawn is still wearing the last scene's tyre tracks.");
        }
    }
}
