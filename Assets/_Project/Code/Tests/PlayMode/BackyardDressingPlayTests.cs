using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The dressed Backyard, built for real (YT-75).
    ///
    /// The EditMode tests prove the PLAN is safe. This proves the yard that gets built from it is —
    /// that a garden's worth of trees, fences and flower beds went in and the arena underneath still
    /// behaves exactly as it did when it was three grey rooms. Every one of these assertions is a way
    /// the art pass could have quietly broken the game: a prop with a collider, a fence you clip, a
    /// cover block whose hit box moved out from under the tree the player learned to hide behind.
    /// </summary>
    public sealed class BackyardDressingPlayTests
    {
        private GameObject _path;
        private GameObject _dressing;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_dressing != null) Object.Destroy(_dressing);
            if (_path != null) Object.Destroy(_path);
            yield return null;
        }

        private IEnumerator BuildYard()
        {
            _path = new GameObject("Path", typeof(BackyardPath));
            yield return null;                              // BackyardPath.Awake builds the blockout

            _dressing = new GameObject("Dressing", typeof(BackyardDressing));
            yield return null;                              // BackyardDressing.Awake dresses it

            Physics.SyncTransforms();
            yield return null;                              // the stripped colliders are really gone
        }

        private BackyardDressing Dressing => _dressing.GetComponent<BackyardDressing>();

        /// <summary>Anything solid and person-height standing at this point.</summary>
        private static bool BlockedAt(Vector3 p) =>
            Physics.OverlapSphere(p, 0.4f).Any(c => c.bounds.size.y >= 1.5f);

        [UnityTest]
        public IEnumerator TheYardGetsDressed()
        {
            yield return BuildYard();

            Assert.Greater(Dressing.PropCount, 100,
                "almost nothing was placed — the kit prefabs are probably missing from Resources");
        }

        [UnityTest]
        public IEnumerator NoPieceOfDressingCanBlockAnything()
        {
            yield return BuildYard();

            var colliders = _dressing.GetComponentsInChildren<Collider>(true);
            Assert.IsEmpty(colliders,
                "dressing is scenery: the first collider that gets in here is the one that walls off " +
                "the lawn and nobody knows why");
        }

        [UnityTest]
        public IEnumerator TheMissionLineIsStillWalkable()
        {
            yield return BuildYard();
            var l = BackyardPathLayout.Default;

            // The stepping stones are laid down this exact line. If one of them ever arrives with a
            // collider, this is what says so.
            for (float z = l.StartZ + 1f; z <= l.GateZ - 1f; z += 2f)
                Assert.IsFalse(BlockedAt(new Vector3(0f, 1f, z)), $"the dressing blocked the path at z={z}");
        }

        [UnityTest]
        public IEnumerator TheLawnIsStillARoomYouCanCircleIn()
        {
            yield return BuildYard();
            var l = BackyardPathLayout.Default;

            // YT-68's whole point, re-checked with a garden in it: the fight room did not get narrower
            // because we planted it out.
            float z = l.LawnStartZ + 2f;
            for (float x = -(l.LawnHalfWidth - 0.6f); x <= l.LawnHalfWidth - 0.6f; x += 1f)
                Assert.IsFalse(BlockedAt(new Vector3(x, 1f, z)), $"the lawn is blocked at x={x}");
        }

        [UnityTest]
        public IEnumerator TheCoverStillBlocksExactlyWhatItBlocked()
        {
            yield return BuildYard();

            foreach (CoverPiece piece in _path.GetComponent<BackyardPath>().CoverPieces)
            {
                var probe = new Vector3(piece.Cover.CenterXz.x, 1f, piece.Cover.CenterXz.y);
                Assert.IsTrue(Physics.OverlapSphere(probe, 0.3f).Length > 0,
                    $"{piece.Cover.Name} lost its collider — the player would run through the tree");

                var renderer = piece.Body.GetComponent<Renderer>();
                Assert.IsFalse(renderer.enabled,
                    $"{piece.Cover.Name} is still showing its greybox through the model standing in it");
            }
        }

        [UnityTest]
        public IEnumerator TheKitKeepsItsOwnMaterials()
        {
            yield return BuildYard();

            // The rendering layer repaints every world surface it can see. If the props aren't marked,
            // the entire garden comes out one flat colour — and it looks deliberate, so nobody notices.
            var props = _dressing.GetComponentsInChildren<MeshRenderer>()
                                 .Where(r => r.name != "Yard Surround");

            foreach (MeshRenderer r in props)
                Assert.IsNotNull(r.GetComponentInParent<KeepsOwnMaterial>(),
                    $"{r.name} would be repainted by the world-material pass");
        }

        [UnityTest]
        public IEnumerator TheGroundReachesTheTreesBeyondTheFence()
        {
            yield return BuildYard();
            var l = BackyardPathLayout.Default;

            var surround = _dressing.transform.Find("Yard Surround");
            Assert.IsNotNull(surround, "no ground outside the fence — the trees are standing on sky");

            var bounds = surround.GetComponent<MeshRenderer>().bounds;
            foreach (Transform t in _dressing.transform.Find("Kit Props"))
            {
                if (Mathf.Abs(t.position.x) <= l.ArenaHalfWidth) continue;   // inside the yard already
                Assert.IsTrue(bounds.Contains(new Vector3(t.position.x, bounds.center.y, t.position.z)),
                    $"{t.name} at {t.position} is standing off the edge of the world");
            }
        }
    }
}
