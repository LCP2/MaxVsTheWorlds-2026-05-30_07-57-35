using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// MV-273: an area's garrison must already be standing before Max can see it — never seen popping
    /// into existence. <see cref="AreaAccumulationDirector.SpawnPointInArea"/> used to fall back to
    /// whatever candidate its last placement attempt tried, even an on-screen one, the instant a room
    /// too crowded with cover/other robots exhausted its placement budget looking for a spot that was
    /// BOTH clear of overlap AND outside the camera's view. This locks in the fix: never being seen
    /// matters more than a clean gap from cover, so a room where no point is ever free of overlap must
    /// still place every robot off-screen rather than falling back to whichever random spot it last
    /// tried.
    /// </summary>
    public sealed class AreaAccumulationPopInPlayTests
    {
        private GameObject _cameraGo;
        private GameObject _directorGo;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();

            // A small, predictable garrison (Area 1's authored seed, no per-area growth) so area2
            // fills in one instant pass, comfortably under the cap below.
            DevTuning.StartLargeCount = 4f;
            DevTuning.StartSmallCount = 6f;
            DevTuning.AreaGrowthPct = 0f;
            DevTuning.MaxActiveRobots = 20f;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (RobotEnemy r in RobotEnemy.Active.ToList())
                if (r != null) Object.Destroy(r.gameObject);

            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.Destroy(bodies);
            if (_directorGo != null) Object.Destroy(_directorGo);
            if (_cameraGo != null) Object.Destroy(_cameraGo);

            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
        }

        /// <summary>An orthographic camera looking straight down at <paramref name="groundLookAt"/> —
        /// the same fixed top-down angle the real gameplay camera uses, just orthographic so the
        /// visible ground footprint is an exact, known rectangle instead of one this test would have
        /// to derive from a perspective frustum.</summary>
        private void NewTopDownCamera(Vector3 groundLookAt, float halfExtent)
        {
            _cameraGo = new GameObject("Eye", typeof(Camera));
            _cameraGo.tag = "MainCamera";
            var cam = _cameraGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = halfExtent;
            _cameraGo.transform.position = groundLookAt + Vector3.up * 50f;
            _cameraGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        [UnityTest]
        public IEnumerator ACrowdedArea_StillPlacesEveryRobotOffScreen()
        {
            // A long "area2" corridor. Cover spans the WHOLE zone, so no candidate anywhere is ever
            // free of overlap — the only way to place a robot at all is the off-screen-only fallback
            // this ticket adds. The camera sees only the near end (ground z 0-30 of a 0-100 deep
            // zone), so a robot ending up off-screen has to have actually been steered into the far
            // majority of the room, not just gotten lucky.
            var zone2 = new MapZone { id = "area2", type = "open", x = 0f, z = 50f, width = 20f, depth = 100f };
            var map = new MapData
            {
                name = "Pop-in regression",
                zones = new[]
                {
                    new MapZone { id = "area1", type = "entry", x = 0f, z = -20f, width = 20f, depth = 20f },
                    zone2,
                },
            };
            var cover = new[]
            {
                new CoverPiece(new ArenaCover("Blanket", zone2.CenterXz,
                    new Vector3(zone2.width, 1f, zone2.depth), CoverShape.Box), null),
            };

            NewTopDownCamera(new Vector3(0f, 0f, 15f), 15f);

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.Configure(map, cover);
            director.EnterArea(2);
            yield return null;

            Assert.AreEqual(10, RobotEnemy.ActiveCount,
                "test setup did not fill area2's full 10-robot garrison in one instant pass");

            Camera cam = Camera.main;
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
            foreach (RobotEnemy robot in RobotEnemy.Active)
            {
                bool onScreen = GeometryUtility.TestPlanesAABB(
                    planes, new Bounds(robot.transform.position, Vector3.one));
                Assert.IsFalse(onScreen,
                    $"{robot.name} at {robot.transform.position} was placed inside the camera's view — " +
                    "a robot must never be seen popping into existence, even in a room too crowded to " +
                    "also give it a clean gap from cover (MV-273).");
            }
        }
    }
}
