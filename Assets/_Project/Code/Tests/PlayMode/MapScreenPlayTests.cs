using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The in-run map (YT-72): it has to open, pause, plot Max where he actually is, and — the part
    /// that would ruin a run — hand time back correctly when it closes.
    /// </summary>
    public sealed class MapScreenPlayTests
    {
        private GameObject _canvasGo;
        private GameObject _playerGo;
        private MapScreen _map;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _playerGo = new GameObject("Max");
            _playerGo.tag = "Player";
            _playerGo.transform.position = new Vector3(0f, 1f, -3f);   // the lawn entry (YT-70)

            _canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster));
            _canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var go = new GameObject("Map Screen");
            go.transform.SetParent(_canvasGo.transform, false);
            _map = go.AddComponent<MapScreen>();
            _map.Build((RectTransform)_canvasGo.transform, 1920f, 1080f);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;   // never leak a paused clock into the next test
            if (_canvasGo != null) Object.Destroy(_canvasGo);
            if (_playerGo != null) Object.Destroy(_playerGo);
            yield return null;
        }

        [Test]
        public void TheMapStartsClosed_AndTheGameIsRunning()
        {
            Assert.IsFalse(_map.IsOpen);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator OpeningPausesTheRun_AndClosingResumesIt()
        {
            _map.Open();
            yield return null;
            Assert.IsTrue(_map.IsOpen);
            Assert.AreEqual(0f, Time.timeScale, "the run kept going while you read the map");

            _map.Close();
            yield return null;
            Assert.IsFalse(_map.IsOpen);
            Assert.AreEqual(1f, Time.timeScale, "the game never came back");
        }

        [UnityTest]
        public IEnumerator ToggleOpensThenCloses()
        {
            _map.Toggle();
            yield return null;
            Assert.IsTrue(_map.IsOpen);

            _map.Toggle();
            yield return null;
            Assert.IsFalse(_map.IsOpen);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator ClosingAnAlreadyClosedMap_DoesNotUnpauseSomeoneElsesPause()
        {
            // e.g. the result card has frozen the game. A stray Close() must not resume the run.
            Time.timeScale = 0f;
            _map.Close();
            yield return null;
            Assert.AreEqual(0f, Time.timeScale, "the map resumed a pause it did not own");
        }

        [UnityTest]
        public IEnumerator TheDotIsWhereMaxIs()
        {
            _playerGo.transform.position = new Vector3(0f, 1f, -3f);
            _map.Open();
            yield return null;

            Vector2 atStart = _map.Map.PlayerNormalized;
            Assert.AreEqual(1, _map.Map.PlayerRoom, "the map says Max isn't in the lawn — he is");

            // Walk him up to the boss arena; the dot must climb the map.
            _playerGo.transform.position = new Vector3(0f, 1f, 33f);
            yield return null;
            _map.Map.Refresh();

            Vector2 atBoss = _map.Map.PlayerNormalized;
            Assert.Greater(atBoss.y, atStart.y, "Max advanced up-field but the dot didn't move up");
            Assert.AreEqual(2, _map.Map.PlayerRoom, "the map can't tell you're in the boss arena");
        }

        [UnityTest]
        public IEnumerator TheDotStaysOnTheMap_AtBothEndsOfThePath()
        {
            _map.Open();
            foreach (float z in new[] { -14f, 0f, 22f, 44f })
            {
                _playerGo.transform.position = new Vector3(0f, 1f, z);
                yield return null;
                _map.Map.Refresh();

                Vector2 n = _map.Map.PlayerNormalized;
                Assert.GreaterOrEqual(n.y, 0f, $"the dot fell off the bottom of the map at z={z}");
                Assert.LessOrEqual(n.y, 1f, $"the dot fell off the top of the map at z={z}");
                Assert.GreaterOrEqual(n.x, 0f); Assert.LessOrEqual(n.x, 1f);
            }
        }
    }
}
