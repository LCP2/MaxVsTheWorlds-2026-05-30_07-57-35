using System.Collections;
using System.Collections.Generic;
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

        private static MapData Map => MapLibrary.Load(MapLibrary.BackyardSlice);

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
            MapData map = Map;

            // Straight up the middle of the lawn, from where Max starts to the gate: no cover is
            // authored on that line, so nothing at all may stand there.
            MapEntity spawn = map.First(EntityKind.PlayerSpawn);
            MapEntity gate = map.First(EntityKind.Gate);

            for (float z = spawn.z + 1f; z <= gate.z - 1f; z += 2f)
                Assert.IsFalse(BlockedAt(new Vector3(spawn.x, 1f, z)),
                    $"the dressing blocked the path at z={z}");

            // And along the whole route to the factory — which turns into the shed and passes BEHIND a
            // hedge on the way — nothing the dressing placed may be in the way. The cover it threads
            // past is level geometry and is meant to be there; a stepping stone that arrived with a
            // collider is not, and this is what says so.
            List<Vector2> route = BackyardDressingSet.Route(map);
            Assert.GreaterOrEqual(route.Count, 2, "the map has no route from the spawn to the factory");

            for (int i = 0; i + 1 < route.Count; i++)
            for (float t = 0f; t <= 1f; t += 0.05f)
            {
                Vector2 xz = Vector2.Lerp(route[i], route[i + 1], t);

                foreach (Collider c in Physics.OverlapSphere(new Vector3(xz.x, 1f, xz.y), 0.4f))
                    Assert.IsFalse(c.transform.IsChildOf(_dressing.transform),
                        $"{c.name} is a piece of dressing standing on the mission line at {xz}");
            }
        }

        [UnityTest]
        public IEnumerator TheLawnIsStillARoomYouCanCircleIn()
        {
            yield return BuildYard();
            MapZone lawn = Map.Zone("lawn");

            // YT-68's whole point, re-checked with a garden in it: the fight room did not get narrower
            // because we planted it out.
            float z = lawn.ZMin + 2f;
            for (float x = lawn.XMin + 0.6f; x <= lawn.XMax - 0.6f; x += 1f)
                Assert.IsFalse(BlockedAt(new Vector3(x, 1f, z)), $"the lawn is blocked at x={x}");
        }

        /// <summary>The rooms that did not exist when the dressing was hand-listed. They get fenced
        /// and planted like everything else — and, like everything else, nothing that goes in them can
        /// be walked into.</summary>
        [UnityTest]
        public IEnumerator TheShedAndTheNookAreDressedAndStillWalkable()
        {
            yield return BuildYard();
            MapData map = Map;

            var kit = _dressing.transform.Find("Kit Props");
            Assert.IsNotNull(kit, "nothing was placed at all");

            foreach (string id in new[] { "shed", "nook" })
            {
                MapZone zone = map.Zone(id);

                // Half a metre out, because a fence panel is sunk into the wall it faces and so stands
                // a hand's breadth outside the room it belongs to.
                Rect f = zone.Footprint;
                var around = new Rect(f.xMin - 0.5f, f.yMin - 0.5f, f.width + 1f, f.height + 1f);

                int placed = 0;
                foreach (Transform t in kit)
                    if (around.Contains(new Vector2(t.position.x, t.position.z))) placed++;

                Assert.Greater(placed, 8, $"'{id}' was left bare — the dressing is not following the map");

                // …and nothing the DRESSING placed is standing in the middle of the room.
                //
                // Asked of the dressing specifically, not of "is anything there". The shed has a
                // machine standing in the middle of it — that is the objective, it arrived with the
                // map (YT-92), and it is supposed to be in the way. What must never be in the way is a
                // shrub.
                foreach (Collider c in Physics.OverlapSphere(new Vector3(zone.x, 1f, zone.z), 0.4f))
                    Assert.IsFalse(c.transform.IsChildOf(_dressing.transform),
                        $"'{c.name}' is a piece of dressing standing in the middle of '{id}'");
            }
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
            Rect yard = Map.Bounds();

            var surround = _dressing.transform.Find("Yard Surround");
            Assert.IsNotNull(surround, "no ground outside the fence — the trees are standing on sky");

            var bounds = surround.GetComponent<MeshRenderer>().bounds;
            foreach (Transform t in _dressing.transform.Find("Kit Props"))
            {
                var at = new Vector2(t.position.x, t.position.z);
                if (yard.Contains(at)) continue;   // inside the yard already

                Assert.IsTrue(bounds.Contains(new Vector3(at.x, bounds.center.y, at.y)),
                    $"{t.name} at {t.position} is standing off the edge of the world");
            }
        }
    }
}
