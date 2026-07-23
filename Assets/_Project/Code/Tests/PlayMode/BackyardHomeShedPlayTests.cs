using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Max's home shed (YT-163), actually built.
    ///
    /// Placement is unit-tested on its own (<c>BackyardHomeShedTests</c>). What only exists once it's
    /// real objects in a real scene is the guarantee that matters: the shed is visible, it costs
    /// nothing, and it is not something the player can touch, hide behind, or get blocked by.
    /// </summary>
    public sealed class BackyardHomeShedPlayTests
    {
        private GameObject _path;
        private GameObject _shed;

        [TearDown]
        public void TearDown()
        {
            if (_shed != null) Object.Destroy(_shed);
            if (_path != null) Object.Destroy(_path);
        }

        private IEnumerator BuildYard()
        {
            _path = new GameObject("Backyard Path");
            _path.AddComponent<BackyardPath>();
            yield return null;

            _shed = new GameObject("BackyardHomeShed");
            _shed.AddComponent<BackyardHomeShed>();
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheShed_IsActuallyThere()
        {
            yield return BuildYard();

            var shed = _shed.GetComponent<BackyardHomeShed>();
            Assert.IsTrue(shed.Built, "the shed did not build — nothing for Max to have walked out of");
            Assert.Greater(shed.GetComponentsInChildren<MeshRenderer>().Length, 0,
                "the shed has no geometry");
        }

        [UnityTest]
        public IEnumerator TheShed_StandsOutsideEveryRoom()
        {
            yield return BuildYard();

            MapData map = MapLibrary.Load(MapLibrary.BackyardSlice);

            foreach (var r in _shed.GetComponentsInChildren<MeshRenderer>())
            {
                Bounds b = r.bounds;
                MapZone standing = map.ZoneAt(b.center.x, b.center.z);

                Assert.IsNull(standing,
                    $"{r.name} is standing in '{standing?.id}' — the shed has become a room");
            }
        }

        [UnityTest]
        public IEnumerator TheShed_CannotBeTouched()
        {
            yield return BuildYard();

            var colliders = _shed.GetComponentsInChildren<Collider>();

            Assert.IsEmpty(colliders,
                "the shed has a collider — it's meant to be a backdrop, not an obstacle");
        }

        [UnityTest]
        public IEnumerator EveryPiece_WearsAMaterialTheBuildCanDraw()
        {
            yield return BuildYard();
            yield return null;

            foreach (var r in _shed.GetComponentsInChildren<MeshRenderer>())
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
