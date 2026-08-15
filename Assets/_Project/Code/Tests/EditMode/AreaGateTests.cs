using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The gated-arena mechanic's SHAPE (v0.5 recut spec §1, WV-222): the map format and validator
    /// treat an area gate exactly the way they treat the scene-adopted <see cref="EntityKind.Gate"/> —
    /// it seals a doorway and nothing else may stand where it stands. <see cref="AreaGatePlayTests"/>
    /// proves what the built gate actually DOES (HP, primary-only damage, opening).
    ///
    /// Landed as a reusable engine capability with its own fixture map — the shipped
    /// <c>backyard_slice.json</c> is untouched (its boss fight's fate is still Lee's call).
    /// </summary>
    public sealed class AreaGateTests
    {
        /// <summary>Two rooms sealed by one area gate — the smallest fixture that exercises the kind.</summary>
        private static MapData TwoAreas(float doorway = 4f)
        {
            return new MapData
            {
                name = "Two Areas",
                wallHeight = 3f,
                wallThickness = 1f,
                zones = new[]
                {
                    new MapZone { id = "area1", type = "entry", x = 0f, z = -10f, width = 20f, depth = 20f },
                    new MapZone { id = "area2", type = "open",  x = 0f, z =  10f, width = 20f, depth = 20f },
                },
                links = new[] { new MapLink { from = "area1", to = "area2", doorway = doorway, gate = "gate1" } },
                entities = new[]
                {
                    new MapEntity { id = "start", kind = "playerSpawn", x = 0f, z = -10f },
                    new MapEntity { id = "gate1", kind = "areaGate", x = 0f, z = 0f, height = 3f, depth = 0.6f },
                },
            };
        }

        /// <summary><paramref name="areaCount"/> rooms in a straight chain, each junction sealed by its
        /// own area gate — the literal shape spec §1 asks for ("10 sequential rooms; each next room
        /// sealed by a gate"). The last room is marked a boss zone purely so <see cref="MapValidation"/>
        /// Reachable actually walks the whole chain rather than taking the fixture's word for it.</summary>
        private static MapData SequentialAreas(int areaCount, float doorway = 4f)
        {
            var zones = new MapZone[areaCount];
            var entities = new List<MapEntity> { new MapEntity { id = "start", kind = "playerSpawn", x = 0f, z = 0f } };
            var links = new List<MapLink>();

            for (int i = 0; i < areaCount; i++)
            {
                string type = i == 0 ? "entry" : i == areaCount - 1 ? "boss" : "open";
                zones[i] = new MapZone { id = $"area{i + 1}", type = type, x = 0f, z = i * 20f, width = 20f, depth = 20f };
            }

            for (int i = 0; i < areaCount - 1; i++)
            {
                string gateId = $"gate{i + 1}";
                links.Add(new MapLink { from = $"area{i + 1}", to = $"area{i + 2}", doorway = doorway, gate = gateId });
                entities.Add(new MapEntity
                {
                    id = gateId, kind = "areaGate", x = 0f, z = i * 20f + 10f, height = 3f, depth = 0.6f,
                });
            }

            return new MapData
            {
                name = "Sequential Areas", wallHeight = 3f, wallThickness = 1f,
                zones = zones, links = links.ToArray(), entities = entities.ToArray(),
            };
        }

        [Test]
        public void Validation_AcceptsTenSequentialAreaGates()
        {
            MapData map = SequentialAreas((int)ArenaTuning.DefaultAreaCount);

            Assert.IsTrue(MapValidation.Validate(map, out string why), why);
            Assert.AreEqual(10, map.zones.Length, "spec §1 asks for 10 sequential outdoor rooms");
            Assert.AreEqual(9, MapValidation.Kind(map, EntityKind.AreaGate).Count,
                "10 rooms in a chain need 9 gates between them");
        }

        [Test]
        public void Validation_RejectsAnAreaGateThatFillsNoDoorway()
        {
            MapData map = TwoAreas();
            map.links[0].gate = "";   // 'gate1' still exists, but no link names it any more

            Assert.IsFalse(MapValidation.Validate(map, out string why));
            StringAssert.Contains("area gate 'gate1' does not fill any doorway", why);
        }

        [Test]
        public void Validation_RejectsABossRoomUnreachableThroughABrokenAreaGateChain()
        {
            MapData map = SequentialAreas(3);

            // Cut the last link AND its gate — not just the link, or the failure we get is "gate2 fills
            // no doorway" rather than the unreachable-boss-room claim this test is actually after.
            var links = new List<MapLink>(map.links);
            links.RemoveAt(1);
            map.links = links.ToArray();

            var entities = new List<MapEntity>(map.entities);
            entities.RemoveAll(e => e.id == "gate2");
            map.entities = entities.ToArray();

            Assert.IsFalse(MapValidation.Validate(map, out string why));
            StringAssert.Contains("cannot be walked to", why);
        }

        [Test]
        public void AnAreaGate_IsAsWideAsTheDoorwayItFills_PlusTheWallEitherSide()
        {
            MapData map = TwoAreas(doorway: 5f);
            MapEntity gate = map.Entity("gate1");

            Assert.AreEqual(5f + map.wallThickness * 2f, MapRuntime.SealWidth(map, gate), 1e-3,
                "the area gate did not follow its doorway — it would leave a gap beside itself");
        }

        [Test]
        public void TheFormat_ReadsAreaGateCaseAndSeparatorInsensitively()
        {
            Assert.AreEqual(EntityKind.AreaGate, MapEnums.Entity("areaGate"));
            Assert.AreEqual(EntityKind.AreaGate, MapEnums.Entity("area_gate"));
            Assert.AreEqual(EntityKind.AreaGate, MapEnums.Entity("AREA-GATE"));
        }

        [Test]
        public void TheFormat_SurvivesARoundTripThroughJson()
        {
            MapData before = TwoAreas();
            MapData after = MapLibrary.Parse(MapLibrary.ToJson(before));

            Assert.IsNotNull(after);
            Assert.AreEqual(EntityKind.AreaGate, after.Entity("gate1").Kind);
        }

        // --- MV-320: gates should open away from Max, not toward him ---

        [Test]
        public void AwayFromPlayerDirection_PointsFromTheNearRoomToTheFarRoom()
        {
            MapData map = TwoAreas(); // area1 (z=-10) -> gate1 (z=0) -> area2 (z=10)
            MapEntity gate = map.Entity("gate1");

            Vector3 away = MapRuntime.AwayFromPlayerDirection(map, gate);

            Assert.Greater(away.z, 0f, "area2 sits at +Z of area1, so 'away' should point toward +Z");
        }

        [Test]
        public void AwayFromPlayerDirection_IsZeroForAnUnlinkedGate()
        {
            MapData map = TwoAreas();
            map.links[0].gate = ""; // gate1 no longer fills any doorway

            Vector3 away = MapRuntime.AwayFromPlayerDirection(map, map.Entity("gate1"));

            Assert.AreEqual(Vector3.zero, away);
        }

        [Test]
        public void SwingSign_FlipsWhenTheFarRoomSitsOnTheSameSideTheDoorDefaultsToSwingingAwayFrom()
        {
            // A positive-angle hinge swing always sweeps toward -forward (AreaGate.SwingSign's own
            // doc) — so when the far room is ahead on the +forward side, the default (+1) would swing
            // the door back into the near room and the sign must flip to -1.
            Assert.AreEqual(-1f, AreaGate.SwingSign(Vector3.forward, Vector3.forward));
        }

        [Test]
        public void SwingSign_KeepsTheDefaultWhenTheFarRoomIsAlreadyOnTheSwingsNaturalSide()
        {
            Assert.AreEqual(1f, AreaGate.SwingSign(-Vector3.forward, Vector3.forward));
        }

        [Test]
        public void SwingSign_ADoubledBackChainNeedsTheOppositeSignFromAStraightOne()
        {
            // world1_config: g1 (area1 -> area2) runs +X, g3 (area3 -> area4) runs -X on the SAME
            // E/W-wall gate orientation (forward = world +X for every E/W gate, MapRuntime.BuildAreaGate)
            // — a single hardcoded sign would get one of the two backwards.
            Vector3 ewForward = Vector3.right;

            float straight = AreaGate.SwingSign(Vector3.right, ewForward);   // g1-style: away runs +X
            float doubledBack = AreaGate.SwingSign(-Vector3.right, ewForward); // g3-style: away runs -X

            Assert.AreNotEqual(straight, doubledBack);
        }

        [Test]
        public void SwingSign_DefaultsToPositiveWithNoMapContext()
        {
            Assert.AreEqual(1f, AreaGate.SwingSign(Vector3.zero, Vector3.forward));
            Assert.AreEqual(1f, AreaGate.SwingSign(Vector3.zero, Vector3.right));
        }

        // --- MV-323: ambient spawns should be biased away from the door a room is entered through ---

        [Test]
        public void EntryDirection_PointsFromTheNearRoomToTheFarRoom()
        {
            MapData map = TwoAreas(); // area1 (z=-10) -> gate1 (z=0) -> area2 (z=10)

            Vector3 entry = MapRuntime.EntryDirection(map, "area2");

            Assert.Greater(entry.z, 0f, "area2 sits at +Z of area1, so 'entry' should point toward +Z");
        }

        [Test]
        public void EntryDirection_IsZeroWhenNoLinkLeadsIntoTheZone()
        {
            MapData map = TwoAreas();

            Vector3 entry = MapRuntime.EntryDirection(map, "area1"); // nothing links INTO area1

            Assert.AreEqual(Vector3.zero, entry);
        }

        // --- MV-378: a fresh gate has to be a REAL physical obstruction, not just a damageable prop ---

        [Test]
        public void TheBuiltGate_HasASolidNonTriggerColliderExactlyWhereMaxWouldWalk()
        {
            MapData map = TwoAreas(doorway: 4f);
            var root = new GameObject("Physical Gate Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                GameObject gate = built.Actors["gate1"];
                Assert.IsNotNull(gate, "the map built no gate1 actor at all");

                var col = gate.GetComponent<Collider>();
                Assert.IsNotNull(col, "the built gate carries no Collider -- nothing can ever stop Max at it");
                Assert.IsTrue(col.enabled, "the built gate's collider starts disabled");
                Assert.IsFalse(col.isTrigger,
                    "the built gate's collider is trigger-only -- a CharacterController passes straight " +
                    "through a trigger, it does not stop at one");

                // autoSyncTransforms is off project-wide (DynamicsManager.asset), so a transform moved
                // by script (exactly what Spawn() just did) is not guaranteed visible to a physics query
                // in the same frame without an explicit sync.
                Physics.SyncTransforms();
                Collider[] hits = Physics.OverlapBox(gate.transform.position, Vector3.one * 0.05f,
                                                      gate.transform.rotation);
                Assert.Contains(col, hits,
                    "a physics query at the gate's own centre does not find its collider -- the doorway " +
                    "reads as empty space to anything that queries physics there, e.g. a CharacterController");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // --- MV-386: opening a gate must drop the doorway's threshold, but the physical leaf has to
        // stay solid -- MV-378's fix only ever disabled the one collider both jobs shared, so a fully
        // open gate leaf had zero collision forever. That split (AreaGate._thresholdCollider /
        // _leafCollider, see AreaGate.cs) is deliberately NOT covered by an EditMode test here: proving
        // it needs AreaGate.Awake() to have actually run (it builds the threshold collider there) and
        // TakeDamage()/Open() to fire (they need _health, also built in Awake) -- and this project's
        // synchronous EditMode harness does not run a freshly AddComponent'd MonoBehaviour's Awake()
        // within a single [Test] method (confirmed empirically while building this ticket: an Awake-
        // start Debug.Log added for diagnosis never printed, in ANY AreaGate-building EditMode test,
        // including the pre-existing, passing ones above -- they only ever happened to pass because the
        // properties they check, Collider.enabled/isTrigger, match CreatePrimitive's own defaults
        // whether or not Awake ran). This is exactly why MV-378's own author put every Open()/TakeDamage
        // assertion in AreaGatePlayTests (PlayMode) instead, never in this file -- PlayMode's
        // [UnitySetUp] yield return null actually ticks a frame first. The real regression coverage for
        // this split is PlayMode: AreaGatePlayTests.SustainedPrimaryFire_BreaksTheGateAtExactlyItsHp_NotBefore
        // now asserts leaf.enabled stays true and ThresholdObject's collider goes false after Open() --
        // CI runs it (CC_AUTONOMY.md: this worker never authors or runs PlayMode itself).
    }
}
