using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1450: a13's west gate g12 swings its solid leaf 100 degrees into a 3 m corridor, which used to
    /// leave 0.05 m and block the route into Lee's nested maze. The maze was translated 2 m east (data
    /// only). This builds the City, breaks g12, finishes the hinge swing, and sweeps a Bruiser-sized
    /// capsule (r 0.55) up the corridor along x = 90.0. Guards MV-1450.
    /// </summary>
    public sealed class MV1450A13DoorClearanceTests
    {
        private static readonly (string, float, float)[] Expected =
        {
            ("a13_c28", 116.15f, 208.53f),
            ("a13_c33", 91.5f, 210.5f),
            ("a13_c34", 103.5f, 197.5f),
            ("a13_c35", 114.5f, 212.5f),
            ("a13_c36", 96.5f, 210.5f),
            ("a13_c37", 103.5f, 200.5f),
            ("a13_c38", 109.5f, 212.5f),
            ("a13_c39", 101.5f, 209.5f),
            ("a13_c40", 103.5f, 203.5f),
            ("a13_c41", 104.5f, 212.5f),
            ("a13_c42", 100.5f, 220.5f),
            ("a13_c43", 100.5f, 223.5f),
            ("a13_c44", 102.5f, 226.5f),
            ("a13_c45", 91.5f, 228.5f),
            ("a13_w1", 92.5f, 199.5f),
            ("a13_w2", 92.5f, 221.5f),
            ("a13_w3", 93.5f, 198.5f),
            ("a13_w4", 93.5f, 222.5f),
            ("a13_w5", 97.5f, 202.5f),
            ("a13_w6", 97.5f, 218.5f),
            ("a13_w7", 98.5f, 201.5f),
            ("a13_w8", 98.5f, 219.5f),
            ("a13_w9", 102.5f, 219.5f),
            ("a13_w10", 103.5f, 218.5f),
            ("a13_w11", 107.5f, 201.5f),
            ("a13_w12", 107.5f, 222.5f),
            ("a13_w13", 108.5f, 202.5f),
            ("a13_w14", 108.5f, 221.5f),
            ("a13_w15", 112.5f, 198.5f),
            ("a13_w16", 112.5f, 225.5f),
            ("a13_w17", 113.5f, 199.5f),
            ("a13_w18", 113.5f, 224.5f),
            ("a13_rep1_pylon2", 121.223374f, 207.597151f),
            ("a13_rep1_pylon1", 102.9f, 205.5f),
            ("a13_rep2_pylon1", 121.292592f, 219.325635f),
            ("a13_rep2_pylon2", 103.06f, 207.14f)
        };

        private static Transform FindById(Transform root, string id)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == id) return t;
            return null;
        }

        [Test]
        public void A13OpenedWestDoorLeavesTheCorridorIntoTheMazeWalkable()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(cfg, "City config failed to load");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            Assert.IsTrue(MapValidation.Validate(map, out string vReason), vReason);

            var root = new GameObject("MV1450 Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                Assert.IsTrue(built.Actors.TryGetValue("g12", out GameObject g12) && g12 != null, "g12 not built");
                var gate = g12.GetComponent<AreaGate>();
                gate.ForceOpen();
                gate.Tick(10f); // run the hinge swing to completion
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide

                int blocked = 0;
                float firstBlockedZ = float.NaN;
                for (float z = 205f; z <= 220f; z += 0.1f)
                {
                    Vector3 p1 = new Vector3(90f, 0.65f, z), p2 = new Vector3(90f, 1.45f, z);
                    if (Physics.CheckCapsule(p1, p2, 0.55f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (blocked == 0) firstBlockedZ = z;
                        blocked++;
                    }
                }
                Assert.AreEqual(0, blocked,
                    $"capsule r0.55 along x=90 blocked at {blocked} steps, first at z={firstBlockedZ:F1}");

                // Built a13 cover and pylons sit within 0.05 m of the data file's positions.
                foreach (var (id, x, z) in Expected)
                {
                    Transform t = FindById(root.transform, id);
                    Assert.IsNotNull(t, $"{id} not built");
                    Vector3 pos = t.position;
                    Assert.LessOrEqual(Mathf.Abs(pos.x - x), 0.05f, $"{id} x {pos.x} vs {x}");
                    Assert.LessOrEqual(Mathf.Abs(pos.z - z), 0.05f, $"{id} z {pos.z} vs {z}");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

    }
}
