using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The neighbourhood, actually built (YT-76).
    ///
    /// The plan is unit-tested on its own. What only exists once it's real objects in a real scene is
    /// the guarantee that matters: a yard that now has houses, hedges and trees in it still has
    /// nothing out there to walk into, shoot, or hide behind.
    /// </summary>
    public sealed class BackyardBackdropPlayTests
    {
        private GameObject _path;
        private GameObject _backdrop;

        [TearDown]
        public void TearDown()
        {
            if (_backdrop != null) Object.Destroy(_backdrop);
            if (_path != null) Object.Destroy(_path);
        }

        private IEnumerator BuildYard()
        {
            _path = new GameObject("Backyard Path");
            _path.AddComponent<BackyardPath>();
            yield return null;

            _backdrop = new GameObject("BackyardBackdrop");
            _backdrop.AddComponent<BackyardBackdrop>();
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheNeighbourhood_IsActuallyThere()
        {
            yield return BuildYard();

            var backdrop = _backdrop.GetComponent<BackyardBackdrop>();
            Assert.Greater(backdrop.PieceCount, 20,
                "nothing was built beyond the fence — the yard is a diorama again");
        }

        [UnityTest]
        public IEnumerator NothingInTheNeighbourhood_CanBeTouched()
        {
            yield return BuildYard();

            var colliders = _backdrop.GetComponentsInChildren<Collider>();

            Assert.IsEmpty(colliders.Select(c => c.name),
                "scenery with a collider is not scenery — it's an obstacle the player can't shoot");
        }

        [UnityTest]
        public IEnumerator NothingInTheNeighbourhood_CastsAShadowIntoTheYard()
        {
            yield return BuildYard();

            // The shadow map is a fixed budget over a fixed distance. A neighbour's roof twenty
            // metres away eating texels so it can throw a shadow the player will never see is a bad
            // trade — the yard's own fence needs them.
            foreach (var r in _backdrop.GetComponentsInChildren<MeshRenderer>())
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode,
                    $"{r.name} out in the backdrop is casting shadows");
        }

        [UnityTest]
        public IEnumerator EveryPiece_WearsAMaterialTheBuildCanDraw()
        {
            yield return BuildYard();
            yield return null;

            foreach (var r in _backdrop.GetComponentsInChildren<MeshRenderer>())
            foreach (var m in r.sharedMaterials)
            {
                Assert.IsNotNull(m, $"{r.name} has an empty material slot");
                Assert.IsTrue(m.shader.isSupported, $"{r.name} wears an unsupported shader");
                Assert.That(m.name, Does.Not.StartWith("Default-Material"),
                    $"{r.name} kept Unity's built-in default — magenta in a player build");
            }
        }
    }
}
