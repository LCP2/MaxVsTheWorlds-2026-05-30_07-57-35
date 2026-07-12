using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// PlayMode checks that the generated Backyard blockout (YT-38, reshaped by YT-68) is actually
    /// the space the design asks for: a walled patio that opens into a wide, cover-strewn lawn you
    /// can circle in, a gate doorway, and an enclosed boss arena. Guards against a coordinate
    /// mistake in the blockout that the pure layout test can't catch — that a room is walled where
    /// it should be open, or open where it should be walled.
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

        private IEnumerator BuildPath()
        {
            _go = new GameObject("Path", typeof(BackyardPath));
            yield return null;                 // Awake builds geometry
            Physics.SyncTransforms();
            yield return null;
        }

        /// <summary>Anything solid and person-height standing at this point.</summary>
        private static bool BlockedAt(Vector3 p) =>
            Physics.OverlapSphere(p, 0.4f).Any(c => c.bounds.size.y >= 1.5f);

        [Test]
        public void CoverIsAuthoredOffTheCentreLine()
        {
            // Cheap guard on the data itself, so a failure below is unambiguously a geometry bug.
            var l = BackyardPathLayout.Default;
            Assert.IsTrue(BackyardCover.Validate(l, BackyardCover.Default, 15f, 3.5f, out string why), why);
        }

        [UnityTest]
        public IEnumerator TheMissionLineIsWalkableEndToEnd()
        {
            yield return BuildPath();
            var l = BackyardPathLayout.Default;

            // Patio mouth → gate along the centre: the route Max takes must never be blocked.
            for (float z = l.StartZ + 1f; z <= l.GateZ - 1f; z += 2f)
                Assert.IsFalse(BlockedAt(new Vector3(0f, 1f, z)), $"mission line blocked at z={z}");
        }

        [UnityTest]
        public IEnumerator ThePatioIsNarrowAndWalled()
        {
            yield return BuildPath();
            var l = BackyardPathLayout.Default;
            float z = (l.StartZ + l.LawnStartZ) * 0.5f;

            Assert.IsTrue(BlockedAt(new Vector3(-(l.PatioHalfWidth + 0.6f), 1f, z)), "no left patio wall");
            Assert.IsTrue(BlockedAt(new Vector3(l.PatioHalfWidth + 0.6f, 1f, z)), "no right patio wall");
            Assert.IsTrue(BlockedAt(new Vector3(0f, 1f, l.StartZ - 0.6f)), "no patio back wall");
        }

        [UnityTest]
        public IEnumerator TheLawnOpensOutIntoARoomYouCanCircleIn()
        {
            yield return BuildPath();
            var l = BackyardPathLayout.Default;

            // The heart of YT-68. At a cover-free depth just inside the lawn, sweep the full width:
            // where the old corridor had walls at ±4.5, there must now be open floor all the way out.
            float z = l.LawnStartZ + 2f;
            for (float x = -(l.LawnHalfWidth - 0.6f); x <= l.LawnHalfWidth - 0.6f; x += 1f)
                Assert.IsFalse(BlockedAt(new Vector3(x, 1f, z)),
                    $"the lawn is still walled in at x={x} — it's a corridor, not a fight room");

            // …and it IS a room: walls at its edges, not open ground running off to nowhere.
            float mid = (l.LawnStartZ + l.GateZ) * 0.5f;
            Assert.IsTrue(BlockedAt(new Vector3(-(l.LawnHalfWidth + 0.6f), 1f, mid)), "no left lawn wall");
            Assert.IsTrue(BlockedAt(new Vector3(l.LawnHalfWidth + 0.6f, 1f, mid)), "no right lawn wall");
        }

        [UnityTest]
        public IEnumerator TheLawnHasCoverToBreakTheBeeline()
        {
            yield return BuildPath();

            foreach (var c in BackyardCover.Default)
            {
                var probe = new Vector3(c.CenterXz.x, 1f, c.CenterXz.y);
                Assert.IsTrue(Physics.OverlapSphere(probe, 0.3f).Length > 0,
                    $"{c.Name} has no collider — you'd run straight through it");
            }

            var names = _go.GetComponentsInChildren<Transform>().Select(t => t.name).ToArray();
            foreach (var c in BackyardCover.Default)
                Assert.Contains(c.Name, names, "cover piece missing from the blockout");
        }

        [UnityTest]
        public IEnumerator TheGateIsADoorwayInAWall()
        {
            yield return BuildPath();
            var l = BackyardPathLayout.Default;

            // Open in the middle…
            Assert.IsFalse(BlockedAt(new Vector3(0f, 1f, l.GateZ)), "the gate doorway is walled shut");
            // …and shouldered off to the sides, so the boss arena is sealed until the gate opens.
            Assert.IsTrue(BlockedAt(new Vector3(l.GateHalfWidth + 1.5f, 1f, l.GateZ)), "no right gate shoulder");
            Assert.IsTrue(BlockedAt(new Vector3(-(l.GateHalfWidth + 1.5f), 1f, l.GateZ)), "no left gate shoulder");
            // Including the step out to the wider arena — no slipping round the lawn wall's end.
            Assert.IsTrue(BlockedAt(new Vector3(l.LawnHalfWidth + 1.5f, 1f, l.GateZ)), "gap beside the lawn wall");
        }

        [UnityTest]
        public IEnumerator TheBossArenaIsEnclosed()
        {
            yield return BuildPath();
            var l = BackyardPathLayout.Default;

            Assert.IsTrue(BlockedAt(new Vector3(0f, 1f, l.ArenaEndZ + 0.6f)), "no arena back wall");
            float az = l.ArenaCenter.z;
            Assert.IsTrue(BlockedAt(new Vector3(-(l.ArenaHalfWidth + 0.6f), 1f, az)), "no left arena wall");
            Assert.IsTrue(BlockedAt(new Vector3(l.ArenaHalfWidth + 0.6f, 1f, az)), "no right arena wall");
            // And it's clear inside — the boss needs room to charge and drop AoEs.
            Assert.IsFalse(BlockedAt(new Vector3(0f, 1f, az)), "something is standing in the boss arena");
        }
    }
}
