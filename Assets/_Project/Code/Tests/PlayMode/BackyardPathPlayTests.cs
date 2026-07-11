using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// PlayMode checks that the generated Backyard path (YT-38) is actually traversable geometry:
    /// the lane centreline is clear of walls, the lane edges are walled, and the arena is enclosed.
    /// Guards against a coordinate mistake in the blockout that the pure layout test can't catch.
    /// </summary>
    public sealed class BackyardPathPlayTests
    {
        private GameObject _go;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            yield return null;
        }

        private static bool WallAt(Vector3 p)
        {
            // The path pieces are named boxes with colliders; ignore the ground floor (very flat).
            foreach (var c in Physics.OverlapSphere(p, 0.4f))
            {
                if (c.bounds.size.y >= 2f) return true; // a wall-height box
            }
            return false;
        }

        [UnityTest]
        public IEnumerator LaneCentreIsClear_AndEdgesAreWalled()
        {
            _go = new GameObject("Path", typeof(BackyardPath));
            yield return null;                 // Awake builds geometry
            Physics.SyncTransforms();
            yield return null;

            var l = BackyardPathLayout.Default;

            // Centre of the lane must be walkable from just inside the patio to just before the gate.
            for (float z = l.StartZ + 1f; z <= l.GateZ - 1f; z += 3f)
            {
                Assert.IsFalse(WallAt(new Vector3(0f, 1f, z)),
                    $"lane centre blocked by a wall at z={z}");
            }

            // Just outside the lane there must be a wall on both sides (the corridor is enclosed).
            float mid = (l.StartZ + l.GateZ) * 0.5f;
            Assert.IsTrue(WallAt(new Vector3(-(l.LaneHalfWidth + 0.6f), 1f, mid)), "no left corridor wall");
            Assert.IsTrue(WallAt(new Vector3(l.LaneHalfWidth + 0.6f, 1f, mid)), "no right corridor wall");
        }

        [UnityTest]
        public IEnumerator ArenaIsEnclosedBehindTheGate()
        {
            _go = new GameObject("Path", typeof(BackyardPath));
            yield return null;
            Physics.SyncTransforms();
            yield return null;

            var l = BackyardPathLayout.Default;

            // Back wall of the arena.
            Assert.IsTrue(WallAt(new Vector3(0f, 1f, l.ArenaEndZ + 0.5f)), "no arena back wall");
            // Side walls of the arena at its mid-depth.
            float az = (l.GateZ + l.ArenaEndZ) * 0.5f;
            Assert.IsTrue(WallAt(new Vector3(-(l.ArenaHalfWidth + 0.6f), 1f, az)), "no left arena wall");
            Assert.IsTrue(WallAt(new Vector3(l.ArenaHalfWidth + 0.6f, 1f, az)), "no right arena wall");
        }
    }
}
